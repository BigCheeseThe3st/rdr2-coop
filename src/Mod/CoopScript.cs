// Story-mode co-op for up to 4 players through a relay server (server/relay.py,
// or the one built into this mod: BuiltInServer).
// Every game runs the area around its own player; when players are close, one
// game runs the shared area (the host's if the host is there, so its story and
// enRage world are the one you share) and the others show copies of its NPCs.
// Each player sees the others as puppets; hits go to the game that owns the NPC
// or, for NPC hits on a player, to that player.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;
using LinkState = RDR2Coop.Net.LinkState;
using Appearance = RDR2Coop.Net.Appearance;
using Screen = RDR2.UI.Screen;

namespace RDR2Coop.Mod
{
    public class CoopScript : Script
    {
        sealed class Remote
        {
            public Interpolator Interp;
            public Puppet Puppet;
            public PlayerState Last;
            public bool HasLast;
            public long LastDriveMs;
            public Appearance Look;
            public bool HasLook;
            public uint AppliedLook;
        }

        const float PuppetRange = 400f;   // further than this: no puppet (the area isn't loaded)

        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly Dictionary<byte, Remote> _remotes = new Dictionary<byte, Remote>();
        ServerLink _link;
        BuiltInServer _builtIn;
        OwnedWorld _owned;
        MirrorWorld _mirror;
        byte _owner;                 // whose game runs our area (our own id when ours)
        string _status = "";
        uint _appearAs;
        Keys _teleportKey, _statusKey, _spawnWagonKey, _spawnTrainKey, _partyKey;
        readonly MissionParty _party = new MissionParty();
        readonly DownState _down = new DownState();
        Keys _reviveKey, _travelKey, _nemesisKey;
        bool _travelPending;
        readonly MissionProbe _missionProbe = new MissionProbe();
        readonly MissionAssist _assist = new MissionAssist();
        readonly EnrageLink _enrage = new EnrageLink();   // run from the tick: waiting inside a key handler isn't safe
        Vector3 _travelTo;
        string _travelName = "";
        long _lastRevivePrompt;
        long _lastLookCheck, _lastLookSent;
        uint _lookSig;
        int _sendEveryMs = 33, _smoothingMs = 70;
        uint _seq;
        long _lastSendMs;

        public CoopScript()
        {
            Interval = 0;
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;

            try { Start(); }
            catch (Exception e) { Log("start failed: " + e); _status = "RDR2Coop: start failed, see RDR2Coop.log"; }
        }

