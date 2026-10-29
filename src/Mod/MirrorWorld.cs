// Showing an area another game runs: our own game stops spawning people, and
// the owner's NPCs are drawn as copies ("mirrors") that follow its snapshots.
// Hits on mirrors go to the owner, which decides who dies.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class MirrorWorld
    {
        sealed class Mirror
        {
            public int Ped;
            public uint Model;
            public uint Weapon;
            public long LastTaskMs;
            public int Group = -1;   // -1 not set yet, 0 neutral copies, 1 hostile
            public uint Outfit;
            public int LastShots = -1;
            public long KillSentMs;      // we killed this copy (fists, a finishing move, anything): when the owner was told
            public bool KillPending;     // ... and the hit loop still has to tell them
            public bool KnockoutLogged;
            public long BusyUntilMs;
            public bool Standing;
            public int Blip;
            public bool HogtieSent;
            public uint LookSig;
            public bool? Guarded;        // can't be hurt here (the owner's horse, a mission ally); null: not set yet
            public EntityAction Action;
            public uint Scenario;
        }

        sealed class MirrorVehicle
        {
            public int Veh;
            public uint Model;
            public int StandIn;        // invisible local driver when the owner's has none
            public long LastDriveMs;
            public long DriverlessSinceMs = -1;
            public long LastSpeedMs;
            public long LastSnapLogMs, LastSnapMs;
            public int Blip;
        }

        // How copy vehicles follow the owner's: "drive" (an AI driver chases
        // the real position) or "glide" (the body follows it exactly by
        // velocity; the horses are only told how fast to pull).
        public static string VehicleMode = "glide";

        // Measurements, logged every 10 s while copies exist.
        double _gapSum; int _gapCount; float _gapMax; int _snaps; long _lastStatsMs;

        sealed class MirrorTrain
        {
            public int Train;
            public uint Config;
            public long LastFixMs;
        }

        const float TrainFixDistance = 40f;
        readonly Dictionary<ushort, MirrorTrain> _trains = new Dictionary<ushort, MirrorTrain>();
        bool _trainsOff;

        const float ClearRadius = 200f;
        const float VehicleSnapDistance = 15f;
        readonly Dictionary<ushort, MirrorVehicle> _vehicles = new Dictionary<ushort, MirrorVehicle>();
        readonly WorldBuffer _buffer;
        readonly Dictionary<ushort, Mirror> _mirrors = new Dictionary<ushort, Mirror>();
        long _hitLogMs, _deadLogMs;   // diagnostics (0.9.0): one line a second at most
        const int KillWaitMs = 3000;  // a copy we killed stays down this long for the owner's game to catch up
        readonly HashSet<uint> _missingModels = new HashSet<uint>();   // not in this game's files: never tried again
        readonly RetryBook _retry = new RetryBook();                     // refused or slow for now: tried again after a wait
        readonly Dictionary<uint, long> _loadingSince = new Dictionary<uint, long>();
        long _lastClearMs;
        readonly HashSet<int> _hidden = new HashSet<int>();
        readonly HashSet<ushort> _taken = new HashSet<ushort>();   // copy horses we got on: ours now, never copied again
        readonly HashSet<int> _horseLogged = new HashSet<int>();   // breeds already logged by the guest clear   // our own script-made people (camp, side quests), hidden while we show the owner's world
        static uint _neutralGroup, _hostileGroup;   // the joaat of fixed names: the same for every instance; made on first use, owner or follower
        readonly Random _rng = new Random();
        byte _myId;

        public MirrorWorld(int smoothingMs) { _buffer = new WorldBuffer(smoothingMs); }

        public void Push(WorldSnapshot w, long at) => _buffer.Push(w, at);
        public long NewestMs => _buffer.NewestMs;

        // owner: the game whose area we show; puppets: every other player here.
        ServerLink _link;
        byte _ownerId;

        /// <summary>Bubbles ([Gameplay] Bubbles, src/Net/Bubbles.cs): one of these per linked player, next to our own
        /// world; nothing of ours is suppressed, cleared or hidden here then (the crowd rule in CoopScript decides).</summary>
        public static bool Bubbles;

        /// <summary>Everything this mirror made (copies, their vehicles, stand-in drivers, trains): our own world never
        /// sends these on (bubbles).</summary>
        public IEnumerable<int> MadeHandles() =>
            _mirrors.Values.Select(m => m.Ped).Concat(_vehicles.Values.SelectMany(v => new[] { v.Veh, v.StandIn })).Concat(_trains.Values.Select(t => t.Train)).Where(h => h != 0);

        public void Tick(ServerLink link, byte owner, IDictionary<byte, Puppet> puppets, long now)
        {
            _link = link;
            _ownerId = owner;
            if (!Bubbles) SuppressAmbient();
            if (!Bubbles && now - _lastClearMs > 1000)
            {
                _lastClearMs = now;
                ClearLocalPeds(puppets.Values);
            }
            _myId = link.MyId;

            // Vehicles before people, so passengers have somewhere to sit.
            var states = _buffer.Sample(now);
            _lastStates = states;
            var drivenByCopy = new HashSet<ushort>(states.Where(e => e.VehicleId != 0 && e.Seat == -1 && !e.Dead).Select(e => e.VehicleId));
            var vseen = new HashSet<ushort>();
            foreach (var v in _buffer.SampleVehicles(now))
            {
                vseen.Add(v.Id);
                UpdateVehicle(v, drivenByCopy.Contains(v.Id), now);
            }
            foreach (var id in _vehicles.Keys.Where(id => !vseen.Contains(id)).ToList()) RemoveVehicle(id);
            LogStats(now);

            // Trains: none of our own while we show someone else's area.
            if (!_trainsOff && !Bubbles) { VEHICLE.SET_RANDOM_TRAINS(false); VEHICLE.DELETE_ALL_TRAINS(); _trainsOff = true; }
            var tseen = new HashSet<ushort>();
            foreach (var t in _buffer.LatestTrains())
            {
                tseen.Add(t.Id);
                UpdateTrain(t, now);
            }
            foreach (var id in _trains.Keys.Where(id => !tseen.Contains(id)).ToList()) RemoveTrain(id);

            // A copy horse we sit on is ours from now on (the owner's game keeps its own): steered after the
            // owner's, it would throw us straight off (guests couldn't ride a horse they took, 29 Oct).
            int myPed = PLAYER.PLAYER_PED_ID();
            int myMount = PED.IS_PED_ON_MOUNT(myPed) ? PED.GET_MOUNT(myPed) : 0;
            if (myMount != 0) foreach (var kv in _mirrors.Where(kv => kv.Value.Ped == myMount).ToList()) Take(kv.Key, kv.Value);

            var seen = new HashSet<ushort>();
            // Horses first, so their riders can be put on them in the same tick.
            foreach (var e in states.OrderBy(s => s.MountId == 0 ? 0 : 1))
            {
                seen.Add(e.Id);
                Update(e, puppets, now);
            }
            foreach (var id in _mirrors.Keys.Where(id => !seen.Contains(id)).ToList()) Remove(id);

            int me = PLAYER.PLAYER_PED_ID();
            foreach (var kv in _mirrors)
            {
                if (!Peds.Exists(kv.Value.Ped)) continue;
                // We killed our copy outright (a punch, a finishing move): the owner's NPC dies too (bug #9: it got back up).
                if (kv.Value.KillPending)
                {
                    kv.Value.KillPending = false;
                    int b = 0;
                    unsafe { PED.GET_PED_LAST_DAMAGE_BONE(kv.Value.Ped, &b); }
                    uint how = PED.GET_PED_CAUSE_OF_DEATH(kv.Value.Ped);
                    link.SendHit(owner, kv.Key, Hit.Kill, b, how);
                    CoopScript.Log($"copy {kv.Key} ({kv.Value.Model:X8}) killed in our game by us (cause {how:X8}): told {owner}'s game to kill it");
                    continue;
                }
                if (ENTITY.IS_ENTITY_DEAD(kv.Value.Ped) || kv.Value.Guarded == true) continue;
                if (!kv.Value.KnockoutLogged && PED.IS_PED_INCAPACITATED(kv.Value.Ped))
                {
                    kv.Value.KnockoutLogged = true;   // diagnostics: knockouts aren't relayed (yet)
                    CoopScript.Log($"copy {kv.Key} ({kv.Value.Model:X8}) knocked out in our game; the owner's NPC isn't told about knockouts");
                }
                int damage = Peds.TakeTrapped(kv.Value.Ped, me, 0, out int bone);
                if (damage > 0)
                {
                    link.SendHit(owner, kv.Key, (short)Math.Min(damage, Hit.MaxDamage), bone, Peds.CurrentWeapon(me));   // more is dropped as a bad packet
                    if (now - _hitLogMs > 1000) { _hitLogMs = now; CoopScript.Log($"hit sent to {owner}'s game: copy {kv.Key}, damage {damage}"); }
                }
            }
        }

        // The owner's game says this copy's NPC lives, and it decides: up again where it fell. Removing it made it
        // vanish ("they fall off the map") and pop up again. Its trap and tasks are set again on the next update.
        static bool Revive(Mirror m)
        {
            PED.RESURRECT_PED(m.Ped);
            PED.REVIVE_INJURED_PED(m.Ped);
            if (ENTITY.IS_ENTITY_DEAD(m.Ped)) return false;
            TASK.CLEAR_PED_TASKS_IMMEDIATELY(m.Ped, false, true);
            m.KillSentMs = 0; m.KillPending = false; m.LastTaskMs = 0; m.Guarded = null; m.KnockoutLogged = false;
            return true;
        }

        // Whether we caused this copy's death: the game names us as its source, or we damaged it.
        static bool KilledByUs(int ped)
        {
            int me = PLAYER.PLAYER_PED_ID();
            return PED.GET_PED_SOURCE_OF_DEATH(ped) == me || ENTITY.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY(ped, me, true, true);
        }

        void Update(EntityState e, IDictionary<byte, Puppet> puppets, long now)
        {
            // The owner's own horse while they're on foot: their stand-in's horse already
            // waits where they got off (0.8.0), so a copy would be a second horse.
            if ((e.Flags & EntityFlags.PlayerHorse) != 0 && puppets.TryGetValue(_ownerId, out var op) && op.HasWaitingHorse)
            {
                if (_mirrors.ContainsKey(e.Id)) Remove(e.Id);
                return;
            }
            if (_taken.Contains(e.Id)) return;
            if (!_mirrors.TryGetValue(e.Id, out var m) || m.Model != e.Model || !Peds.Exists(m.Ped))
            {
                if (m != null) Remove(e.Id);
                if (!ModelReady(e.Model, now)) return;
                int ped = Peds.Spawn(e.Model, CoopScript.ToGame(e.Position), e.Heading);
                if (ped == 0)
                {
                    if (_retry.Failed(e.Model, now, 5000))
                        CoopScript.Log($"cannot spawn model {e.Model:X8} right now (the game holds {World.GetAllPeds().Length} people, {_hidden.Count} of ours hidden): trying again in 5 s");
                    return;
                }
                m = new Mirror { Ped = ped, Model = e.Model };
                _mirrors[e.Id] = m;
            }
            if (Cutscenes.Scripted.Contains(m.Ped)) return;   // playing the host's cutscene here

            // The owner's horse and a story mission's allies: shown, never hurt here, since the owner's game ignores
            // our hits on them (a copy we could kill only dropped and came back). Anyone else is damage-trapped.
            bool guarded = !e.Dead && (e.Flags & (EntityFlags.PlayerHorse | EntityFlags.Protected)) != 0;   // dead in the owner's game: dies here too
            if (m.Guarded != guarded)
            {
                Peds.ArmTrap(m.Ped);   // full trap health either way, so the hit loop never reads a guarded copy's health as damage
                if (guarded) { ENTITY.SET_ENTITY_INVINCIBLE(m.Ped, true); ENTITY.SET_ENTITY_CAN_BE_DAMAGED(m.Ped, false); }
                m.Guarded = guarded;
            }

            // The same named outfit as in the owner's game (not a random one).
            if (e.Outfit != 0 && e.Outfit != m.Outfit)
            {
                Looks.EquipOutfit(m.Ped, e.Outfit);
                m.Outfit = e.Outfit;
                m.LookSig = 0;   // the full look goes on top of the outfit again
            }
            if (_looks.TryGetValue(e.Id, out var look) && look.Model == e.Model && m.LookSig != look.Signature())
            {
                Looks.Apply(m.Ped, look);
                m.LookSig = look.Signature();
            }

            // A spawned ped starts in its model's default group; for Arthur's
            // or gang members' models that is the player's own group, which
            // the game won't let us aim at. Copies always use our own groups.
            int group = (e.Flags & EntityFlags.Hostile) != 0 ? 1 : 0;
            if (group != m.Group)
            {
                EnsureGroups();
                PED.SET_PED_RELATIONSHIP_GROUP_HASH(m.Ped, group == 1 ? _hostileGroup : _neutralGroup);
                m.Group = group;
            }

            bool hostile = (e.Flags & EntityFlags.Hostile) != 0;
            Blips.Sync(ref m.Blip, m.Ped, (e.Flags & EntityFlags.Blipped) != 0 && !e.Dead,
                       hostile ? "BLIP_STYLE_ENEMY" : "BLIP_STYLE_COMPANION");

            bool deadHere = ENTITY.IS_ENTITY_DEAD(m.Ped);
            if (e.Dead)
            {
                if (!deadHere) ENTITY.SET_ENTITY_HEALTH(m.Ped, 0, 0);
                return;
            }
            if (deadHere)
            {
                if (m.KillSentMs == 0 && KilledByUs(m.Ped)) { m.KillSentMs = now; m.KillPending = true; return; }   // the hit loop tells the owner; the body stays meanwhile
                if (m.KillSentMs > 0 && now - m.KillSentMs < KillWaitMs) return;
                if (now - _deadLogMs > 1000)
                {
                    _deadLogMs = now;
                    string what = m.KillSentMs > 0 ? $"copy {e.Id} we killed is still alive in the owner's game after {KillWaitMs / 1000} s"
                                                   : $"copy {e.Id} died in our game but not in the owner's, and not by us (source {PED.GET_PED_SOURCE_OF_DEATH(m.Ped)})";
                    bool back = Revive(m);
                    CoopScript.Log(what + (back ? ": back on its feet where it fell" : ": the game won't revive it: removed, respawns"));
                    if (back) return;
                }
                else if (Revive(m)) return;
                Remove(e.Id); return;   // died here but not in the owner's game, and won't get up: respawn next tick
            }

            // We hogtied this copy: the owner's game ties up the real one; the
            // copy stays tied up here meanwhile.
            if (PED.IS_PED_HOGTIED(m.Ped))
            {
                if (e.Action != EntityAction.Hogtied && !m.HogtieSent)
                {
                    _link?.SendAction(_ownerId, ActionKind.Hogtie, e.Id);
                    m.HogtieSent = true;
                    CoopScript.Log($"hogtied copy {e.Id}: told the owner");
                }
                return;
            }
            m.HogtieSent = false;

            if (e.MountId != 0 && _mirrors.TryGetValue(e.MountId, out var horse) && Peds.Exists(horse.Ped))
            {
                if (!PED.IS_PED_ON_MOUNT(m.Ped)) PED.SET_PED_ONTO_MOUNT(m.Ped, horse.Ped, -1, true);
                return;   // the horse's own steering moves them both
            }

            // In a vehicle: sit in the copy's seat; the vehicle's steering moves them.
            bool seated = false;
            int inside = VehicleHandle(e.VehicleId);   // a copy wagon, or a copy train's engine
            if (inside != 0)
                seated = Peds.SitIn(m.Ped, inside, e.Seat);
            else if (PED.IS_PED_IN_ANY_VEHICLE(m.Ped, false))
                TASK.CLEAR_PED_TASKS_IMMEDIATELY(m.Ped, false, true);   // got out in the owner's game

            if (e.Weapon != 0 && e.Weapon != m.Weapon && WEAPON.IS_WEAPON_VALID(e.Weapon))
            {
                WEAPON.GIVE_WEAPON_TO_PED(m.Ped, e.Weapon, 100, true, false, 0, false, 0.5f, 1f, 0, true, 0f, false);
                WEAPON.SET_PED_INFINITE_AMMO(m.Ped, true, e.Weapon);
                WEAPON.SET_CURRENT_PED_WEAPON(m.Ped, e.Weapon, true, 0, false, false);
                m.Weapon = e.Weapon;
            }

            int newShots = Shots.NewShots(m.LastShots, e.Shots);
            m.LastShots = e.Shots;
            if (newShots > 0 && m.Weapon != 0)
            {
                Fire(m, e, newShots, puppets, now);
                return;
            }
            if (now < m.BusyUntilMs || seated) return;   // let the shots finish; seated peds ride along
            if (ActOut(m, e, now)) return;

            // Between shots: aim at whoever the owner says they fight.
            if ((e.Flags & EntityFlags.Shooting) != 0 && e.Aim != 0 && e.Velocity.Length < 1f)
            {
                if (now - m.LastTaskMs < Peds.RetaskEveryMs) return;
                m.LastTaskMs = now;
                m.Standing = false;
                TASK.TASK_AIM_GUN_AT_COORD(m.Ped, TargetOf(e, puppets), 600, false, false);
                return;
            }

            // A riderless horse right next to us: we may be getting on it; held still till we're on (then Take).
            if ((e.Flags & EntityFlags.Animal) != 0 && PED._IS_THIS_MODEL_A_HORSE(m.Model) && PED._IS_MOUNT_SEAT_FREE(m.Ped, -1)
                && Peds.Position(m.Ped).DistanceTo(Game.Player.Character.Position) < 3f) return;
            m.LastTaskMs = Peds.Steer(m.Ped, e.Position, e.Velocity, e.Heading,
                (e.Flags & EntityFlags.Animal) != 0, m.LastTaskMs, ref m.Standing, now);
        }

        // Busy towns stream dozens of models in at once, often slower than a
        // few frames: keep asking on later ticks and only give up after 15 s.
        bool ModelReady(uint model, long now)
        {
            if (_missingModels.Contains(model) || !_retry.CanTry(model, now)) return false;
            if (STREAMING.HAS_MODEL_LOADED(model)) { _loadingSince.Remove(model); return true; }
            if (!new Model(model).IsInCdImage) { _missingModels.Add(model); CoopScript.Log($"model {model:X8} isn't in the game files"); return false; }
            STREAMING.REQUEST_MODEL(model, false);
            if (!_loadingSince.TryGetValue(model, out long since)) _loadingSince[model] = now;
            else if (now - since > 15000)
            {
                _loadingSince.Remove(model);
                if (_retry.Failed(model, now, 60000)) CoopScript.Log($"model {model:X8} didn't load in 15 s: copies with it are invisible for now, tried again in a minute");
            }
            return false;
        }

        void UpdateVehicle(VehicleState v, bool copyDrives, long now)
        {
            if (!_vehicles.TryGetValue(v.Id, out var mv) || mv.Model != v.Model || !Peds.Exists(mv.Veh))
            {
                if (mv != null) RemoveVehicle(v.Id);
                if (!STREAMING.IS_MODEL_A_VEHICLE(v.Model)) { _missingModels.Add(v.Model); return; }
                if (!ModelReady(v.Model, now)) return;
                int veh = VEHICLE.CREATE_VEHICLE(v.Model, CoopScript.ToGame(v.Position), v.Rotation.Z, false, false, false, false);
                STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED(v.Model);
                if (veh == 0)
                {
                    if (_retry.Failed(v.Model, now, 5000)) CoopScript.Log($"cannot spawn vehicle model {v.Model:X8} right now: trying again in 5 s");
                    return;
                }
                ENTITY.SET_ENTITY_AS_MISSION_ENTITY(veh, true, true);
                ENTITY.SET_ENTITY_INVINCIBLE(veh, true);   // the owner's game decides what breaks
                ENTITY.SET_ENTITY_LOAD_COLLISION_FLAG(veh, true);
                if ((v.Flags & VehicleFlags.Draft) != 0)
                {
                    VEHICLE._SET_DRAFT_VEHICLE_ANIMALS_CAN_DETACH(veh, false);
                    VEHICLE._SET_DRAFT_VEHICLE_YOKE_CAN_BREAK(veh, false);
                }
                mv = new MirrorVehicle { Veh = veh, Model = v.Model };
                _vehicles[v.Id] = mv;
            }
            if (Cutscenes.Scripted.Contains(mv.Veh)) return;   // in the host's cutscene here
            Blips.Sync(ref mv.Blip, mv.Veh, (v.Flags & VehicleFlags.Blipped) != 0, "BLIP_STYLE_OBJECTIVE");

            // We drive the copy ourselves: leave it to us (the owner's wagon won't follow yet).
            if (VEHICLE.GET_PED_IN_VEHICLE_SEAT(mv.Veh, -1) == PLAYER.PLAYER_PED_ID()) return;

            var target = CoopScript.ToGame(v.Position);
            var current = Peds.Position(mv.Veh);
            // Wheels follow our own ground: heights differ a little between the
            // two games. Boats keep the owner's height (water is level).
            bool onGround = (v.Flags & VehicleFlags.Boat) == 0;
            float gap = onGround ? current.DistanceTo2D(target) : current.DistanceTo(target);
            // Too far off, flipped, or thrown into the air: put it back on its
            // wheels at the right spot, facing the right way.
            // (Not right after a snap: the ground there may still be streaming
            // in, and "56 m above ground" then just means "no ground yet".)
            bool upset = onGround && now - mv.LastSnapMs > 2000
                && (ENTITY.IS_ENTITY_UPSIDEDOWN(mv.Veh) || ENTITY.GET_ENTITY_HEIGHT_ABOVE_GROUND(mv.Veh) > 3f);
            // In view, a moderate gap glides shut instead (the pull catches up ~5 m/s).
            bool snap = gap > VehicleSnapDistance && (gap > VehicleSnapDistance * 4f || !ENTITY.IS_ENTITY_ON_SCREEN(mv.Veh));
            if (snap || upset)
            {
                if (onGround) target = OnGround(target);
                ENTITY.SET_ENTITY_COORDS_NO_OFFSET(mv.Veh, target, false, false, false);
                if (onGround) ENTITY.SET_ENTITY_ROTATION(mv.Veh, 0f, 0f, v.Rotation.Z, 2, true);
                else ENTITY.SET_ENTITY_ROTATION(mv.Veh, v.Rotation.X, v.Rotation.Y, v.Rotation.Z, 2, true);
                ENTITY.SET_ENTITY_VELOCITY(mv.Veh, CoopScript.ToGame(v.Velocity));
                mv.LastDriveMs = 0;
                mv.LastSnapMs = now;
                _snaps++;
                if (now - mv.LastSnapLogMs > 5000)
                {
                    mv.LastSnapLogMs = now;
                    CoopScript.Log($"snapped vehicle {v.Id} model {v.Model:X8}: gap {gap:0.0} m, upside down {ENTITY.IS_ENTITY_UPSIDEDOWN(mv.Veh)}, " +
                                   $"above ground {ENTITY.GET_ENTITY_HEIGHT_ABOVE_GROUND(mv.Veh):0.0} m, owner speed {v.Velocity.Length:0.0}");
                }
                return;
            }
            _gapSum += gap; _gapCount++; if (gap > _gapMax) _gapMax = gap;

            if (VehicleMode == "glide") { Glide(mv, v, target, current, onGround, copyDrives, now); return; }

            // Someone has to hold the reins so the horses pull (or the engine of
            // a boat runs): the copy of the owner's driver, else a hidden stand-in.
            int driver = DriverFor(mv, copyDrives, now);
            if (driver == 0) return;

            // Drive like the game does: towards where the real one is heading at
            // its speed, a little faster when behind, slower when ahead.
            if (now - mv.LastDriveMs >= Peds.RetaskEveryMs * 2)
            {
                mv.LastDriveMs = now;
                var vel = CoopScript.ToGame(v.Velocity);
                float speed = new Vector3(vel.X, vel.Y, 0f).Length();
                if (speed < 0.5f && gap < 2f)
                {
                    TASK.TASK_VEHICLE_DRIVE_TO_COORD(driver, mv.Veh, current, 0f, 0UL, v.Model, DriveMode, 3f, 0f);
                }
                else
                {
                    var toTarget = target - current;
                    float ahead = speed > 0.1f ? Vector3.Dot(toTarget, vel / speed) : 0f;   // + = we're behind
                    float command = Math.Max(1f, speed + Math.Max(-2f, Math.Min(3f, ahead * 0.5f)));
                    TASK.TASK_VEHICLE_DRIVE_TO_COORD(driver, mv.Veh, target + vel * 1.5f, command, 0UL, v.Model, DriveMode, 1f, 0f);
                }
            }

            // Drifted: steer the velocity back towards the line. Built from the
            // owner's velocity (never from our current one, which would pile up
            // frame after frame) and capped, so it can't run away.
            if (gap > 4f)
            {
                var cur = ENTITY.GET_ENTITY_VELOCITY(mv.Veh, 0);
                var owner = CoopScript.ToGame(v.Velocity);
                var pull = (target - current) * 0.5f;
                var want = new Vector3(owner.X + pull.X, owner.Y + pull.Y, 0f);
                float max = new Vector3(owner.X, owner.Y, 0f).Length() + 4f;
                if (want.Length() > max) want = want.Normalized * max;
                ENTITY.SET_ENTITY_VELOCITY(mv.Veh, new Vector3(want.X, want.Y, onGround ? cur.Z : owner.Z + pull.Z));
            }
        }

        void Glide(MirrorVehicle mv, VehicleState v, Vector3 target, Vector3 current, bool onGround, bool copyDrives, long now)
        {
            bool draft = (v.Flags & VehicleFlags.Draft) != 0;
            int driver = draft && now - mv.LastSpeedMs >= VehicleSteer.DriveEveryMs ? DriverFor(mv, copyDrives, now) : 0;
            VehicleSteer.Glide(mv.Veh, v.Model, target, current, CoopScript.ToGame(v.Velocity), v.Rotation.Z, onGround, draft, driver, ref mv.LastSpeedMs, now);
        }

        void LogStats(long now)
        {
            if (now - _lastStatsMs < 10000) return;
            _lastStatsMs = now;
            if (_gapCount > 0 || _snaps > 0)
                CoopScript.Log($"vehicles ({VehicleMode}): {_vehicles.Count} copies, gap avg {_gapSum / Math.Max(1, _gapCount):0.0} m, max {_gapMax:0.0} m, snaps {_snaps}");
            _gapSum = 0; _gapCount = 0; _gapMax = 0; _snaps = 0;
        }

        // Driving style for the copies' drivers (GTA-family "normal").
        public static int DriveMode = 786603;

        int DriverFor(MirrorVehicle mv, bool copyDrives, long now)
        {
            int seated = VEHICLE.GET_PED_IN_VEHICLE_SEAT(mv.Veh, -1);
            if (copyDrives)
            {
                // The copied driver sits down in the people pass; make room for it.
                if (mv.StandIn != 0) { Peds.Delete(mv.StandIn); mv.StandIn = 0; }
                return seated != mv.StandIn ? seated : 0;
            }
            if (Peds.Exists(mv.StandIn)) return mv.StandIn;
            if (seated != 0) { mv.DriverlessSinceMs = -1; return seated; }   // e.g. a player's puppet
            // Give a player's puppet a second to take the reins before a stand-in does.
            if (mv.DriverlessSinceMs < 0) mv.DriverlessSinceMs = now;
            if (now - mv.DriverlessSinceMs < 1000) return 0;
            int ped = Peds.Spawn(ENTITY.GET_ENTITY_MODEL(PLAYER.PLAYER_PED_ID()), Peds.Position(mv.Veh), 0f);
            if (ped == 0) return 0;
            ENTITY.SET_ENTITY_VISIBLE(ped, false);
            ENTITY.SET_ENTITY_INVINCIBLE(ped, true);
            PED.SET_PED_CAN_BE_TARGETTED(ped, false);
            PED.SET_PED_INTO_VEHICLE(ped, mv.Veh, -1);
            mv.StandIn = ped;
            return ped;
        }

        // The ground under a point (searching from a little above it), else the point.
        static unsafe Vector3 OnGround(Vector3 p)
        {
            float z = 0f;
            return MISC.GET_GROUND_Z_FOR_3D_COORD(p.X, p.Y, p.Z + 5f, &z, false) ? new Vector3(p.X, p.Y, z) : p;
        }

        void UpdateTrain(TrainState t, long now)
        {
            if (!_trains.TryGetValue(t.Id, out var mt) || mt.Config != t.Config || !Peds.Exists(mt.Train))
            {
                if (mt != null) RemoveTrain(t.Id);
                if (_missingModels.Contains(t.Config)) return;
                int train = TrainConfigs.Create(t.Config, CoopScript.ToGame(t.Position), t.Direction, false);
                if (train == 0)
                {
                    _missingModels.Add(t.Config);
                    CoopScript.Log($"cannot create train config {t.Config:X8}");
                    return;
                }
                ENTITY.SET_ENTITY_AS_MISSION_ENTITY(train, true, true);
                ENTITY.SET_ENTITY_INVINCIBLE(train, true);
                VEHICLE._SET_TRAIN_STOPS_FOR_STATIONS(train, false);   // it stops when the owner's stops
                mt = new MirrorTrain { Train = train, Config = t.Config };
                _trains[t.Id] = mt;
                CoopScript.Log($"train {t.Id}: created config {t.Config:X8} with {t.Cars} cars");
            }
            // We drive this copy ourselves: the owner's train follows ours (TrainDrive).
            if (Sync.Trains && VEHICLE.GET_PED_IN_VEHICLE_SEAT(mt.Train, -1) == PLAYER.PLAYER_PED_ID()) return;

            var target = CoopScript.ToGame(t.Position);
            var current = Peds.Position(mt.Train);
            if (current.DistanceTo(target) > TrainFixDistance && now - mt.LastFixMs > 2000)
            {
                VEHICLE.SET_MISSION_TRAIN_COORDS(mt.Train, target);   // back onto the right spot of the track
                mt.LastFixMs = now;
                CoopScript.Log($"train {t.Id}: moved {current.DistanceTo(target):0} m to catch up");
            }
            // Same speed as the owner's, a little more or less to close a gap
            // along the track.
            TrainDrive.Follow(mt.Train, target, t.Speed);
        }

        unsafe void RemoveTrain(ushort id)
        {
            if (_trains.TryGetValue(id, out var mt) && Peds.Exists(mt.Train))
            {
                int h = mt.Train;
                if (Sync.Trains) TrainDrive.EmptySeats(h);   // stand-ins and copies sit in it now
                VEHICLE.DELETE_MISSION_TRAIN(&h);
            }
            _trains.Remove(id);
        }

        void RemoveVehicle(ushort id)
        {
            if (_vehicles.TryGetValue(id, out var mv)) { Blips.Remove(ref mv.Blip); Peds.Delete(mv.StandIn); Peds.Delete(mv.Veh); }
            _vehicles.Remove(id);
        }

        // For the state we broadcast (which copy we sit in) and for seating
        // other players' puppets in the copies.
        public int HandleOf(ushort id) => _mirrors.TryGetValue(id, out var m) && Peds.Exists(m.Ped) && !ENTITY.IS_ENTITY_DEAD(m.Ped) ? m.Ped : 0;

        public IEnumerable<(uint model, Vector3 pos)> CopyModels() => _mirrors.Values.Where(m => Peds.Exists(m.Ped)).Select(m => (m.Model, Peds.Position(m.Ped)));
        public IEnumerable<int> CopyHandles() => _mirrors.Values.Select(m => m.Ped).Where(Peds.Exists);

        public int VehicleHandle(ushort id) => id != 0 && _vehicles.TryGetValue(id, out var mv) && Peds.Exists(mv.Veh) ? mv.Veh
            : Sync.Trains && id != 0 && _trains.TryGetValue(id, out var mt) && Peds.Exists(mt.Train) ? mt.Train : 0;   // a train: its engine

        public ushort IdOfVehicle(int handle)
        {
            foreach (var kv in _vehicles) if (kv.Value.Veh == handle) return kv.Key;
            if (Sync.Trains) foreach (var kv in _trains) if (kv.Value.Train == handle) return kv.Key;
            return 0;
        }

        // Does what the NPC is busy with in the owner's game (sitting, leaning,
        // hands up, cowering, lying limp). True while it is: no steering then.
        bool ActOut(Mirror m, EntityState e, long now)
        {
            var action = e.Action;
            // Some scenarios walk about (carrying, patrolling): just follow those.
            if (action == EntityAction.Scenario && e.Velocity.Length > 0.6f) action = EntityAction.None;
            uint scenario = action == EntityAction.Scenario ? e.Scenario : 0;
            bool changed = action != m.Action || scenario != m.Scenario;
            if (changed)
            {
                if (m.Action != EntityAction.None) TASK.CLEAR_PED_TASKS(m.Ped, false, false);   // stand up, hands down
                m.Action = action;
                m.Scenario = scenario;
                m.LastTaskMs = 0;
            }
            if (action == EntityAction.None) return false;

            var at = CoopScript.ToGame(e.Position);
            switch (action)
            {
                case EntityAction.Scenario:
                    if (!changed) break;
                    ENTITY._SET_ENTITY_COORDS_AND_HEADING(m.Ped, at, e.Heading, false, false, false);
                    TASK.TASK_START_SCENARIO_IN_PLACE_HASH(m.Ped, scenario, -1, true, 0, e.Heading, false);
                    break;
                case EntityAction.HandsUp:
                    if (changed) TASK.TASK_HANDS_UP(m.Ped, -1, 0, -1, false);
                    break;
                case EntityAction.Cower:
                    if (changed) TASK.TASK_COWER(m.Ped, -1, 0, null);
                    break;
                default:   // knocked over or hogtied: limp, where the owner's lies
                    if (now - m.LastTaskMs < 800) break;
                    m.LastTaskMs = now;
                    if (Peds.Position(m.Ped).DistanceTo(at) > 2f)
                        ENTITY._SET_ENTITY_COORDS_AND_HEADING(m.Ped, at, e.Heading, false, false, false);
                    PED.SET_PED_TO_RAGDOLL(m.Ped, 1500, 1500, 0, false, false, null);
                    break;
            }
            return true;
        }

        // The NPC fired in the owner's game: fire as many real shots here, for
        // the sound, flash and tracers. Aimed over our head so they whizz past;
        // the damage itself only ever comes from the owner's game.
        void Fire(Mirror m, EntityState e, int count, IDictionary<byte, Puppet> puppets, long now)
        {
            Vector3 target = TargetOf(e, puppets);
            if (e.Aim != 0)   // at a player (us or a partner's stand-in): over the head; the owner's game does the damage
            {
                var from = Peds.Position(m.Ped);
                var dir = target - from;
                var side = new Vector3(-dir.Y, dir.X, 0f).Normalized;
                target = target + new Vector3(0f, 0f, 1.4f + (float)_rng.NextDouble() * 0.6f)
                                + side * (float)(_rng.NextDouble() * 2.4 - 1.2);
            }
            int duration = 200 + count * 350;
            TASK.TASK_SHOOT_AT_COORD(m.Ped, target, duration, 0, 0UL);
            m.BusyUntilMs = now + duration;
            m.LastTaskMs = now;
            m.Standing = false;
        }

        Vector3 TargetOf(EntityState e, IDictionary<byte, Puppet> puppets)
        {
            if (e.Aim == _myId) return Game.Player.Character.Position;
            if (e.Aim != 0 && puppets.TryGetValue(e.Aim, out var p) && p.Exists) return p.Position;
            // Firing at something we don't copy (another NPC): straight ahead.
            double r = e.Heading * Math.PI / 180.0;
            return CoopScript.ToGame(e.Position) + new Vector3((float)-Math.Sin(r) * 20f, (float)Math.Cos(r) * 20f, 1f);
        }

        static unsafe void EnsureGroups()
        {
            if (_neutralGroup != 0) return;
            uint n = 0, h = 0;
            PED.ADD_RELATIONSHIP_GROUP("COOP_COPIES", &n);
            PED.ADD_RELATIONSHIP_GROUP("COOP_HOSTILE", &h);
            uint player = MISC.GET_HASH_KEY("PLAYER");
            // 3 = neutral, 5 = hate; set both ways.
            PED.SET_RELATIONSHIP_BETWEEN_GROUPS(3, n, player);
            PED.SET_RELATIONSHIP_BETWEEN_GROUPS(3, player, n);
            PED.SET_RELATIONSHIP_BETWEEN_GROUPS(5, h, player);
            PED.SET_RELATIONSHIP_BETWEEN_GROUPS(5, player, h);
            _neutralGroup = n;
            _hostileGroup = h;
            CoopScript.Log($"relationship groups: copies {n:X8}, hostile {h:X8}");
        }

        // A ped in one of our copy groups is a copy, whoever lost track of it (a delete
        // that didn't take while it was mounted or carried, an earlier owner change, an
        // earlier script instance): never a real NPC of this game.
        internal static bool IsCopyGroup(int ped)
        {
            if (_neutralGroup == 0) EnsureGroups();   // an owner that was never a follower (a reloaded script, say) sweeps the last instance's copies too
            uint g = PED.GET_PED_RELATIONSHIP_GROUP_HASH(ped);
            return g == _neutralGroup || g == _hostileGroup;
        }

        internal static void SuppressAmbient()
        {
            PED._SET_AMBIENT_PED_DENSITY_MULTIPLIER_THIS_FRAME(0f);
            PED.SET_SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME(0f);
            PED._SET_AMBIENT_ANIMAL_DENSITY_MULTIPLIER_THIS_FRAME(0f);
            VEHICLE.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME(0f);
            VEHICLE.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME(0f);
            VEHICLE.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME(0f);
        }

        // People our own game spawned before suppression kicked in. Card tables' players and our own peds are
        // kept. People our own scripts made (mission entities: the camp's gang, a side quest's stranger) are hidden
        // rather than deleted, since their scripts expect them, and come back when we stop showing the owner's world
        // (Clear): the owner's game sends its own copies of them, so keeping ours doubled everyone (bug #4: "when he
        // joins at the camp he sees double NPCs"). [Gameplay] OwnMissionsAsGuest=Yes keeps them as before.
        void ClearLocalPeds(IEnumerable<Puppet> puppets)
        {
            var me = Game.Player.Character;
            var keep = new HashSet<int>(_mirrors.Values.Select(m => m.Ped)) { me.Handle };
            foreach (var p in puppets) { keep.Add(p.Handle); keep.Add(p.HorseHandle); }
            if (me.IsOnMount && me.CurrentMount != null) keep.Add(me.CurrentMount.Handle);
            var horse = Game.Player.ActiveHorse;
            if (horse != null) keep.Add(horse.Handle);
            // Our horse by every name the game has for it (the saddle horse alone missed some players' horses: 29 Oct,
            // guests who couldn't call or ride theirs), and any horse we stand at (getting on it).
            keep.Add(PLAYER._GET_ACTIVE_HORSE_FOR_PLAYER(PLAYER.PLAYER_ID()));
            keep.Add(PED._GET_LAST_MOUNT(me.Handle));

            var mePos = me.Position;
            int orphans = 0, hid = 0;
            foreach (int h in EntityScan.Peds(new[] { mePos }, ClearRadius))
            {
                if (keep.Contains(h) || !Peds.Exists(h) || PED.IS_PED_A_PLAYER(h)) continue;
                if (IsCopyGroup(h)) orphans++;   // a copy we lost track of: a mission entity, but ours to delete
                else
                {
                    uint model = ENTITY.GET_ENTITY_MODEL(h);
                    if (PED._IS_THIS_MODEL_A_HORSE(model))
                    {
                        if (Peds.Position(h).DistanceTo(mePos) < 3f) continue;   // we may be getting on it
                        // Hidden, never removed: a removed camp horse was put straight back by the camp script, once a
                        // second (guests in the host's story stand in the same camp, 29 Oct); hidden ones come back
                        // when our game runs the area again. Logged once per breed.
                        if (Hide(h) && _horseLogged.Add((int)model)) CoopScript.Log($"guest clear: horses of breed {model:X8} hidden while showing the owner's world (ours kept: saddle {horse?.Handle ?? 0}, active {PLAYER._GET_ACTIVE_HORSE_FOR_PLAYER(PLAYER.PLAYER_ID())}, last {PED._GET_LAST_MOUNT(me.Handle)})");
                        continue;
                    }
                    if (TableGames.CardPlayer(h)) continue;
                    if (PED._GET_VEHICLE_DRAFT_HORSE_IS_ATTACHED_TO(h) != 0) continue;   // goes with its wagon below
                    if (Peds.Position(h).DistanceTo(mePos) > ClearRadius) continue;
                    if (ENTITY.IS_ENTITY_A_MISSION_ENTITY(h))
                    {
                        if (!Sync.OwnMissionsAsGuest && !Peds.Ours.Contains(h) && Hide(h)) hid++;   // never the mod's own peds (wagon drivers, carried bodies, partners' horses)
                        continue;
                    }
                }
                ENTITY.SET_ENTITY_AS_MISSION_ENTITY(h, true, true);   // ambient peds can only be deleted as mission entities
                Peds.Delete(h);
            }
            if (orphans > 0) CoopScript.Log($"swept {orphans} orphan copies");
            if (hid > 0) CoopScript.Log($"hid {hid} of our own script-made people (camp, side quests) while showing the owner's world ({_hidden.Count} hidden)");

            var keepVehicles = new HashSet<int>(_vehicles.Values.Select(v => v.Veh)) { Peds.VehicleOf(me.Handle) };
            foreach (int h in EntityScan.Vehicles(new[] { mePos }, ClearRadius))
            {
                if (keepVehicles.Contains(h) || !Peds.Exists(h) || ENTITY.IS_ENTITY_A_MISSION_ENTITY(h)) continue;
                if (Peds.Position(h).DistanceTo(mePos) > ClearRadius) continue;
                ENTITY.SET_ENTITY_AS_MISSION_ENTITY(h, true, true);
                Peds.Delete(h);
            }
        }

        void Remove(ushort id)
        {
            if (_mirrors.TryGetValue(id, out var m)) { Blips.Remove(ref m.Blip); Peds.Delete(m.Ped); }
            _mirrors.Remove(id);
        }

        // The owner's NPCs' full looks, by snapshot id (kept while the owner stays).
        readonly Dictionary<ushort, Appearance> _looks = new Dictionary<ushort, Appearance>();

        public void OnLook(ushort id, Appearance a)
        {
            if (_looks.Count > 400) _looks.Clear();
            _looks[id] = a;
        }

        // One of our own script-made people out of sight: invisible, no collision, still. True if newly hidden.
        bool Hide(int h)
        {
            if (!_hidden.Add(h)) return false;
            ENTITY.SET_ENTITY_VISIBLE(h, false);
            ENTITY.SET_ENTITY_COLLISION(h, false, false);
            ENTITY.FREEZE_ENTITY_POSITION(h, true);
            return true;
        }

        void ShowHidden()
        {
            if (_hidden.Count == 0) return;
            int back = 0;
            foreach (int h in _hidden)
            {
                if (!Peds.Exists(h)) continue;
                ENTITY.FREEZE_ENTITY_POSITION(h, false);
                ENTITY.SET_ENTITY_COLLISION(h, true, true);
                ENTITY.SET_ENTITY_VISIBLE(h, true);
                back++;
            }
            CoopScript.Log($"our own script-made people back: {back} of {_hidden.Count} (the rest are gone)");
            _hidden.Clear();
        }

        // A copy horse we got on: no longer a copy (no group, no trap, no blip, not steered), and that id isn't copied again.
        void Take(ushort id, Mirror m)
        {
            _mirrors.Remove(id);
            _taken.Add(id);
            Blips.Remove(ref m.Blip);
            PED.SET_PED_RELATIONSHIP_GROUP_HASH(m.Ped, PED.GET_PED_RELATIONSHIP_GROUP_DEFAULT_HASH(m.Ped));
            ENTITY.SET_ENTITY_INVINCIBLE(m.Ped, false);
            ENTITY.SET_ENTITY_CAN_BE_DAMAGED(m.Ped, true);
            // shortcut: it keeps the copy's big health pool (Peds.TrapHealth), so it's hard to kill; set a real horse's health if players notice
            Peds.Ours.Add(m.Ped);   // never hidden or cleared as one of our own script-made people
            CoopScript.Log($"copy horse {id} ({m.Model:X8}) taken: we rode off on it, it's ours now");
        }

        public void Clear()
        {
            ShowHidden();
            _taken.Clear();
            _looks.Clear();
            foreach (var id in _mirrors.Keys.ToList()) Remove(id);
            foreach (var id in _vehicles.Keys.ToList()) RemoveVehicle(id);
            foreach (var id in _trains.Keys.ToList()) RemoveTrain(id);
            _lastStates = new List<EntityState>();
            if (_trainsOff) { VEHICLE.SET_RANDOM_TRAINS(true); _trainsOff = false; }
            _buffer.Clear();
        }

        public int Count => _mirrors.Count;
        public bool Empty => _mirrors.Count == 0 && _vehicles.Count == 0 && _trains.Count == 0 && _hidden.Count == 0;

        // After a tick gap (pause menu, alt-tab, loading): a model asked for just before
        // it hasn't really been loading all that time; give it its 15 s again.
        public void AfterGap() => _loadingSince.Clear();

        List<EntityState> _lastStates = new List<EntityState>();

        // An enemy near `p` that fights (shoots or targets a player) right now.
        public bool CombatNear(Vector3 p, float radius) => _lastStates.Any(e =>
            !e.Dead && (e.Flags & EntityFlags.Hostile) != 0 && ((e.Flags & EntityFlags.Shooting) != 0 || e.Aim != 0)
            && CoopScript.ToGame(e.Position).DistanceTo(p) < radius);
    }
}
