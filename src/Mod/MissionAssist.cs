// Story missions from the guests' side. The host's game finds where its
// mission wants the player to go (map points that appeared after the mission
// started) and shares them; guests see them on their map. A guest who gets to
// one first can ask the host over: the host sees a prompt and only moves if
// they press Y.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class MissionAssist
    {
        public const float DefaultRadius = 8f;   // map points don't say how big their area is
        const float HostFarAway = 30f;
        const int AskEveryMs = 15000, OfferMs = 12000, SendEveryMs = 3000;

        readonly ObjectiveScan _scan = new ObjectiveScan();
        List<Objective> _sent = new List<Objective>();
        long _lastSendMs;

        // Guest: the host's objectives as markers here.
        List<Objective> _theirs = new List<Objective>();
        readonly List<int> _marks = new List<int>();
        long _lastAskMs = -AskEveryMs;

        // Host: a guest asked us over.
        byte _askedBy;
        long _offerUntil, _lastSay;
        bool _goPending;

        // Host side, every tick.
        public void HostTick(ServerLink link, Func<byte, PlayerState?> stateOf, long now)
        {
            if (_scan.Tick(MissionProbe.StoryMission))
            {
                _sent = _scan.Current;
                link.SendObjectives(_sent);
                _lastSendMs = now;
            }
            else if (_sent.Count > 0 && now - _lastSendMs > SendEveryMs)
            {
                link.SendObjectives(_sent);   // for anyone who joined since
                _lastSendMs = now;
            }

            if (_askedBy == 0) return;
            if (now > _offerUntil) { _askedBy = 0; _goPending = false; return; }
            if (_goPending)
            {
                _goPending = false;
                var st = stateOf(_askedBy);
                if (st.HasValue) GoTo(CoopScript.ToGame(st.Value.Position), link.NameOf(_askedBy));
                _askedBy = 0;
                return;
            }
            if (now - _lastSay > 2500)
            {
                _lastSay = now;
                Say($"Co-op: {link.NameOf(_askedBy)} is at the mission objective - press Y to join them");
            }
        }

        // Guest side, every tick.
        public void GuestTick(ServerLink link, bool partyMember, PlayerState? host, long now)
        {
            if (!partyMember || !host.HasValue || _theirs.Count == 0) return;
            var h = host.Value;
            if ((h.Flags & (StateFlags.InCutscene | StateFlags.ScreenFaded)) != 0) return;
            var me = Game.Player.Character.Position;
            if (me.DistanceTo(CoopScript.ToGame(h.Position)) < HostFarAway) return;
            bool atOne = _theirs.Any(o => me.DistanceTo2D(CoopScript.ToGame(o.Position)) <= o.Radius + 4f);
            if (!atOne || now - _lastAskMs < AskEveryMs) return;
            _lastAskMs = now;
            link.SendParty(new PartyMsg { Kind = PartyKind.AtObjective });
            Say($"Co-op: you're at the objective - {link.NameOf(link.HostId)} can press Y to join you");
        }

        public void OnObjectives(byte from, List<Objective> list, ServerLink link)
        {
            if (from != link.HostId) return;
            _theirs = list ?? new List<Objective>();
            foreach (var b in _marks) { int x = b; Blips.Remove(ref x); }
            _marks.Clear();
            foreach (var o in _theirs)
            {
                int b = MAP.BLIP_ADD_FOR_COORDS(MISC.GET_HASH_KEY("BLIP_STYLE_OBJECTIVE"), CoopScript.ToGame(o.Position));
                if (b != 0) _marks.Add(b);
            }
            CoopScript.Log($"host's mission objectives: {_theirs.Count}");
        }

        public void OnAtObjective(byte from, ServerLink link, long now)
        {
            if (!link.IAmHost || MissionProbe.InMissionScene) return;
            _askedBy = from;
            _offerUntil = now + OfferMs;
            _lastSay = 0;
            CoopScript.Log($"{link.NameOf(from)} asked us to the objective");
        }

        // True when the key was ours.
        public bool OnKey(System.Windows.Forms.Keys key)
        {
            if (key != System.Windows.Forms.Keys.Y || _askedBy == 0) return false;
            _goPending = true;   // moved from the tick, not inside the key handler
            return true;
        }

        public void Clear()
        {
            foreach (var b in _marks) { int x = b; Blips.Remove(ref x); }
            _marks.Clear();
            _theirs.Clear();
            _askedBy = 0;
        }

        // Next to the player who asked, horse and all, behind a fade.
        static void GoTo(Vector3 near, string name)
        {
            var me = Game.Player.Character;
            int mover = me.IsOnMount && me.CurrentMount != null ? me.CurrentMount.Handle : me.Handle;
            var at = near + new Vector3(2f, 2f, 0f);
            CAM.DO_SCREEN_FADE_OUT(300);
            for (int i = 0; i < 20 && !CAM.IS_SCREEN_FADED_OUT(); i++) Script.Yield();
            STREAMING.REQUEST_COLLISION_AT_COORD(at);
            ENTITY.SET_ENTITY_COORDS(mover, at, false, false, false, false);
            for (int i = 0; i < 60 && !ENTITY.HAS_COLLISION_LOADED_AROUND_ENTITY(mover); i++) Script.Yield();
            CAM.DO_SCREEN_FADE_IN(600);
            Say($"Co-op: joined {name} at the objective");
            CoopScript.Log($"moved to {name} at the objective {at}");
        }

        static void Say(string text) => RDR2.UI.Screen.DisplaySubtitle(text);
    }

    // RDR2 can't list map markers, so: check every possible marker handle
    // (a few hundred a frame) and keep the plain map points (not ones on a
    // person or vehicle) that appeared after the mission started.
    sealed class ObjectiveScan
    {
        const int PerTick = 400, Limit = 1 << 20;
        // Off unless [Testing] ObjectiveScan=Yes: probing handles that were never
        // given out crashed ScriptHook (23 Oct, a guest rejoining as host).
        public static bool Enabled;
        int _cursor = 1;
        HashSet<int> _pass = new HashSet<int>(), _baseline = new HashSet<int>();
        bool _off, _haveBaseline;
        public List<Objective> Current { get; private set; } = new List<Objective>();

        // True when Current changed (checked after each full pass).
        public bool Tick(bool inMission)
        {
            if (!Enabled || _off || Blips.MaxHandle == 0) return false;   // no marker made yet: the handle range is unknown
            if (Blips.MaxHandle > Limit)
            {
                _off = true;
                CoopScript.Log($"objective scan off: marker handles too large to check ({Blips.MaxHandle})");
                return false;
            }
            int end = Math.Min(Limit, Blips.MaxHandle * 4 + 4096);
            for (int i = 0; i < PerTick && _cursor <= end; i++, _cursor++)
                if (MAP.DOES_BLIP_EXIST(_cursor)) _pass.Add(_cursor);
            if (_cursor <= end) return false;

            var seen = _pass;
            _pass = new HashSet<int>();
            _cursor = 1;
            if (!inMission || !_haveBaseline)
            {
                _baseline = seen;
                _haveBaseline = true;
                if (Current.Count == 0) return false;
                Current = new List<Objective>();
                return true;
            }
            var me = Game.Player.Character.Position;
            var now = seen.Where(b => !_baseline.Contains(b) && MAP.DOES_BLIP_EXIST(b) && !MAP._IS_BLIP_ATTACHED_TO_ANY_ENTITY(b))
                          .Select(b => MAP.GET_BLIP_COORDS(b))
                          .Where(p => p.X != 0f || p.Y != 0f)
                          .OrderBy(p => p.DistanceTo(me))
                          .Take(Objective.Max)
                          .Select(p => new Objective { Position = CoopScript.ToNet(p), Radius = MissionAssist.DefaultRadius })
                          .ToList();
            bool changed = now.Count != Current.Count
                || now.Zip(Current, (a, b) => a.Position.X != b.Position.X || a.Position.Y != b.Position.Y).Any(d => d);
            if (changed) CoopScript.Log($"objective scan: {seen.Count} markers, {now.Count} new map points since the mission started");
            Current = now;
            return changed;
        }
    }
}