        void Start()
        {
            // BaseDirectory is the game folder, not scripts\, so look in both.
            string iniPath = Path.Combine(BaseDirectory, "scripts", "RDR2Coop.ini");
            if (!File.Exists(iniPath)) iniPath = Path.Combine(BaseDirectory, "RDR2Coop.ini");
            if (!File.Exists(iniPath)) Log("settings file not found next to the game or in scripts\\; using defaults");
            var ini = Ini.Load(iniPath);
            string name = ini.Get("Coop", "Name", Environment.UserName);
            string server = ini.Get("Coop", "Server", "127.0.0.1");
            int port = ini.Get("Coop", "Port", ServerLink.DefaultPort);
            string password = ini.Get("Coop", "Password", "");
            bool preferHost = ini.Get("Coop", "PreferHost", "No").StartsWith("y", StringComparison.OrdinalIgnoreCase);
            string appearAs = ini.Get("Coop", "AppearAs", "");
            if (ini.Get("Coop", "RunServer", "No").StartsWith("y", StringComparison.OrdinalIgnoreCase))
            {
                _builtIn = BuiltInServer.Start(port, password, !ini.Get("Coop", "UPnP", "Yes").StartsWith("n", StringComparison.OrdinalIgnoreCase), out string why);
                if (why != null) { Log(why); Say(why); }
                server = "127.0.0.1";   // our own server (or whatever already runs on that port here)
            }
            _appearAs = string.IsNullOrWhiteSpace(appearAs) ? 0 : MISC.GET_HASH_KEY(appearAs.Trim());
            _teleportKey = ParseKey(ini.Get("Keys", "TeleportToPartner", "F9"), Keys.F9);
            _statusKey = ParseKey(ini.Get("Keys", "ShowStatus", "F10"), Keys.F10);
            _spawnWagonKey = ParseKey(ini.Get("Keys", "SpawnWagon", ""), Keys.None);   // testing aids: off unless set in the ini
            _spawnTrainKey = ParseKey(ini.Get("Keys", "SpawnTrain", ""), Keys.None);
            _partyKey = ParseKey(ini.Get("Keys", "MissionParty", "F8"), Keys.F8);
            _reviveKey = ParseKey(ini.Get("Keys", "Revive", "F7"), Keys.F7);
            _travelKey = ParseKey(ini.Get("Keys", "TravelTo", ""), Keys.None);
            _nemesisKey = ParseKey(ini.Get("Keys", "Nemeses", "F11"), Keys.F11);
            var to = ini.Get("Testing", "TravelTo", "-283,790,118").Split(',');
            _travelTo = to.Length == 3 && float.TryParse(to[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tx)
                && float.TryParse(to[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ty)
                && float.TryParse(to[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tz)
                ? new Vector3(tx, ty, tz) : new Vector3(-283f, 790f, 118f);
            _travelName = ini.Get("Testing", "TravelName", "Valentine");

            int sendHz = Clamp(ini.Get("Net", "SendHz", 30), 5, 60);
            int worldHz = Clamp(ini.Get("Net", "WorldHz", 20), 2, 30);
            _smoothingMs = Clamp(ini.Get("Net", "SmoothingMs", 70), 0, 500);
            Peds.RetaskEveryMs = Clamp(ini.Get("Net", "RetaskMs", 150), 50, 1000);
            MirrorWorld.DriveMode = ini.Get("Net", "DriveMode", MirrorWorld.DriveMode);
            MirrorWorld.VehicleMode = ini.Get("Net", "VehicleMode", MirrorWorld.VehicleMode).ToLowerInvariant();
            _sendEveryMs = 1000 / sendHz;
            Log($"RDR2 Co-op {BuildInfo.Version} (protocol {Protocol.Version})");
            Log($"settings {iniPath} (exists {File.Exists(iniPath)}): name {name}, server {server}:{port}, " +
                $"prefer host {preferHost}, send {sendHz} Hz, world {worldHz} Hz, smoothing {_smoothingMs} ms, retask {Peds.RetaskEveryMs} ms");
            if (password.Length == 0) Log("no Password set in RDR2Coop.ini; the server will refuse us");

            _owned = new OwnedWorld(worldHz);
            _mirror = new MirrorWorld(_smoothingMs);
            _link = new ServerLink(name, preferHost, password, server, port);

            _link.Joined += () =>
            {
                Say($"Co-op: joined {server} as player {_link.MyId}");
                Log($"joined {server}:{port} as player {_link.MyId}, host {_link.HostId}");
            };
            _link.RosterChanged += OnRoster;
            _link.Disconnected += reason =>
            {
                _party.Leave();
                if (_down.IsDown) _down.GetUp(null, "disconnected");
                foreach (var r in _remotes.Values) r.Puppet?.Delete();
                _remotes.Clear();
                _assist.Clear();
                _enrage.Clear();
                AppDomain.CurrentDomain.SetData("rdr2coop.puppets", null);   // stale handles could be reused by new peds
                AppDomain.CurrentDomain.SetData("rdr2coop.copies", null);
                AppDomain.CurrentDomain.SetData("rdr2coop.puppetNames", null);
                _sharedPuppets = new int[0]; _sharedCopies = new int[0]; _sharedNames = new string[0];
                _mirror.Clear();
                _owner = 0;
                Say("Co-op: " + reason);
                Log("disconnected: " + reason);
            };
            _link.StateReceived += (from, s, at) =>
            {
                var r = RemoteFor(from);
                r.Interp.Push(s, at);
                r.Last = s;
                r.HasLast = true;
            };
            _link.WorldReceived += (from, w, at) =>
            {
                if (from == _owner && _owner != _link.MyId) _mirror.Push(w, at);
            };
            _link.HitReceived += OnHit;
            _link.ObjectivesReceived += (from, list) => _assist.OnObjectives(from, list, _link);
            _link.NoticeReceived += (from, n) => _enrage.OnNotice(from, n, _link, _clock.ElapsedMilliseconds);
            _link.ActionReceived += (from, a) =>
            {
                if (_owner == _link.MyId && _remotes.TryGetValue(from, out var r) && r.HasLast && r.Last.Following == _link.MyId)
                    _owned.ApplyAction(a, r.Puppet);
            };
            _link.EntityLookReceived += (from, id, a) =>
            {
                if (from == _owner && _owner != _link.MyId) _mirror.OnLook(id, a);
            };
            _link.AppearanceReceived += (from, a) =>
            {
                var r = RemoteFor(from);
                r.Look = a;
                r.HasLook = true;
            };
            _link.PartyReceived += (from, m) =>
            {
                if (m.Kind == PartyKind.AtObjective) { _assist.OnAtObjective(from, _link, _clock.ElapsedMilliseconds); return; }
                if (m.Kind == PartyKind.Revive)
                {
                    if (m.Members != null && m.Members.Contains(_link.MyId) && _down.IsDown)
                        _down.GetUp(null, $"{_link.NameOf(from)} revived you");
                    return;
                }
                _party.OnMessage(from, m, _link);
            };

            _status = $"Co-op: calling {server}:{port}";
            Log(_status);
            Say(_status);
        }

        Remote RemoteFor(byte id)
        {
            if (!_remotes.TryGetValue(id, out var r))
                _remotes[id] = r = new Remote { Interp = new Interpolator(_smoothingMs) };
            return r;
        }

        void OnRoster()
        {
            var ids = new HashSet<byte>(_link.Roster.Select(e => e.Id));
            foreach (var id in _remotes.Keys.Where(id => !ids.Contains(id)).ToList())
            {
                _remotes[id].Puppet?.Delete();
                _remotes.Remove(id);
            }
            string who = string.Join(", ", _link.Roster.Select(e => e.Name + (e.Role == PlayerRole.Host ? " (host)" : "")));
            Say("Co-op: " + who);
            Log("roster: " + who);
            _party.OnRoster(_link);
            _lastLookSent = 0;   // newcomers need our look
        }

        void OnTick(object sender, EventArgs e)
        {
            if (_link == null) return;
            if (_travelPending) { _travelPending = false; TravelTo(_travelTo, _travelName); }
            long now = _clock.ElapsedMilliseconds;
            _missionProbe.Tick(now);
            string note = _builtIn?.TakeNote();
            if (note != null) Say(note);
            _link.Poll(now);
            if (_link.State != LinkState.Connected) return;

            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;
            byte myId = _link.MyId;
            var myPos = ToNet(me.Position);

            // Whose game runs our area?
            var others = new Dictionary<byte, OtherPlayer>();
            foreach (var kv in _remotes)
                if (kv.Value.HasLast) others[kv.Key] = new OtherPlayer { Position = kv.Value.Last.Position, Following = kv.Value.Last.Following };
            byte owner = Authority.Pick(myId, myPos, _link.HostId, others, _owner == 0 ? myId : _owner);
            if (owner != _owner)
            {
                _mirror.Clear();
                if (_owner != 0) Say(owner == myId ? "Co-op: your game runs this area" : $"Co-op: sharing {_link.NameOf(owner)}'s world");
                Log($"area owner {_owner} -> {owner}");
                _owner = owner;
            }

            if (now - _lastSendMs >= _sendEveryMs)
            {
                _lastSendMs = now;
                var s = Capture(me, now);
                s.Following = owner == myId ? (byte)0 : owner;
                int veh = Peds.VehicleOf(me.Handle);
                if (veh != 0)
                {
                    // Vehicle ids live in the area owner's id space.
                    s.VehicleId = owner == myId ? _owned.IdForVehicle(veh) : _mirror.IdOfVehicle(veh);
                    s.Seat = Peds.SeatOf(veh, me.Handle);
                    if (s.Seat == -1)
                    {
                        // We drive: the game that owns the vehicle steers its real one after ours.
                        s.Flags |= StateFlags.Driving;
                        s.MountPosition = ToNet(Peds.Position(veh));
                        s.MountHeading = ENTITY.GET_ENTITY_ROTATION(veh, 2).Z;
                        s.Velocity = ToNet(ENTITY.GET_ENTITY_VELOCITY(veh, 0));
                    }
                }
                _link.SendState(s);
            }

            // Our look, when it changes (and every 15 s for anyone who joined late).
            if (_appearAs == 0 && now - _lastLookCheck > 2000)
            {
                _lastLookCheck = now;
                var look = Looks.Capture(me.Handle);
                uint sig = look.Signature();
                if (sig != _lookSig || now - _lastLookSent > 15000)
                {
                    if (sig != _lookSig) Log($"our look: {look.Tags.Count} pieces");
                    _lookSig = sig;
                    _lastLookSent = now;
                    _link.SendAppearance(look);
                }
            }

            // Puppets for everyone close enough to be in a loaded area.
            var puppets = new Dictionary<byte, Puppet>();
            foreach (var kv in _remotes)
            {
                var r = kv.Value;
                if (!r.HasLast || Vec3.Distance(r.Last.Position, myPos) > PuppetRange)
                {
                    r.Puppet?.Delete();
                    r.Puppet = null;
                    continue;
                }
                var st = r.Interp.Sample(now);
                if (r.Puppet == null || r.Puppet.PedModel != st.PedModel)
                {
                    r.Puppet?.Delete();
                    r.Puppet = Puppet.Spawn(st.PedModel, ENTITY.GET_ENTITY_MODEL(me.Handle), ToGame(st.Position), st.Heading, _link.NameOf(kv.Key));
                    r.AppliedLook = 0;
                    if (r.Puppet == null) continue;   // model still streaming; try next tick
                }
                // Their clothes, once we have them (only for the same model).
                if (r.HasLook && r.Look.Model == r.Puppet.PedModel && r.Look.Signature() != r.AppliedLook)
                {
                    Looks.Apply(r.Puppet.Handle, r.Look);
                    r.AppliedLook = r.Look.Signature();
                    Log($"{_link.NameOf(kv.Key)}'s look applied ({r.Look.Tags.Count} pieces)");
                }
                int vehicle = VehicleFor(r.Last, kv.Key, owner, myId);
                r.Puppet.Follow(st, now, vehicle);
                // Our game runs the area and they drive one of our real vehicles:
                // it follows the copy they steer in their game.
                if (owner == myId && vehicle != 0 && (st.Flags & StateFlags.Driving) != 0
                    && VEHICLE.GET_PED_IN_VEHICLE_SEAT(vehicle, -1) == r.Puppet.Handle)
                {
                    var target = ToGame(st.MountPosition);
                    VehicleSteer.Glide(vehicle, ENTITY.GET_ENTITY_MODEL(vehicle), target, Peds.Position(vehicle), ToGame(st.Velocity),
                        st.MountHeading, !VEHICLE.IS_THIS_MODEL_A_BOAT(ENTITY.GET_ENTITY_MODEL(vehicle)),
                        VEHICLE.IS_DRAFT_VEHICLE(vehicle), r.Puppet.Handle, ref r.LastDriveMs, now);
                }
                r.Puppet.SetCatchHits(owner == myId && r.Last.Following == myId);
                puppets[kv.Key] = r.Puppet;
            }
            ShareWithEnrage(puppets.Where(kv => kv.Value.Exists).Select(kv => (kv.Value.Handle, _link.NameOf(kv.Key))), _mirror.CopyHandles(), now);

            if (owner == myId)
            {
                var followers = new Dictionary<byte, Puppet>();
                var positions = new List<Vec3>();
                foreach (var kv in puppets)
                {
                    if (_remotes[kv.Key].Last.Following != myId) continue;
                    followers[kv.Key] = kv.Value;
                    positions.Add(_remotes[kv.Key].Last.Position);
                }
                _owned.Tick(_link, followers, positions, now);
                foreach (var kv in followers)
                {
                    int damage = kv.Value.TakeHits(me.Handle, out int bone);
                    if (damage > 0) _link.SendHit(kv.Key, 0, (short)Math.Min(damage, short.MaxValue), bone);
                }
            }
            else
            {
                _mirror.Tick(_link, owner, puppets, now);
            }

            PlayerState? hostState = _remotes.TryGetValue(_link.HostId, out var hr) && hr.HasLast ? hr.Last : (PlayerState?)null;
            _party.Tick(_link, hostState, now);
            _enrage.Tick(_link, _down.IsDown, now);
            if (owner != myId) _enrage.DrawNames(owner, _mirror.HandleOf);
            if (_link.IAmHost) _assist.HostTick(_link, id => _remotes.TryGetValue(id, out var x) && x.HasLast ? x.Last : (PlayerState?)null, now);
            else _assist.GuestTick(_link, _party.IsMember, hostState, now);

            // Down / revive.
            Vector3? ownerPos = owner != myId && _remotes.TryGetValue(owner, out var or) && or.HasLast ? ToGame(or.Last.Position) : (Vector3?)null;
            _down.Tick(owner != myId, ownerPos, owner != myId && _mirror.CombatNear(me.Position, 150f), _link.NameOf(owner), now);
            var downed = DownedNear(me.Position);
            if (downed != 0 && now - _lastRevivePrompt > 2000)
            {
                _lastRevivePrompt = now;
                Say($"Co-op: press {_reviveKey} to revive {_link.NameOf(downed)}");
            }
        }

        // enRage (with its co-op patch) reads these from the script domain: the
        // other players' characters count as "you" (with their names, for
        // per-player grudges), copies count as nobody. New arrays each time
        // (never edited in place); only when they change.
        long _lastShareMs;
        int[] _sharedPuppets = new int[0], _sharedCopies = new int[0];
        string[] _sharedNames = new string[0];

        void ShareWithEnrage(IEnumerable<(int handle, string name)> puppets, IEnumerable<int> copies, long now)
        {
            if (now - _lastShareMs < 250) return;
            _lastShareMs = now;
            var list = puppets.OrderBy(p => p.handle).ToList();
            var p2 = list.Select(p => p.handle).ToArray();
            var names = list.Select(p => p.name).ToArray();
            var c = copies.OrderBy(h => h).ToArray();
            AppDomain.CurrentDomain.SetData("rdr2coop.myName", _link.NameOf(_link.MyId));
            if (!names.SequenceEqual(_sharedNames)) { _sharedNames = names; AppDomain.CurrentDomain.SetData("rdr2coop.puppetNames", names); }
            if (!p2.SequenceEqual(_sharedPuppets)) { _sharedPuppets = p2; AppDomain.CurrentDomain.SetData("rdr2coop.puppets", p2); }
            if (!c.SequenceEqual(_sharedCopies)) { _sharedCopies = c; AppDomain.CurrentDomain.SetData("rdr2coop.copies", c); }
        }

        // A player who is down within reach of us, or 0.
        byte DownedNear(Vector3 at)
        {
            foreach (var kv in _remotes)
                if (kv.Value.HasLast && (kv.Value.Last.Flags & StateFlags.Downed) != 0 && kv.Value.Puppet != null
                    && kv.Value.Puppet.Exists && kv.Value.Puppet.Position.DistanceTo(at) < 2.5f)
                    return kv.Key;
            return 0;
        }

        // The local vehicle (real when our game runs the area, else a copy) a
        // remote player says they sit in, if we share the same area owner.
        int VehicleFor(PlayerState st, byte remoteId, byte owner, byte myId)
        {
            if (st.VehicleId == 0) return 0;
            byte theirOwner = st.Following == 0 ? remoteId : st.Following;
            if (theirOwner != owner) return 0;
            return owner == myId ? _owned.VehicleHandle(st.VehicleId) : _mirror.VehicleHandle(st.VehicleId);
        }

        // A hit on one of our NPCs (when our game runs the area), or an NPC in
        // someone else's game hitting our character (EntityId 0).
        void OnHit(byte from, Hit hit)
        {
            if (hit.Damage < 1 || hit.Damage > 1000) return;   // no gun does more; anything else is a bad packet
            if (hit.EntityId != 0)
            {
                // Only players in our area can shoot our NPCs.
                if (_owner == _link.MyId && _remotes.TryGetValue(from, out var r) && r.HasLast && r.Last.Following == _link.MyId)
                    _owned.ApplyHit(hit, r.Puppet);
                return;
            }
            // Only the game that runs our area has NPCs that can reach us.
            if (from != _owner || _down.IsDown) return;
            var me = Game.Player.Character;
            if (me == null || !me.Exists() || me.IsDead) return;
            if (_down.WouldFinish(hit.Damage)) { _down.GoDown($"hit by an NPC in {_link.NameOf(from)}'s game"); return; }
            PED.APPLY_DAMAGE_TO_PED(me.Handle, hit.Damage, 0, hit.Bone, 0);
        }

        PlayerState Capture(Ped me, long now)
        {
            var s = new PlayerState
            {
                Seq = ++_seq,
                TimeMs = (int)now,
                PedModel = _appearAs != 0 ? _appearAs : ENTITY.GET_ENTITY_MODEL(me.Handle),
                Position = ToNet(me.Position),
                Heading = me.Heading,
                Velocity = ToNet(me.Velocity),
            };
            if (me.IsDead) s.Flags |= StateFlags.Dead;
            if (Game.InCutscene || Game.IsCutsceneActive || MissionProbe.InMissionScene) s.Flags |= StateFlags.InCutscene;
            if (MissionProbe.StoryMission) s.Flags |= StateFlags.InMission;
            if (CAM.IS_SCREEN_FADED_OUT() || CAM.IS_SCREEN_FADING_OUT()) s.Flags |= StateFlags.ScreenFaded;
            if (_down.IsDown) s.Flags |= StateFlags.Downed;

            var horse = me.IsOnMount ? me.CurrentMount : null;
            if (horse != null && horse.Exists())
            {
                s.Flags |= StateFlags.Mounted;
                s.MountModel = ENTITY.GET_ENTITY_MODEL(horse.Handle);
                s.MountPosition = ToNet(horse.Position);
                s.MountHeading = horse.Heading;
                s.Velocity = ToNet(horse.Velocity);
            }
            return s;
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_link == null) return;
            if (e.KeyCode == _partyKey) { _party.Toggle(_link); return; }
            if (e.KeyCode == _travelKey) { _travelPending = true; return; }
            if (e.KeyCode == _nemesisKey && _enrage.RequestList(_link)) { Say($"Co-op: asking {_link.NameOf(_link.HostId)}'s enRage about your nemeses"); return; }
            if (e.KeyCode == _reviveKey)
            {
                byte target = DownedNear(Game.Player.Character.Position);
                if (target == 0) { Say("Co-op: nobody down within reach"); return; }
                _link.SendParty(new PartyMsg { Kind = PartyKind.Revive, Members = new List<byte> { target } });
                Say($"Co-op: you revived {_link.NameOf(target)}");
                return;
            }
            if (_assist.OnKey(e.KeyCode)) return;
            if (e.KeyCode == Keys.Y || e.KeyCode == Keys.N) _party.OnKey(e.KeyCode, _link);
            if (e.KeyCode == _statusKey)
            {
                string players = string.Join(", ", _link.Roster.Select(p => p.Name + (p.Role == PlayerRole.Host ? " (host)" : "")));
                string area = _owner == 0 ? "-" : _owner == _link.MyId ? "your game" : _link.NameOf(_owner) + "'s game";
                Say($"Co-op {BuildInfo.Version}: {_link.State} | players: {players} | this area: {area} | NPC copies: {_mirror.Count} | {_party.Status}" +
                    (_builtIn != null ? " | " + _builtIn.Status : ""));
            }
            else if (e.KeyCode == _spawnWagonKey)
            {
                // Testing aid: a coach (or cart) with its horses, 8 m ahead.
                var me = Game.Player.Character;
                var at = me.Position + me.ForwardVector * 8f;
                foreach (var name in new[] { "stagecoach001x", "cart01", "wagon02x" })
                {
                    uint model = MISC.GET_HASH_KEY(name);
                    if (!Peds.Load(model)) continue;
                    int veh = VEHICLE.CREATE_VEHICLE(model, at, me.Heading, false, false, false, false);
                    STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED(model);
                    if (veh == 0) continue;
                    Say("Co-op: spawned " + name);
                    Log("spawned " + name);
                    return;
                }
                Say("Co-op: no wagon model could be loaded");
            }
            else if (e.KeyCode == _spawnTrainKey)
            {
                // Testing aid: a passenger train at our feet (stand on the rails), rolling at 8 m/s.
                var me = Game.Player.Character;
                int train = TrainConfigs.Create(TrainConfigs.Default, me.Position, true, true);
                if (train == 0) { Say("Co-op: no train - see RDR2Coop.log"); return; }
                VEHICLE.SET_TRAIN_SPEED(train, 8f);
                VEHICLE.SET_TRAIN_CRUISE_SPEED(train, 8f);
                me.Position = me.Position + new Vector3(0f, 0f, 0f) + me.RightVector * 6f;   // step off the track
                Say("Co-op: spawned a train");
                Log("spawned a test train");
            }
            else if (e.KeyCode == _teleportKey)
            {
                // To the nearest other player we can see.
                var me = Game.Player.Character;
                var target = _remotes.Values.Where(r => r.Puppet != null && r.Puppet.Exists)
                    .OrderBy(r => r.Puppet.Position.DistanceTo(me.Position)).FirstOrDefault();
                if (target == null) { Say("Co-op: nobody close enough to teleport to"); return; }
                var to = target.Puppet.Position + new Vector3(1.5f, 1.5f, 0f);
                ENTITY.SET_ENTITY_COORDS(me.IsOnMount ? me.CurrentMount.Handle : me.Handle, to, false, false, false, false);
            }
        }

        void OnAborted(object sender, EventArgs e)
        {
            foreach (var r in _remotes.Values) r.Puppet?.Delete();
            _mirror?.Clear();
            _party?.Leave();   // never leave us faded, frozen or without control
            if (_down != null && _down.IsDown) _down.GetUp(null, "co-op stopped");
            _link?.Dispose();
            _builtIn?.Dispose();
            AppDomain.CurrentDomain.SetData("rdr2coop.puppets", null);
            AppDomain.CurrentDomain.SetData("rdr2coop.copies", null);
        }

        // Testing aid: there in one go, horse and all, on the ground.
        static unsafe void TravelTo(Vector3 at, string name)
        {
            var me = Game.Player.Character;
            int mover = me.IsOnMount && me.CurrentMount != null ? me.CurrentMount.Handle : me.Handle;
            CAM.DO_SCREEN_FADE_OUT(300);
            for (int i = 0; i < 20 && !CAM.IS_SCREEN_FADED_OUT(); i++) Yield();
            STREAMING.REQUEST_COLLISION_AT_COORD(at);
            ENTITY.SET_ENTITY_COORDS(mover, at, false, false, false, false);
            for (int i = 0; i < 120 && !ENTITY.HAS_COLLISION_LOADED_AROUND_ENTITY(mover); i++) Yield();
            float z = 0f;
            if (MISC.GET_GROUND_Z_FOR_3D_COORD(at.X, at.Y, at.Z + 20f, &z, false))
                ENTITY.SET_ENTITY_COORDS(mover, new Vector3(at.X, at.Y, z + 0.5f), false, false, false, false);
            CAM.DO_SCREEN_FADE_IN(600);
            Say("Co-op: travelled to " + name);
        }

        static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));

        static Keys ParseKey(string s, Keys fallback) =>
            !string.IsNullOrWhiteSpace(s) && Enum.TryParse(s, true, out Keys k) ? k : fallback;

        internal static Vec3 ToNet(Vector3 v) => new Vec3(v.X, v.Y, v.Z);
        internal static Vector3 ToGame(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        static void Say(string text) => Screen.DisplaySubtitle(text);

        internal static void Log(string line)
        {
            try
            {
                File.AppendAllText(Path.Combine("scripts", "RDR2Coop.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + line + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
