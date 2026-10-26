// Contests (0.9.0): the game side of src/Net/Contest.cs. "Start a contest" in the co-op menu (F10 twice) lists the
// kinds whose [Games] switch is on; the kind asks the host its questions (pick-lists) and the contest opens:
// everyone within 60 m of the host who isn't busy is in (their game answers by itself), a 10 s countdown with a
// subtitle each second, GO, the standings every 2 s, the results for 15 s, then it's cleaned up. One contest at a
// time in a game. The one we host is called off when we start a story mission, leave or stop the mod; one whose
// host leaves ends for everyone; anyone can quit one from the menu ("Quit the race": the host takes them off the
// standings and it goes on without them). A kind's code that throws calls its contest off (logged), nothing more.
// Where the panel sits, on the 1280x720 layout Ui.Panel uses: top right, its box's right edge at x 1264, first
// line at y 210, under the party list, in the slot the table panels and the scoreboard use too (the scoreboard
// yields while Showing); 12 lines at most, whatever a host sends; nothing is drawn in a story mission or cutscene,
// at one of the game's tables, in the pause menu or on a black screen.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using RDR2.Native;
using RDR2Coop.Net;
using LinkState = RDR2Coop.Net.LinkState;
using Screen = RDR2.UI.Screen;

namespace RDR2Coop.Mod
{
    sealed class Contests
    {
        // A kind: how to make one, the host's setup (asks with pick-lists, then calls back with the params string,
        // null when they changed their mind) and its game side, ticked with the contest while one exists, then once
        // with null when it's gone (for its blips and markers).
        sealed class Kind { public string Name; public Func<Contest> Make; public Action<Action<string>> Setup; public Action<Contest, long> View; }

        const int Top = 210;
        static readonly List<Kind> Kinds = new List<Kind>();
        static readonly Color[] Tones = { Color.White, Color.FromArgb(255, 235, 190, 70), Color.FromArgb(255, 170, 170, 170), Color.FromArgb(255, 200, 40, 40), Color.FromArgb(255, 90, 200, 90) };

        public static void Register(string kind, Func<Contest> make, Action<Action<string>> setup, Action<Contest, long> view = null) =>
            Kinds.Add(new Kind { Name = kind, Make = make, Setup = setup, View = view });

        ContestRun _run;
        Kind _kind;
        uint _seq;
        Vec3 _myPos;
        long _now;
        byte _me;
        Func<byte, Vec3?> _theirPos = id => null;   // where we last saw each partner (CoopScript's, with every notice)
        readonly Dictionary<byte, BlastGate> _gates = new Dictionary<byte, BlastGate>();   // 5 notices a second per sender

        static bool On(string kind)
        {
            switch (kind) { case "race": return Games.Race; case "duel": return Games.Duel; case "shooting": return Games.Shooting; case "hunting": return Games.Hunting; default: return true; }
        }

        static IEnumerable<Kind> Open => Kinds.Where(k => On(k.Name));
        bool Live => _run != null && !_run.Ended;
        public bool Showing => _run != null;   // the panel has the top-right slot (countdown, standings or results)

        bool _menuLive;   // the menu row said "Call off"/"Quit" when the menu opened

        public string MenuLabel(ServerLink link)
        {
            if (link == null || link.State != LinkState.Connected || !Open.Any()) return null;
            _menuLive = Live;
            return Live ? (_run.IAmHost ? "Call off the " : "Quit the ") + _run.Game.Kind : "Start a contest";
        }

        Vec3? Where(byte id) => id == _me ? _myPos : _theirPos(id);   // a contest's PosOf: us too

        public void MenuAct(ServerLink link)
        {
            if (link == null || link.State != LinkState.Connected) return;
            if (Live && !_menuLive) { Say("Co-op: you're already in one"); return; }   // a stale "Start a contest" row never quits it
            if (Live) { Say(_run.IAmHost ? $"Co-op: {_run.Title} called off" : $"Co-op: you left the {_run.Game.Kind}"); _run.Cancel(); Flush(link); Drop(); return; }
            if (Activities.Busy || MissionProbe.InMissionScene || MissionProbe.StoryMission) { Say("Co-op: finish what you're doing first"); return; }
            var rows = Open.Select(k => (char.ToUpperInvariant(k.Name[0]) + k.Name.Substring(1), (Action)(() => k.Setup(prms => Start(k, prms, link))))).ToList();
            PickList.Open("Start a contest", rows, null, "Everyone within 60 m of you is in");
        }

        void Start(Kind kind, string prms, ServerLink link)
        {
            if (prms == null || Live || link.State != LinkState.Connected) return;
            if (_run != null) Drop();   // the last one's results make way
            _me = link.MyId;
            _run = ContestRun.Open(link.MyId, ++_seq, kind.Make(), prms, _now);
            _run.Game.NameOf = link.NameOf;
            _run.Game.PosOf = Where;
            _kind = kind;
            CoopScript.Log($"contest {_run.Id}: {kind.Name} '{prms}' opened by us");
            Flush(link);
        }

