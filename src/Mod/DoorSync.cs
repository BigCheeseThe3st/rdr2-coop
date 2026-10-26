// Doors and broken things match in a shared area (0.8.0, [Gameplay] Doors). RDR2 keeps a door's
// lock in its door system, by the door's hash (the list is src/Net/DoorTable.cs). The game that
// runs the area tells the games showing it how the doors its scripts manage stand near them (a
// shop locked for the night, a mission door unlocked or held open): every 3 s, and within half a
// second of a change. Those games make their doors match, and put them back as they were when
// they stop showing that area. A partner there who opens a door or kicks one open tells the
// owner's game, which does the same (not a locked door in its story mission: that one is for its
// own player). Things that break (crates, fences, tables: the game's EVENT_ENTITY_BROKEN) break in
// the other games too: their object of that model at that spot. A door never locks with our player
// inside, nor when we put it back; while our own story mission runs its doors are its own.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class DoorSync
    {
        const int ScanMs = 500, ResendMs = 3000, HoldMs = 3000, RecentMs = 15000;
        const float ScanRange = 30f, ApplyRange = 40f, TouchRange = 2.5f, PartnerReach = 4f, BreakRange = 60f;
        const uint EntityBroken = 0xED53F1A6;         // EVENT_ENTITY_BROKEN: 9 slots, [0] the object, [6..8] where it stood
        const ulong SwingOpen = 0xB3B1546D23DF8DE1;   // _DOOR_SYSTEM_SWING_OPEN(door, dirX, dirY, dirZ, reverse), unnamed in ScriptHookRDR2DotNet 2.2.0

        // One of our doors, matched to the owner's.
        sealed class Door
        {
            public bool Ours;          // we added it to the door system: it leaves it again
            public bool Changed;       // we changed its lock or angle: put back as it was
            public int State;          // as it was before
            public float Ratio;
            public int Applied = -1;   // the owner's state we last gave it
            public bool Near, WasClosed;
            public long HoldUntil;     // our player just changed it: the owner's word waits until then
        }

        readonly Dictionary<uint, Door> _doors = new Dictionary<uint, Door>();
        readonly List<(uint model, Vec3 at, long until)> _recent = new List<(uint, Vec3, long)>();   // breaks sent or done
        byte _owner;
        long _lastScanMs, _lastSentMs;
        string _lastSent;
        bool _loggedApply, _loggedBreak;

        // Every tick. followers: where the players showing our area are (null while we show another game's).
        public void Tick(ServerLink link, byte owner, IList<Vec3> followers, long now)
        {
            if (!Sync.Doors) return;
            if (owner != _owner) { Clear(); _owner = owner; }
            bool own = owner == link.MyId;
            if (!own && MissionProbe.StoryMission) { ReleaseAll(); return; }   // our own mission's doors are its own
            var points = own ? followers : new List<Vec3> { CoopScript.ToNet(Game.Player.Character.Position) };
            if (points == null || points.Count == 0) return;
            Breaks(link, own, points, now);   // an event lasts one frame: read them every tick
            if (now - _lastScanMs < ScanMs) return;
            _lastScanMs = now;
            if (own) SendDoors(link, points, now); else Watch(link, now);
        }

        // We run the area: how the doors our scripts manage stand near the players showing it.
        void SendDoors(ServerLink link, IList<Vec3> followers, long now)
        {
            var lines = new List<string>();
            foreach (uint h in Doors.Near(followers, ScanRange, Doors.MaxPicture))
            {
                if (!OBJECT.IS_DOOR_REGISTERED_WITH_SYSTEM(h)) continue;   // no script manages it: physics alone, the same everywhere
                lines.Add(Doors.Line(new DoorLine { Hash = h, State = OBJECT.DOOR_SYSTEM_GET_DOOR_STATE(h),
                                                    Ratio = ClampRatio(OBJECT.DOOR_SYSTEM_GET_OPEN_RATIO(h)), Open = !OBJECT.IS_DOOR_CLOSED(h) }));
            }
            string text = Doors.Join("P", lines);
            if (text == _lastSent && now - _lastSentMs < ResendMs) return;
            _lastSent = text;
            _lastSentMs = now;
            link.SendNotice(Protocol.Broadcast, NoticeKind.Door, text);
        }

        // We show another game's area: doors we're far from go back, and the owner hears when our
        // player opens one or kicks one open (they'd snap back shut otherwise).
        void Watch(ServerLink link, long now)
        {
            var me = Game.Player.Character.Position;
            foreach (var kv in _doors.ToList())
            {
                uint h = kv.Key;
                var d = kv.Value;
                Doors.TryGet(h, out var at);
                float dist = CoopScript.ToGame(at).DistanceTo(me);
                if (dist > ApplyRange + 10f) { Release(h); continue; }
                if (dist > TouchRange || !OBJECT.IS_DOOR_REGISTERED_WITH_SYSTEM(h)) { d.Near = false; continue; }
                bool closed = OBJECT.IS_DOOR_CLOSED(h);
                int state = OBJECT.DOOR_SYSTEM_GET_DOOR_STATE(h);
                bool kicked = d.Near && d.Applied >= 0 && state != d.Applied, opened = d.Near && d.WasClosed && !closed;
                d.Near = true;
                d.WasClosed = closed;
                if (!kicked && !opened) continue;
                d.Applied = state;
                d.HoldUntil = now + HoldMs;
                link.SendNotice(_owner, NoticeKind.Door, Doors.Line(new DoorLine { Hash = h, State = state, Ratio = ClampRatio(OBJECT.DOOR_SYSTEM_GET_OPEN_RATIO(h)), Open = !closed }));
            }
        }

        // owner: whose game runs our area; sender: the latest state of the player it came from.
        public void OnNotice(byte from, string text, ServerLink link, byte owner, PlayerState? sender, long now)
        {
            var ped = Game.Player.Character;
            if (!Sync.Doors || ped == null || !ped.Exists()) return;
            var doors = new List<DoorLine>();
            var breaks = new List<BreakLine>();
            Doors.Parse(text, out bool picture, doors, breaks);
            var me = ped.Position;
            if (owner != link.MyId)
            {
                if (from != owner || owner != _owner || MissionProbe.StoryMission) return;   // only the game whose area we show, outside our own mission
                if (picture)
                {
                    // An unlisted door near us isn't the owner's to hold; further out it may only be past its scan.
                    var listed = new HashSet<uint>(doors.Select(o => o.Hash));
                    foreach (uint h in _doors.Keys.Where(h => !listed.Contains(h) && Doors.TryGet(h, out var at)
                                                              && CoopScript.ToGame(at).DistanceTo(me) < ScanRange - 5f).ToList()) Release(h);
                    bool inside = INTERIOR.GET_INTERIOR_FROM_ENTITY(ped.Handle) != 0;
                    foreach (var o in doors) Apply(o, me, inside, now);
                    if (!_loggedApply && _doors.Count > 0) { _loggedApply = true; CoopScript.Log($"doors: {_doors.Count} near us follow {link.NameOf(from)}'s game"); }
                }
                foreach (var b in breaks.Take(16)) if (Vec3.Distance(b.At, CoopScript.ToNet(me)) <= BreakRange) Break(b, now);
                return;
            }

            // We run the area: a partner showing it opened a door, kicked one open, or broke something.
            if (!sender.HasValue || sender.Value.Following != link.MyId) return;
            var them = CoopScript.ToGame(sender.Value.Position);
            foreach (var r in doors.Take(4))
            {
                Doors.TryGet(r.Hash, out var at);
                if (CoopScript.ToGame(at).DistanceTo(them) > PartnerReach || !OBJECT.IS_DOOR_REGISTERED_WITH_SYSTEM(r.Hash)) continue;
                int ours = OBJECT.DOOR_SYSTEM_GET_DOOR_STATE(r.Hash);
                if (ours == 2 && (r.State == 0 || r.Open))   // locked but breakable: they kicked it in
                {
                    if (MissionProbe.StoryMission) continue;   // a mission waits for our player to open its doors
                    OBJECT.DOOR_SYSTEM_SET_DOOR_STATE(r.Hash, 0);
                    Swing(r.Hash, at, them, r.Ratio);
                    CoopScript.Log($"doors: {link.NameOf(from)} kicked door {r.Hash:X8} open, so it opens here too");
                }
                else if (ours == 0 && r.Open && OBJECT.IS_DOOR_CLOSED(r.Hash)) Swing(r.Hash, at, them, r.Ratio);
            }
            var shared = new List<string>();
            foreach (var b in breaks.Take(4))
                if (Vec3.Distance(b.At, sender.Value.Position) <= BreakRange && Break(b, now)) shared.Add(Doors.Line(b));
            if (shared.Count > 0) link.SendNotice(Protocol.Broadcast, NoticeKind.Door, Doors.Join(null, shared));   // for everyone else here
        }

        // The owner's door, here: the same lock, and the angle its scripts hold it at.
        // inside: our player is in a building, where a lock could shut them in.
        void Apply(DoorLine o, Vector3 me, bool inside, long now)
        {
            uint h = o.Hash;
            if (!Doors.TryGet(h, out var at) || CoopScript.ToGame(at).DistanceTo(me) > ApplyRange) return;
            bool locked = o.State == 1 || o.State == 2;
            if (!_doors.TryGetValue(h, out var d))
            {
                bool known = OBJECT.IS_DOOR_REGISTERED_WITH_SYSTEM(h);
                if (!known && (o.State == 0 || (locked && inside))) return;   // nothing locks it here; never shut our player in
                if (!known) OBJECT._ADD_DOOR_TO_SYSTEM_NEW(h, true, false, false, 0, 0, false);   // as the story scripts add one
                _doors[h] = d = new Door { Ours = !known, State = known ? OBJECT.DOOR_SYSTEM_GET_DOOR_STATE(h) : -1,
                                           Ratio = known ? ClampRatio(OBJECT.DOOR_SYSTEM_GET_OPEN_RATIO(h)) : 0f };
            }
            if (now < d.HoldUntil) return;
            if (locked && inside) { d.Applied = -1; return; }   // never lock or shut it on our player: it locks once they're out
            int state = OBJECT.DOOR_SYSTEM_GET_DOOR_STATE(h);
            bool turn = locked && (Math.Abs(o.Ratio) > 0.05f
                ? Math.Abs(OBJECT.DOOR_SYSTEM_GET_OPEN_RATIO(h) - o.Ratio) > 0.05f   // held at an angle there
                : !o.Open && !OBJECT.IS_DOOR_CLOSED(h));                              // shut there, open here
            if (turn)
            {
                if (state == 1 || state == 2) { OBJECT.DOOR_SYSTEM_SET_DOOR_STATE(h, 0); state = 0; }   // a locked door may not turn
                OBJECT.DOOR_SYSTEM_SET_OPEN_RATIO(h, o.Ratio, true);
            }
            bool relock = state != o.State;
            if (relock) OBJECT.DOOR_SYSTEM_SET_DOOR_STATE(h, o.State);
            if (turn || relock) { d.Changed = true; d.Near = false; }   // our change, not our player's
            d.Applied = o.State;
        }

        void Release(uint h)
        {
            if (!_doors.TryGetValue(h, out var d)) return;
            _doors.Remove(h);
            if (d.Ours) { OBJECT.REMOVE_DOOR_FROM_SYSTEM(h); return; }
            if (!d.Changed || d.State < 0 || d.State > Doors.MaxState || !OBJECT.IS_DOOR_REGISTERED_WITH_SYSTEM(h)) return;
            if (d.State == 1 || d.State == 2)
            {
                int state = OBJECT.DOOR_SYSTEM_GET_DOOR_STATE(h);
                if (state != 1 && state != 2) return;   // never lock one on the way out: our game may have unlocked it since (a shop opening)
                OBJECT.DOOR_SYSTEM_SET_DOOR_STATE(h, 0);
                OBJECT.DOOR_SYSTEM_SET_OPEN_RATIO(h, d.Ratio, true);
            }
            OBJECT.DOOR_SYSTEM_SET_DOOR_STATE(h, d.State);
        }

        void ReleaseAll()
        {
            foreach (uint h in _doors.Keys.ToList()) Release(h);
        }

        static float ClampRatio(float r) => float.IsNaN(r) ? 0f : Math.Max(-1f, Math.Min(1f, r));

        // A door swung open as a push would (the native wants it unlocked first).
        static void Swing(uint door, Vec3 at, Vector3 from, float ratio)
        {
            var dir = CoopScript.ToGame(at) - from;
            dir.Z = 0f;
            dir = dir.Length() > 0.1f ? dir.Normalized : new Vector3(1f, 0f, 0f);
            Function.Call(SwingOpen, door, dir.X, dir.Y, dir.Z, ratio < 0f);
        }

        // Objects that broke in our game near the players: to the owner's game, or as the owner to everyone.
        unsafe void Breaks(ServerLink link, bool own, IList<Vec3> points, long now)
        {
            var events = AiEvents.Now(now);
            if (events.Count == 0) return;
            ulong* data = stackalloc ulong[16];   // the event fills 9; room to spare if a game version wrote more
            List<string> lines = null;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] != EntityBroken || !SCRIPTS.GET_EVENT_DATA(AiEvents.Queue, i, data, 9)) continue;
                int obj = (int)data[0];
                if (!Peds.Exists(obj) || !ENTITY.IS_ENTITY_AN_OBJECT(obj) || ENTITY.IS_ENTITY_A_MISSION_ENTITY(obj)) continue;
                var at = new Vec3(*(float*)(data + 6), *(float*)(data + 7), *(float*)(data + 8));
                if (float.IsNaN(at.X + at.Y + at.Z) || Math.Abs(at.X) + Math.Abs(at.Y) < 1f || at.Length > 20000f) at = CoopScript.ToNet(Peds.Position(obj));
                var b = new BreakLine { Model = ENTITY.GET_ENTITY_MODEL(obj), At = at };
                if (!points.Any(p => Vec3.Distance(p, at) <= BreakRange) || Recent(b, now)) continue;
                _recent.Add((b.Model, b.At, now + RecentMs));
                (lines = lines ?? new List<string>()).Add(Doors.Line(b));
            }
            if (lines != null) link.SendNotice(own ? Protocol.Broadcast : _owner, NoticeKind.Door, Doors.Join(null, lines));
        }

        // Breaks our object of that model at that spot, if we have one (true when it did).
        bool Break(BreakLine b, long now)
        {
            if (Recent(b, now) || STREAMING.IS_MODEL_A_PED(b.Model) || STREAMING.IS_MODEL_A_VEHICLE(b.Model)) return false;
            int obj = OBJECT.GET_CLOSEST_OBJECT_OF_TYPE(CoopScript.ToGame(b.At), 1.5f, b.Model, false, false, false);
            if (!Peds.Exists(obj) || ENTITY.IS_ENTITY_A_MISSION_ENTITY(obj)) return false;
            _recent.Add((b.Model, b.At, now + RecentMs));   // its own break event here isn't news,
            _recent.Add((b.Model, CoopScript.ToNet(Peds.Position(obj)), now + RecentMs));   // even if it stood a little off that spot
            OBJECT.BREAK_ALL_OBJECT_FRAGMENT_BONES(obj);
            if (!_loggedBreak) { _loggedBreak = true; CoopScript.Log($"broke a {b.Model:X8} at {b.At} to match the game it broke in"); }
            return true;
        }

        bool Recent(BreakLine b, long now)
        {
            if (_recent.Count > 128) _recent.Clear();   // a whole street blown up: forget, at worst one echo
            _recent.RemoveAll(r => r.until < now);
            return _recent.Any(r => r.model == b.Model && Vec3.Distance(r.at, b.At) < 1f);
        }

        // Every door we changed goes back as it was (another area, the session over, the mod stopping).
        public void Clear()
        {
            ReleaseAll();
            _recent.Clear();
            _lastSent = null;
            _owner = 0;
            _loggedApply = false;
        }
    }
}
