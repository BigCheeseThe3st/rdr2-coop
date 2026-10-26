// Story missions from the guests' side. Between missions the host's game shares
// where its story missions start (the mission markers on its map, read from
// the story scripts' mission table: src/Net/MissionMarkers.cs); guests see them
// on their map ([Gameplay] Markers). A guest in the mission party who gets to
// one first can ask the host over ([Gameplay] MissionSteps): the host sees a
// prompt and only moves if they press Y. Guests can't do a mission step (talk,
// open, pick up) for the host: the prompt only answers the host's own buttons
// where the host stands, and the scene after it moves the host's character.
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
        const int AskEveryMs = 15000, OfferMs = 12000, SendEveryMs = 3000, ReadEveryMs = 2000, RepeatChangeMs = 10000;

        // Host: our mission markers as last shared.
        List<Objective> _sent = new List<Objective>();
        long _lastReadMs = -ReadEveryMs, _lastSendMs, _changedMs = -RepeatChangeMs;

        // Guest: the host's markers drawn here.
        List<Objective> _theirs = new List<Objective>();
        byte _theirsFrom;
        readonly List<int> _marks = new List<int>();
        long _lastAskMs = -AskEveryMs;
        bool _wasAtOne;

        // Host: a guest asked us over.
        byte _askedBy;
        long _offerUntil, _lastSay;
        bool _goPending, _moving;

        // Host side, every tick.
        public void HostTick(ServerLink link, Func<byte, PlayerState?> stateOf, long now)
        {
            if (_theirsFrom != 0) ClearMarks();   // we host now: the old host's markers go
            if (now - _lastReadMs >= ReadEveryMs)
            {
                _lastReadMs = now;
                // Only between missions: during one the guests get none.
                var list = Sync.Markers && MissionProbe.GlobalsOk && !MissionProbe.StoryMission
                    ? MissionMarkers.Read(MissionProbe.GInt, MissionProbe.GFloat, CoopScript.ToNet(Game.Player.Character.Position), DefaultRadius)
                    : new List<Objective>();
                if (!MissionMarkers.Same(list, _sent))
                {
                    _sent = list;
                    _changedMs = now;
                    _lastSendMs = -SendEveryMs;
                    CoopScript.Log($"our mission markers for the guests: {_sent.Count}");
                }
            }
            // Again every few seconds for anyone who joined since (and a few times after a change, in case one got lost).
            // As a MissionStep notice, which 0.7.x ignores: it reads the Objectives packet as a running mission's
            // objectives, and would redraw them each time and keep asking us over from camp.
            if ((_sent.Count > 0 || now - _changedMs < RepeatChangeMs) && now - _lastSendMs >= SendEveryMs)
            {
                link.SendNotice(Protocol.Broadcast, NoticeKind.MissionStep, MissionMarkers.Write(_sent));
                _lastSendMs = now;
            }

            if (_askedBy == 0) return;
            // A mission of ours started meanwhile (the markers were where missions start), or an activity
            // of ours did (Y isn't ours then): the offer is over.
            if (now > _offerUntil || MissionProbe.StoryMission || Activities.Busy) { _askedBy = 0; _goPending = false; return; }
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
                Say($"Co-op: {link.NameOf(_askedBy)} is at one of your mission markers - press Y to join them");
            }
        }

        // Guest side, every tick.
        public void GuestTick(ServerLink link, bool partyMember, PlayerState? host, long now)
        {
            if (_theirsFrom != 0 && _theirsFrom != link.HostId) ClearMarks();   // that host left
            if (!Sync.MissionSteps || !partyMember || !host.HasValue || _theirs.Count == 0) return;
            var me = Game.Player.Character.Position;
            bool atOne = _theirs.Any(o => me.DistanceTo2D(CoopScript.ToGame(o.Position)) <= o.Radius + 4f);
            // Once per arrival: many missions start in camp, and a guest waiting there mustn't keep asking.
            bool arrived = atOne && !_wasAtOne;
            _wasAtOne = atOne;
            var h = host.Value;
            if (!arrived || (h.Flags & (StateFlags.InCutscene | StateFlags.ScreenFaded)) != 0) return;
            if (me.DistanceTo(CoopScript.ToGame(h.Position)) < HostFarAway || now - _lastAskMs < AskEveryMs) return;
            _lastAskMs = now;
            link.SendParty(new PartyMsg { Kind = PartyKind.AtObjective });
            Say($"Co-op: you're at {link.NameOf(link.HostId)}'s mission marker - {link.NameOf(link.HostId)} can press Y to join you");
        }

        public void OnObjectives(byte from, List<Objective> list, ServerLink link)
        {
            if (from != link.HostId || link.IAmHost || !Sync.Markers) return;
            list = list ?? new List<Objective>();
            if (from == _theirsFrom && MissionMarkers.Same(list, _theirs)) return;   // the host repeats its list every few seconds
            ClearMarks();
            _theirs = list;
            _theirsFrom = from;
            string name = Plain(link.NameOf(from)) + "'s mission";
            foreach (var o in _theirs)
            {
                int b = MAP.BLIP_ADD_FOR_COORDS(MISC.GET_HASH_KEY("BLIP_STYLE_OBJECTIVE"), CoopScript.ToGame(o.Position));
                if (b == 0) continue;
                MAP._SET_BLIP_NAME(b, name);
                _marks.Add(b);
            }
            CoopScript.Log($"{link.NameOf(from)}'s mission markers: {_theirs.Count}");
        }

        public void OnAtObjective(byte from, ServerLink link, long now)
        {
            if (!Sync.MissionSteps || !link.IAmHost || MissionProbe.InMissionScene || MissionProbe.StoryMission) return;
            _askedBy = from;
            _offerUntil = now + OfferMs;
            _lastSay = 0;
            CoopScript.Log($"{link.NameOf(from)} asked us to the mission marker");
        }

        // True when the key was ours.
        public bool OnKey(System.Windows.Forms.Keys key)
        {
            if (key != System.Windows.Forms.Keys.Y || _askedBy == 0) return false;
            _goPending = true;   // moved from the tick, not inside the key handler
            return true;
        }

        // Session over or the mod stopping: our markers go, and a move cut off half way doesn't leave the screen black.
        public void Clear()
        {
            ClearMarks();
            _askedBy = 0;
            _goPending = false;
            if (_moving) { _moving = false; CAM.DO_SCREEN_FADE_IN(0); }
        }

        void ClearMarks()
        {
            foreach (var b in _marks) { int x = b; Blips.Remove(ref x); }
            _marks.Clear();
            _theirs = new List<Objective>();
            _theirsFrom = 0;
        }

        // Next to the player who asked, horse and all, behind a fade.
        void GoTo(Vector3 near, string name)
        {
            var me = Game.Player.Character;
            int mover = RideDouble.Mover(me);
            var at = near + new Vector3(2f, 2f, 0f);
            _moving = true;
            CAM.DO_SCREEN_FADE_OUT(300);
            for (int i = 0; i < 20 && !CAM.IS_SCREEN_FADED_OUT(); i++) Script.Yield();
            STREAMING.REQUEST_COLLISION_AT_COORD(at);
            ENTITY.SET_ENTITY_COORDS(mover, at, false, false, false, false);
            for (int i = 0; i < 60 && !ENTITY.HAS_COLLISION_LOADED_AROUND_ENTITY(mover); i++) Script.Yield();
            CAM.DO_SCREEN_FADE_IN(600);
            _moving = false;
            Say($"Co-op: joined {name} at the mission marker");
            CoopScript.Log($"moved to {name} at the mission marker {at}");
        }

        // The subtitles carry partners' names: no game markup (~ < >) in them.
        static void Say(string text) { text = Plain(text); if (!TableGames.Hold(text)) RDR2.UI.Screen.DisplaySubtitle(text); }

        static string Plain(string s) => new string((s ?? "").Where(c => c != '~' && c != '<' && c != '>').ToArray());   // names come from strangers
    }
}