        // Every tick while connected, with where we are.
        public void Tick(ServerLink link, Vec3 myPos, long now)
        {
            _myPos = myPos;
            _now = now;
            if (_run == null) return;
            try
            {
                if (_run.IAmHost && !_run.Ended && MissionProbe.StoryMission) { Say($"Co-op: {_run.Title} called off: you started a mission"); _run.Cancel(); }
                else View(_run.Game, now);   // before the run asks the kind what to report
                Said(_run.Tick(now));
                Flush(link);
                if (_run.Done) { Drop(); return; }
                if (Activities.Busy || MissionProbe.InMissionScene || MissionProbe.StoryMission || HUD.IS_PAUSE_MENU_ACTIVE() || CAM.IS_SCREEN_FADED_OUT() || CAM.IS_SCREEN_FADING_OUT()) return;
                var lines = _run.Lines(now).Take(12).Select(l => (l.text, Tones[Math.Max(0, Math.Min(Tones.Length - 1, l.tone))])).ToList();   // a title, a winner and 4 players fit; a host's text is only bounded by the notice size
                if (lines.Count == 0) return;
                float width = Math.Min(640f, 32f + 10f * lines.Max(l => l.Item1.Length));   // ~10 px a character, as the party list reckons
                Ui.Panel(lines, width, 1264f - width, Top);
            }
            catch (Exception e) { Fail(link, e); }
        }

        // A kind's code threw: log it once, call the contest off (the host tells everyone), clean up.
        void Fail(ServerLink link, Exception e)
        {
            CoopScript.Log($"contest {_run?.Id} {_kind?.Name}: {e.GetType().Name}: {e.Message}");
            if (_run == null) return;
            _run.Out.Clear();   // whatever it was halfway through saying
            _run.Cancel();
            Flush(link);
            Drop();
        }

        // A Contest notice from someone in the roster: ours by id; an open near us puts us in.
        public void OnNotice(byte from, string text, ServerLink link, Func<byte, Vec3?> posOf, long now)
        {
            if (!Open.Any() || !link.Roster.Any(e => e.Id == from)) return;
            _theirPos = posOf;
            _me = link.MyId;
            if (!_gates.TryGetValue(from, out var gate)) _gates[from] = gate = new BlastGate();
            if (!gate.Allow(now) || !ContestMsg.Parse(text, out string verb, out string id, out string rest)) return;
            if (_run != null && _run.Id == id)
            {
                if (verb == "open") return;
                try { Said(_run.OnNotice(from, verb, rest, now)); }
                catch (Exception e) { Fail(link, e); return; }
                Flush(link);
                if (_run.Done) Drop();
                return;
            }
            if (verb != "open" || ContestMsg.HostOf(id) != from || Live) return;   // one at a time; unknown ids and others' ids are ignored
            int sp = rest.IndexOf(' ');
            string kind = sp < 0 ? rest : rest.Substring(0, sp), prms = sp < 0 ? "" : rest.Substring(sp + 1);
            var k = Kinds.Find(x => x.Name == kind);
            if (k == null || !On(kind) || Activities.Busy || MissionProbe.InMissionScene || MissionProbe.StoryMission) return;
            var at = posOf(from);
            if (at == null || Vec3.Distance(at.Value, _myPos) > ContestRun.JoinRange) return;
            if (_run != null) Drop();
            _run = ContestRun.Join(link.MyId, from, id, k.Make(), prms, now);
            _run.Game.NameOf = link.NameOf;
            _run.Game.PosOf = Where;
            _kind = k;
            Say($"Co-op: {link.NameOf(from)} started a {kind}: you're in");
            CoopScript.Log($"contest {id}: {kind} '{prms}' by {link.NameOf(from)}, we're in");
            Flush(link);
        }

        public void OnRoster(HashSet<byte> ids, ServerLink link, long now)
        {
            if (_run == null) return;
            try { foreach (var id in _run.Players.Concat(new[] { _run.Host }).Where(id => !ids.Contains(id)).Distinct().ToList()) Said(_run.PlayerLeft(id, now)); }
            catch (Exception e) { Fail(link, e); return; }
            Flush(link);
            if (_run.Done) Drop();
        }

        // The session's over or the mod stops: ours is called off, the kind's blips and markers go.
        public void Clear(ServerLink link)
        {
            if (_run == null) return;
            _run.Cancel();
            if (link != null) Flush(link);
            Drop();
        }

        void Drop()
        {
            CoopScript.Log($"contest {_run.Id}: {_run.Why}");
            View(null, _now);
            _run = null;
        }

        void View(Contest game, long now)
        {
            try { _kind.View?.Invoke(game, now); }
            catch (Exception e) when (game == null) { CoopScript.Log($"contest {_kind.Name} view: {e.Message}"); }   // with a contest it goes to Fail: off, logged once
        }

        void Flush(ServerLink link)
        {
            foreach (var m in _run.Out) link.SendNotice(m.to, NoticeKind.Contest, m.text);
            _run.Out.Clear();
        }

        static void Said(string text) { if (text != null) Say(text); }
        static void Say(string text)   // waits while we play at a card table; names come from strangers: no ~ < >
        {
            text = new string(text.Where(c => c != '~' && c != '<' && c != '>').ToArray());
            if (!TableGames.Hold(text)) Screen.DisplaySubtitle(text);
        }
    }
}
