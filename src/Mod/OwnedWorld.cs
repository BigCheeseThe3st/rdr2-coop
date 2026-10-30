// The area this game runs: tells the players sharing it about the NPCs near
// any of them, and applies their hits to the real NPCs. This game is the only
// one that decides what those NPCs do.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class OwnedWorld
    {
        readonly EntityIds _ids = new EntityIds();
        readonly int _intervalMs;
        uint _seq;
        long _lastSendMs;
        int _myHorse, _lastMount;
        HashSet<int> _vehicles = new HashSet<int>();
        long _lastCountLogMs;
        readonly List<int> _trainIds = new List<int>();   // engines sent last round, kept in the id map
        // Shots are counted every frame: IS_PED_SHOOTING is true only around
        // the frame a bullet leaves, which 20 Hz snapshots would mostly miss.
        List<int> _tracked = new List<int>();
        readonly Dictionary<int, byte> _shots = new Dictionary<int, byte>();
        readonly Dictionary<int, long> _lastShotMs = new Dictionary<int, long>();
        readonly HashSet<int> _wasShooting = new HashSet<int>();

        public OwnedWorld(int hz) { _intervalMs = 1000 / Math.Max(1, hz); }

        /// <summary>Bubbles: what our mirrors of other games made (copies, their wagons, stand-in drivers, trains),
        /// set each tick. Never ours to send (it would echo back to its owner), never deleted as a stray copy.</summary>
        public static readonly HashSet<int> Skip = new HashSet<int>();

        // followers: the players showing our area, with their puppets here; everyone: every player's puppet here.
        public void Tick(ServerLink link, IDictionary<byte, Puppet> followers, IEnumerable<Puppet> everyone, IList<Vec3> followerPositions, long now)
        {
            CoopScript.Trace("owned: shots");
            CountShots(now);
            CoopScript.Trace("owned: stealth");
            if (MissionProbe.StoryMission) WatchForGuests(followers.Values, now);
            CoopScript.Trace("owned: scan");
            if (now - _lastSendMs < _intervalMs || followers.Count == 0) return;
            _lastSendMs = now;

            var me = Game.Player.Character;
            int meH = me.Handle;
            // The others already draw our horse under our puppet, and their own
            // characters as they are; none of those are NPCs.
            int myMount = me.IsOnMount && me.CurrentMount != null ? me.CurrentMount.Handle : 0;
            _myHorse = Game.Player.ActiveHorse?.Handle ?? 0;
            _lastMount = PED._GET_LAST_MOUNT(meH);   // any horse we got off (stolen, hired): the followers' stand-in horse waits for it too
            var skip = new HashSet<int> { meH, myMount };
            foreach (var p in everyone) { skip.Add(p.Handle); skip.Add(p.HorseHandle); }   // a player who isn't following us (paused, say) is no NPC either
            skip.UnionWith(WaitingHorses.All);   // partners' waiting horses, wherever those partners are now
            Carrying.Skip(skip);   // carried things travel on the stand-ins instead

            var scanAt = followerPositions.Select(CoopScript.ToGame).Concat(new[] { me.Position }).ToList();
            var candidates = new List<Candidate>();
            foreach (int h in EntityScan.Peds(scanAt, EntitySelector.Radius))
            {
                if (skip.Contains(h) || !Peds.Exists(h) || PED.IS_PED_A_PLAYER(h)) continue;
                if (Skip.Contains(h)) continue;   // a linked game's copy (bubbles): theirs, not ours
                if (MirrorWorld.IsCopyGroup(h)) { Peds.Delete(h); continue; }   // a leftover copy (live ones are in Skip, above) is not one of our NPCs: never sent, removed
                // Harnessed draft horses come with the copy of their wagon.
                if (PED._GET_VEHICLE_DRAFT_HORSE_IS_ATTACHED_TO(h) != 0) continue;
                // A card table's players stay in our game (each game seats its own; copies would sit on theirs), unless they fight.
                if (TableGames.CardPlayer(h) && !PED.IS_PED_IN_COMBAT(h, 0)) continue;
                candidates.Add(new Candidate
                {
                    Handle = h,
                    Position = CoopScript.ToNet(Peds.Position(h)),
                    MountHandle = PED.IS_PED_ON_MOUNT(h) ? PED.GET_MOUNT(h) : 0,
                    Dead = ENTITY.IS_ENTITY_DEAD(h),
                    Hostile = IsHostile(h, meH, followers.Values),
                    Key = MissionRoles.Known(h) || Cutscenes.HostParts.Contains(h),
                });
            }

            var points = new List<Vec3>(followerPositions) { CoopScript.ToNet(me.Position) };
            // Bubbles: every linked game sends its own world, so each sends less the more there are (all of it to one, half to three).
            var picked = EntitySelector.Pick(candidates, points, MirrorWorld.Bubbles ? WorldSnapshot.MaxEntities * 2 / (followers.Count + 1) : WorldSnapshot.MaxEntities);

            CoopScript.Trace("owned: vehicles");
            // Vehicles: ours and the followers' first, then those carrying NPCs we send.
            var playerVehicles = new HashSet<int> { Peds.VehicleOf(meH) };
            foreach (var p in followers.Values) playerVehicles.Add(Peds.VehicleOf(p.Handle));
            var carriers = new HashSet<int>(picked.Select(Peds.VehicleOf));
            var vehicleCandidates = new List<VehicleCandidate>();
            var allVehicles = EntityScan.Vehicles(scanAt, 300f).Where(v => !Skip.Contains(v)).ToList();   // wide: a train's engine can be far from its carriages; never a linked game's copies
            foreach (int vh in allVehicles)
            {
                if (!Peds.Exists(vh) || VEHICLE.IS_THIS_MODEL_A_TRAIN(ENTITY.GET_ENTITY_MODEL(vh))) continue;   // trains: later
                vehicleCandidates.Add(new VehicleCandidate
                {
                    Handle = vh,
                    Position = CoopScript.ToNet(Peds.Position(vh)),
                    HasPlayer = playerVehicles.Contains(vh),
                    HasPickedPed = carriers.Contains(vh),
                });
            }
            var pickedVehicles = VehicleSelector.Pick(vehicleCandidates, points, WorldSnapshot.MaxVehicles);
            _vehicles = new HashSet<int>(pickedVehicles);
            _ids.KeepOnly(picked.Concat(pickedVehicles).Concat(_trainIds).ToList());
            _trainIds.Clear();
            _tracked = picked;
            foreach (var h in _shots.Keys.Where(h => !picked.Contains(h)).ToList())
            {
                _shots.Remove(h);
                _lastShotMs.Remove(h);
                _wasShooting.Remove(h);
            }
            var byHandle = new Dictionary<int, Candidate>();
            foreach (var c in candidates) byHandle[c.Handle] = c;

            var trains = DescribeTrains(allVehicles, points);   // first: whoever sits in an engine rides in its copy
            var list = new List<EntityState>(picked.Count);
            foreach (int h in picked) list.Add(Describe(h, byHandle[h], meH, link.MyId, followers, now));
            var vehicles = pickedVehicles.Select(DescribeVehicle).ToList();
            if (now - _lastCountLogMs > 5000)
            {
                _lastCountLogMs = now;
                CoopScript.Log($"owned area: {candidates.Count} NPC candidates -> {picked.Count} sent; " +
                    $"{vehicleCandidates.Count} vehicle candidates (of {allVehicles.Count} vehicles; pool list {World.GetAllVehicles().Length}) -> {pickedVehicles.Count} sent; " +
                    $"players at {string.Join(" ", points)}; followers {followers.Count}");
            }
            CoopScript.Trace("owned: send");
            link.SendWorld(new WorldSnapshot { Seq = ++_seq, Entities = list, Vehicles = vehicles, Trains = trains });
            SendLooks(link, picked, list, followers.Count, now);
            SendNames(link, picked, list, now);
        }

        // Names enRage gives our NPCs (nemeses, people met), for the partners'
        // name tags: every 3 s when they changed.
        string _namesSent = "";
        long _lastNamesMs;

        void SendNames(ServerLink link, List<int> handles, List<EntityState> states, long now)
        {
            if (now - _lastNamesMs < 3000) return;
            _lastNamesMs = now;
            if (!(AppDomain.CurrentDomain.GetData("rdr2coop.nameOf") is Func<int, string> nameOf)) return;
            var lines = new List<string>();
            for (int i = 0; i < states.Count && i < handles.Count; i++)
            {
                if (states[i].Dead) continue;
                string n = nameOf(handles[i]);
                if (!string.IsNullOrEmpty(n)) lines.Add(states[i].Id + "=" + n.Replace("\n", " "));
            }
            string text = string.Join("\n", lines);
            if (text == _namesSent) return;
            _namesSent = text;
            link.SendNotice(Protocol.Broadcast, NoticeKind.Names, text);
        }

        // Full looks (clothes, hats, faces, a horse's saddle and tack): too big for
        // every snapshot, so each NPC's once, then again only if it changed (one
        // re-check per second, round the list). All again when a player joins.
        readonly Dictionary<ushort, uint> _lookSent = new Dictionary<ushort, uint>();
        int _lookFollowers = -1, _lookCheck;
        long _lastLookCheckMs;

        void SendLooks(ServerLink link, List<int> handles, List<EntityState> states, int followers, long now)
        {
            if (followers != _lookFollowers || _lookSent.Count > 400) { _lookSent.Clear(); _lookFollowers = followers; }
            int recheck = now - _lastLookCheckMs > 1000 && states.Count > 0 ? _lookCheck++ % states.Count : -1;
            if (recheck >= 0) _lastLookCheckMs = now;
            for (int i = 0; i < states.Count && i < handles.Count; i++)
            {
                var e = states[i];
                if (e.Dead || (_lookSent.ContainsKey(e.Id) && i != recheck)) continue;
                int h = handles[i];
                if (!PED.IS_PED_HUMAN(h) && !PED._IS_THIS_MODEL_A_HORSE(e.Model)) continue;
                var look = Looks.Capture(h);
                uint sig = look.Signature();
                if (_lookSent.TryGetValue(e.Id, out uint had) && had == sig) continue;
                _lookSent[e.Id] = sig;
                link.SendEntityLook(e.Id, look);
                return;   // one per snapshot keeps the extra traffic small
            }
        }

        void CountShots(long now)
        {
            foreach (int h in _tracked)
            {
                bool shooting = Peds.Exists(h) && PED.IS_PED_SHOOTING(h);
                if (shooting && !_wasShooting.Contains(h))
                {
                    _shots[h] = (byte)((_shots.TryGetValue(h, out var n) ? n : 0) + 1);
                    _lastShotMs[h] = now;
                }
                if (shooting) _wasShooting.Add(h); else _wasShooting.Remove(h);
            }
        }

        EntityState Describe(int h, Candidate c, int meH, byte myId, IDictionary<byte, Puppet> followers, long now)
        {
            var e = new EntityState
            {
                Id = _ids.IdFor(h),
                Model = ENTITY.GET_ENTITY_MODEL(h),
                Position = c.Position,
                Heading = ENTITY.GET_ENTITY_HEADING(h),
                Velocity = CoopScript.ToNet(ENTITY.GET_ENTITY_VELOCITY(h, 0)),
                MountId = c.MountHandle != 0 && _ids.TryGet(c.MountHandle, out var mid) ? mid : (ushort)0,
            };
            int max = Math.Max(1, ENTITY.GET_ENTITY_MAX_HEALTH(h, false));
            e.HealthPct = (byte)Math.Max(0, Math.Min(100, ENTITY.GET_ENTITY_HEALTH(h) * 100 / max));
            if (c.Dead) e.Flags |= EntityFlags.Dead;
            if (c.Hostile) e.Flags |= EntityFlags.Hostile;
            if (ENTITY.GET_IS_ANIMAL(h)) e.Flags |= EntityFlags.Animal;
            if (PED.IS_PED_FLEEING(h)) e.Flags |= EntityFlags.Fleeing;
            if (h == _myHorse || h == _lastMount) e.Flags |= EntityFlags.PlayerHorse;
            else if (MAP._DOES_ENTITY_HAVE_BLIP(h)) e.Flags |= EntityFlags.Blipped;
            if (!c.Dead && (MissionProbe.StoryMission || MissionRoles.Known(h)) && MissionAlly(h, meH, c.Hostile, out _)) e.Flags |= EntityFlags.Protected;
            e.Outfit = PED._GET_PED_META_OUTFIT_HASH(h);
            if (!c.Dead) e.Action = ActionOf(h, out e.Scenario);
            int veh = Peds.VehicleOf(h);
            if (veh != 0 && (_vehicles.Contains(veh) || (Sync.Trains && _trainIds.Contains(veh))))   // a wagon we send, or a train's engine
            {
                sbyte seat = Peds.SeatOf(veh, h);
                if (seat != sbyte.MinValue) { e.VehicleId = _ids.IdFor(veh); e.Seat = seat; }
            }

            if (!c.Dead && PED.IS_PED_HUMAN(h))
            {
                e.Weapon = Peds.CurrentWeapon(h);
                e.Shots = _shots.TryGetValue(h, out var shots) ? shots : (byte)0;
                if (_lastShotMs.TryGetValue(h, out var last) && now - last < 1000) e.Flags |= EntityFlags.Shooting;
                if (PED.IS_PED_IN_COMBAT(h, meH)) e.Aim = myId;
                foreach (var kv in followers)
                    if (Peds.Exists(kv.Value.Handle) && PED.IS_PED_IN_COMBAT(h, kv.Value.Handle)) { e.Aim = kv.Key; break; }
            }
            return e;
        }

        const int HandsUpTask = 0;   // CTaskHandsUp in the game's task list

        internal static EntityAction ActionOf(int h, out uint scenario)
        {
            scenario = 0;
            if (PED.IS_PED_HOGTIED(h)) return EntityAction.Hogtied;
            if (PED.IS_PED_RAGDOLL(h)) return EntityAction.Ragdoll;
            if (PED._IS_PED_COWERING(h)) return EntityAction.Cower;
            if (TASK.GET_IS_TASK_ACTIVE(h, HandsUpTask)) return EntityAction.HandsUp;
            if (!PED.IS_PED_USING_ANY_SCENARIO(h)) return EntityAction.None;
            // Benches, rails and counters are scenario points; the rest run in place.
            scenario = (uint)TASK._GET_SCENARIO_POINT_TYPE_PED_IS_USING(h);
            if (scenario == 0) scenario = PED._GET_ACTIVE_DYNAMIC_SCENARIO(h);
            return scenario != 0 ? EntityAction.Scenario : EntityAction.None;
        }

        VehicleState DescribeVehicle(int v)
        {
            uint model = ENTITY.GET_ENTITY_MODEL(v);
            var rot = ENTITY.GET_ENTITY_ROTATION(v, 2);
            var s = new VehicleState
            {
                Id = _ids.IdFor(v),
                Model = model,
                Position = CoopScript.ToNet(Peds.Position(v)),
                Rotation = new Vec3(rot.X, rot.Y, rot.Z),
                Velocity = CoopScript.ToNet(ENTITY.GET_ENTITY_VELOCITY(v, 0)),
            };
            int max = Math.Max(1, ENTITY.GET_ENTITY_MAX_HEALTH(v, false));
            s.HealthPct = (byte)Math.Max(0, Math.Min(100, ENTITY.GET_ENTITY_HEALTH(v) * 100 / max));
            if (VEHICLE.IS_DRAFT_VEHICLE(v)) s.Flags |= VehicleFlags.Draft;
            if (MAP._DOES_ENTITY_HAVE_BLIP(v)) s.Flags |= VehicleFlags.Blipped;
            if (VEHICLE.IS_THIS_MODEL_A_BOAT(model)) s.Flags |= VehicleFlags.Boat;
            if (ENTITY.IS_ENTITY_DEAD(v) || !VEHICLE.IS_VEHICLE_DRIVEABLE(v, false, false)) s.Flags |= VehicleFlags.Destroyed;
            return s;
        }

        // Trains near the players, as configuration + engine position + speed.
        List<TrainState> DescribeTrains(List<int> vehicles, IList<Vec3> points)
        {
            var trainParts = vehicles.Where(v => Peds.Exists(v) && VEHICLE.IS_THIS_MODEL_A_TRAIN(ENTITY.GET_ENTITY_MODEL(v))).ToList();
            if (trainParts.Count == 0) return new List<TrainState>();
            var carriagesOf = new Dictionary<int, List<int>>();
            var isCarriage = new HashSet<int>();
            foreach (int v in trainParts)
            {
                int n = VEHICLE._GET_TRAIN_CARRIAGE_TRAILER_NUMBER(v);
                var list = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    int c = VEHICLE.GET_TRAIN_CARRIAGE(v, i);
                    if (c != 0 && c != v) { list.Add(c); isCarriage.Add(c); }
                }
                carriagesOf[v] = list;
            }
            var result = new List<TrainState>();
            foreach (int engine in trainParts.Where(v => !isCarriage.Contains(v))
                         .OrderBy(v => points.Min(p => Vec3.Distance(CoopScript.ToNet(Peds.Position(v)), p)))
                         .Take(WorldSnapshot.MaxTrains))
            {
                var models = carriagesOf[engine].Select(c => ENTITY.GET_ENTITY_MODEL(c)).ToList();
                uint config = TrainConfigs.For(engine, models);
                if (config == 0) continue;
                var vel = ENTITY.GET_ENTITY_VELOCITY(engine, 0);
                var fwd = ENTITY.GET_ENTITY_FORWARD_VECTOR(engine);
                result.Add(new TrainState
                {
                    Id = _ids.IdFor(engine),
                    Config = config,
                    Position = CoopScript.ToNet(Peds.Position(engine)),
                    Speed = vel.X * fwd.X + vel.Y * fwd.Y + vel.Z * fwd.Z,
                    Direction = VEHICLE._GET_TRAIN_DIRECTION(engine),
                    Cars = (byte)System.Math.Min(255, models.Count),
                });
                _trainIds.Add(engine);
            }
            return result;
        }

        // Our id for a vehicle (ours to send: a player is in it), for the state
        // we broadcast, and the real vehicle behind an id a follower reports.
        public ushort IdForVehicle(int vehicle) => vehicle == 0 ? (ushort)0 : _ids.IdFor(vehicle);
        public int VehicleHandle(ushort id) => id == 0 ? 0 : _ids.HandleFor(id);
        public int PedOf(ushort id) => id == 0 ? 0 : _ids.HandleFor(id);
        /// <summary>The id an entity we send goes by, or 0 when we don't send it.</summary>
        public ushort IdOf(int handle) => _ids.TryGet(handle, out var id) ? id : (ushort)0;

        internal static bool IsHostile(int h, int meH, IEnumerable<Puppet> puppets) =>
            PED.GET_RELATIONSHIP_BETWEEN_PEDS(h, meH) == 5   // 5 = hate
            || PED.IS_PED_IN_COMBAT(h, meH)
            || puppets.Any(p => Peds.Exists(p.Handle) && PED.IS_PED_IN_COMBAT(h, p.Handle));

        // Shared with enRage's co-op patch: which weapon a partner's last hit on
        // an NPC came from (its damage event can't tell for relayed hits).
        static Dictionary<int, uint> HitWeapons
        {
            get
            {
                if (!(AppDomain.CurrentDomain.GetData("rdr2coop.hitWeapons") is Dictionary<int, uint> d))
                    AppDomain.CurrentDomain.SetData("rdr2coop.hitWeapons", d = new Dictionary<int, uint>());
                if (d.Count > 512) d.Clear();
                return d;
            }
        }

        // A hit another player landed on one of our NPCs. Their puppet here is
        // credited, so the NPC (and enRage) blame them, not us.
        // An NPC near us fighting us right now (the host's down state: when the fight's over). Every 500 ms at most.
        bool _combat;
        long _combatAt = -1000;
        public bool CombatNear(RDR2.Math.Vector3 at, float radius, long now)
        {
            if (now - _combatAt < 500) return _combat;
            _combatAt = now;
            int me = PLAYER.PLAYER_PED_ID();
            _combat = EntityScan.Peds(new[] { at }, radius).Any(h => Peds.Exists(h) && !PED.IS_PED_A_PLAYER(h) && !ENTITY.IS_ENTITY_DEAD(h) && PED.IS_PED_IN_COMBAT(h, me));
            return _combat;
        }

        public void ApplyHit(Hit hit, Puppet shooter)
        {
            int ped = _ids.HandleFor(hit.EntityId);
            if (!Peds.Exists(ped) || ENTITY.IS_ENTITY_DEAD(ped)) return;
            if (ped == (Game.Player.ActiveHorse?.Handle ?? 0)) return;   // never our own horse
            int damage = hit.Bone == Hit.HeadBone ? Math.Max((int)hit.Damage, 1000) : hit.Damage;
            int me = Game.Player.Character.Handle;
            int inflictor = shooter?.Handle ?? 0;
            if (MissionProbe.StoryMission || MissionRoles.Known(ped))   // stranger missions and encounters the map knows too
            {
                bool hostile = IsHostile(ped, me, shooter != null ? new[] { shooter } : new Puppet[0]);
                bool ally = MissionAlly(ped, me, hostile, out int rel);
                LogMissionHit(ped, rel, hostile, ally);
                if (ally) return;
                // Missions check "killed by the player" on named enemies; once a
                // fight is on, credit the host. Never before: an unalerted guard
                // hurt "by the player" fails stealth missions.
                if (hostile) inflictor = me;
            }
            HitWeapons[ped] = hit.Weapon;   // enRage's damage event has no weapon for script damage
            PED.APPLY_DAMAGE_TO_PED(ped, damage, 0, hit.Bone, inflictor);
        }

        // Hurting a story mission's ally (or an ally's horse) fails the mission for everyone, so a guest's hits on
        // allies are dropped, and their copies can't be hurt (EntityFlags.Protected). Only allies: mission enemies
        // often aren't "hostile" yet (no hate, not fighting the host), and guests' shots used to do nothing to them (bug #8).
        static bool MissionAlly(int ped, int me, bool hostile, out int rel)
        {
            rel = PED.GET_RELATIONSHIP_BETWEEN_PEDS(ped, me);
            var mapped = MissionRoles.Side(ped);   // the mission map, when it's sure (MissionRoles.cs)
            if (mapped != MissionRole.None) return mapped == MissionRole.Ally && !hostile;
            return !hostile && (rel <= 2 || PED.IS_PED_GROUP_MEMBER(ped, PLAYER.GET_PLAYER_GROUP(PLAYER.PLAYER_ID()), false))
                   && (PED.IS_PED_HUMAN(ped) || PED._IS_THIS_MODEL_A_HORSE(ENTITY.GET_ENTITY_MODEL(ped)));
        }

        // What happened to a guest's hit in a story mission, once per NPC and decision.
        readonly Dictionary<int, bool> _missionHitLogged = new Dictionary<int, bool>();
        void LogMissionHit(int ped, int rel, bool hostile, bool ally)
        {
            if (_missionHitLogged.TryGetValue(ped, out bool was) && was == ally) return;
            if (_missionHitLogged.Count > 256) _missionHitLogged.Clear();
            _missionHitLogged[ped] = ally;
            CoopScript.Log($"mission: guest hit on {ENTITY.GET_ENTITY_MODEL(ped):X8} (relationship {rel}, hostile {hostile}, map {MissionRoles.Side(ped)}, mission entity {ENTITY.IS_ENTITY_A_MISSION_ENTITY(ped)}): " +
                           (ally ? "an ally, ignored" : hostile ? "applied, credited to us (the host)" : "applied, credited to the guest"));
        }

        // A guest hogtied their copy of one of our NPCs: tie up the real one
        // (it just ends up hogtied; nothing happens to our own player).
        public void ApplyAction(GuestAction a, Puppet actor)
        {
            if (a.Kind != ActionKind.Hogtie) return;
            int ped = _ids.HandleFor(a.EntityId);
            if (!Peds.Exists(ped) || ENTITY.IS_ENTITY_DEAD(ped) || PED.IS_PED_HOGTIED(ped)) return;
            // In a story mission only enemies and people on the run: tying up an
            // ally fails it for everyone.
            if (MissionProbe.StoryMission && !PED.IS_PED_FLEEING(ped)
                && !IsHostile(ped, Game.Player.Character.Handle, actor != null ? new[] { actor } : new Puppet[0]))
            {
                CoopScript.Log($"mission: ignored a guest hogtie on non-hostile {ENTITY.GET_ENTITY_MODEL(ped):X8}");
                return;
            }
            TASK.TASK_KNOCKED_OUT_AND_HOGTIED(ped, 0f, 1);
            CoopScript.Log($"hogtied {ENTITY.GET_ENTITY_MODEL(ped):X8} for a guest");
        }

        // Stealth: our mission's scripts only watch for us, so when one of our
        // unfriendly NPCs has a guest in plain sight for a second, it goes for
        // them, which the mission sees as being found out.
        const float SpotRange = 40f;
        const int SpotAfterMs = 1000;
        readonly Dictionary<int, long> _seeingSince = new Dictionary<int, long>();
        long _lastWatchMs, _lastWatchLog;

        void WatchForGuests(IEnumerable<Puppet> guests, long now)
        {
            if (now - _lastWatchMs < 250) return;
            _lastWatchMs = now;
            int me = Game.Player.Character.Handle;
            var ps = guests.Where(p => p.Exists).ToList();
            foreach (int h in _tracked)
            {
                if (!Peds.Exists(h) || ENTITY.IS_ENTITY_DEAD(h) || !PED.IS_PED_HUMAN(h) || PED.IS_PED_IN_COMBAT(h, 0)) { _seeingSince.Remove(h); continue; }
                int rel = PED.GET_RELATIONSHIP_BETWEEN_PEDS(h, me);
                Puppet seen = null;
                foreach (var p in ps)
                {
                    if (Peds.Position(h).DistanceTo(p.Position) > SpotRange) continue;
                    if (PED.IS_TARGET_PED_IN_PERCEPTION_AREA(h, p.Handle, -1f, -1f, -1f, -1f)
                        && ENTITY.HAS_ENTITY_CLEAR_LOS_TO_ENTITY(h, p.Handle, 17)
                        && PED.CAN_PED_SEE_ENTITY(h, p.Handle, true, false) == 1) { seen = p; break; }
                }
                if (seen == null) { _seeingSince.Remove(h); continue; }
                if (rel < 4)   // 4 dislike, 5 hate: only those raise the alarm
                {
                    if (now - _lastWatchLog > 5000) { _lastWatchLog = now; CoopScript.Log($"stealth: {ENTITY.GET_ENTITY_MODEL(h):X8} sees a guest but its relationship to us is {rel}: ignored"); }
                    continue;
                }
                if (!_seeingSince.TryGetValue(h, out long since)) { _seeingSince[h] = now; continue; }
                if (now - since < SpotAfterMs) continue;
                _seeingSince.Remove(h);
                TASK.TASK_COMBAT_PED(h, seen.Handle, 0, 16);
                CoopScript.Log($"stealth: {ENTITY.GET_ENTITY_MODEL(h):X8} spotted a guest");
            }
        }
    }
}
