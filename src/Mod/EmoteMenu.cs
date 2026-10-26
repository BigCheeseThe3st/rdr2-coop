// The emote menu (0.9.0, [Convenience] EmoteMenu): the co-op menu's "Emote" row, or [Keys] EmoteMenu=, opens
// a pick-list of src/Net/EmoteList.cs. We play the gesture on the upper body (we can keep walking or riding),
// and partners get "play <index>" (NoticeKind.Emote) and play the same on our stand-in (Puppet.PlayEmote).
// An animation streams in over a few ticks, never waited for: given 2 s, then "that emote isn't available"
// (a partner's is just skipped) and one log line per animation. No emotes in a story cutscene or mission, at
// the game's tables or other activities (Activities.Busy), while down, in a vehicle or swimming; a partner's
// shows only on a stand-in that's here. Nothing to remove afterwards: a gesture ends by itself, and the
// animations we asked for are let go in Clear. It draws nothing of its own (the pick-list does).
using System;
using System.Collections.Generic;
using System.Linq;
using Keys = System.Windows.Forms.Keys;
using RDR2.Native;
using RDR2.UI;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class EmoteMenu
    {
        const int LoadMs = 2000;

        public Keys Key;   // [Keys] EmoteMenu: opens the list (empty = the co-op menu's row only)
        sealed class Pending { public byte Who; public int Index; public long Since = -1; }   // Who 0: us
        readonly List<Pending> _pending = new List<Pending>();   // one per player at most
        readonly Dictionary<byte, BlastGate> _gates = new Dictionary<byte, BlastGate>();   // 5 notices a second per sender
        readonly HashSet<string> _asked = new HashSet<string>(), _missing = new HashSet<string>();

        static bool Allowed => Extras.EmoteMenu && !MissionProbe.InMissionScene && !MissionProbe.StoryMission && !Activities.Busy;

        public string MenuLabel(ServerLink link) => link != null && Allowed ? "Emote" : null;

        public bool OnKey(Keys key)
        {
            if (!Extras.EmoteMenu || Key == Keys.None || key != Key) return false;
            Open();
            return true;
        }

        public void Open()
        {
            if (!Allowed) { Say("Co-op: no emotes in a cutscene, a mission or a game"); return; }
            PickList.Open("Emote (your partners see it)", EmoteList.All.Select((e, i) =>
                (e.Label + (_missing.Contains(e.Dict) ? " (not available)" : ""), (Action)(() => Queue(0, i)))).ToList());
        }

        // A partner's Emote notice: "play <index>" from someone in the roster, 5 a second at most.
        public void OnNotice(byte from, string text, ServerLink link, long now)
        {
            if (!Allowed || from == link.MyId || !link.Roster.Any(e => e.Id == from)) return;
            if (!_gates.TryGetValue(from, out var gate)) _gates[from] = gate = new BlastGate();
            if (!gate.Allow(now)) return;
            int i = EmoteList.Parse(text);
            if (i >= 0) Queue(from, i);
        }

        void Queue(byte who, int index)
        {
            _pending.RemoveAll(p => p.Who == who);   // a newer emote replaces one still loading
            _pending.Add(new Pending { Who = who, Index = index });
        }

        // Every tick while connected, after the stand-ins are placed: loads what's asked for, then plays it.
        public void Tick(ServerLink link, int myPed, bool down, Dictionary<byte, Puppet> puppets, long now)
        {
            if (_pending.Count == 0) return;
            foreach (var p in _pending.ToList())
            {
                var e = EmoteList.All[p.Index];
                Puppet puppet = null;
                if (!Allowed || (p.Who != 0 && !puppets.TryGetValue(p.Who, out puppet))) { _pending.Remove(p); continue; }   // they're not here
                if (p.Since < 0)
                {
                    p.Since = now;
                    if (STREAMING.DOES_ANIM_DICT_EXIST(e.Dict)) { STREAMING.REQUEST_ANIM_DICT(e.Dict); _asked.Add(e.Dict); }
                    else p.Since = now - LoadMs - 1;   // not in this game: give up now
                }
                if (!STREAMING.HAS_ANIM_DICT_LOADED(e.Dict))
                {
                    if (now - p.Since <= LoadMs) continue;
                    _pending.Remove(p);
                    if (_missing.Add(e.Dict)) CoopScript.Log($"emote '{e.Label}': {e.Dict} didn't load in {LoadMs / 1000} s");
                    if (p.Who == 0) Say("Co-op: that emote isn't available");
                    continue;
                }
                _pending.Remove(p);
                _missing.Remove(e.Dict);
                if (p.Who != 0) { puppet.PlayEmote(e.Dict, e.Clip, e.Ms); continue; }
                if (down || ENTITY.IS_ENTITY_DEAD(myPed) || PED.IS_PED_IN_ANY_VEHICLE(myPed, false) || PED.IS_PED_SWIMMING(myPed) || PED.IS_PED_RAGDOLL(myPed))
                {
                    Say("Co-op: you can't emote right now");
                    continue;
                }
                TASK.TASK_PLAY_ANIM(myPed, e.Dict, e.Clip, 8f, -8f, e.Ms, EmoteList.Flags, 0f, false, 0, false, "", false);
                link.SendNotice(Protocol.Broadcast, NoticeKind.Emote, EmoteList.Line(p.Index));
                Stats.Add("emotes");
            }
        }

        public void Clear()
        {
            _pending.Clear();
            _gates.Clear();
            foreach (var d in _asked) STREAMING.REMOVE_ANIM_DICT(d);
            _asked.Clear();
        }

        static void Say(string text) { if (!TableGames.Hold(text)) Screen.DisplaySubtitle(text); }   // waits while we play at a card table
    }
}
