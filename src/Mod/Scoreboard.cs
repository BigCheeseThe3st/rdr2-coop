// The session scoreboard (0.9.0, [Convenience] Scoreboard): one line per player with their kills
// (humans/animals), times down, revives, money won and lost at the mod's tables and contests, and
// contest wins (race/duel/shooting/hunting): ours from Stats, partners' from the "stats" Score notices
// everyone broadcasts every 5 s when theirs changed (src/Net/Score.cs; Kills.cs credits the kills).
// Toggled by [Keys] Scoreboard= or the co-op menu's "Scoreboard" row. Our own counters last until the
// game is restarted; a partner's go when they leave. It draws nothing else and creates nothing.
// Where it sits, on the 1280x720 layout Ui.Panel uses: top right, its box's right edge at x 1264, first
// line at y 210: under the party list (which ends by y 180), in the slot the table and contest panels use
// too, so it stays hidden while you're at a table (Activities.Busy) and yields while a contest's panel shows
// (Contests.Showing, passed in as hide); never over a story cutscene, the pause menu or a black screen.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class Scoreboard
    {
        const int ShareMs = 5000, RefreshMs = 500, Top = 210, PerSecond = 5;
        static readonly Color[] Tones = { Color.White, Color.FromArgb(255, 235, 190, 70), Color.FromArgb(255, 170, 170, 170), Color.FromArgb(255, 200, 40, 40) };

        public Keys Key;   // [Keys] Scoreboard: show, press again to hide (the menu row does the same)
        public bool On { get; private set; }
        readonly Dictionary<byte, Dictionary<string, int>> _theirs = new Dictionary<byte, Dictionary<string, int>>();
        readonly Dictionary<byte, (long since, int n)> _rate = new Dictionary<byte, (long, int)>();   // notices per sender this second
        string _shared = "";
        long _sharedMs, _refreshedMs;
        List<(string text, Color color)> _lines = new List<(string, Color)>();
        float _width;

        public void Toggle() { if (Extras.Scoreboard) On = !On; }

        public bool OnKey(Keys key)
        {
            if (!Extras.Scoreboard || Key == Keys.None || key != Key) return false;
            Toggle();
            return true;
        }

        // Every tick while connected: our stats go out every 5 s when they changed; the panel while it's on
        // and nothing else has its slot (hide).
        public void Tick(ServerLink link, long now, bool hide)
        {
            if (!Extras.Scoreboard) return;
            if (now - _sharedMs >= ShareMs)
            {
                _sharedMs = now;
                string line = Score.StatsLine(Stats.All());
                if (line != _shared) { _shared = line; link.SendNotice(Protocol.Broadcast, NoticeKind.Score, line); }
            }
            if (!On || hide) return;
            if (now - _refreshedMs >= RefreshMs)
            {
                _refreshedMs = now;
                var mine = Stats.All();
                _lines = Score.Lines(link.Roster.Select(e => (e.Id, e.Name)), link.MyId, id => id == link.MyId ? mine : _theirs.TryGetValue(id, out var s) ? s : null)
                              .Select(l => (text: l.text, color: Tones[l.tone])).ToList();
                _width = Math.Min(640f, 32f + 10f * _lines.Max(l => l.text.Length));   // ~10 px a character, as the party list reckons
            }
            if (Activities.Busy || MissionProbe.InMissionScene || HUD.IS_PAUSE_MENU_ACTIVE() || CAM.IS_SCREEN_FADED_OUT() || CAM.IS_SCREEN_FADING_OUT()) return;
            Ui.Panel(_lines, _width, 1264f - _width, Top);
        }

        // A Score notice from someone in the roster: "stats" kept as theirs, "kill" lines for Kills (the area owner's only;
        // the hunting contest needs them with the scoreboard off).
        public void OnNotice(byte from, string text, ServerLink link, byte owner, long now)
        {
            if (!(Extras.Scoreboard || Games.Hunting) || !link.Roster.Any(e => e.Id == from)) return;
            (long since, int n) r = _rate.TryGetValue(from, out var x) && now - x.since < 1000 ? x : (now, 0);
            _rate[from] = (r.since, r.n + 1);
            if (r.n >= PerSecond) return;   // a flood: the rest of this second is dropped
            var stats = Score.ParseStats(text);
            if (stats != null) _theirs[from] = stats;
            else Kills.OnCredit(from, text, link, owner);
        }

        public void OnRoster(HashSet<byte> ids)
        {
            foreach (var id in _theirs.Keys.Where(id => !ids.Contains(id)).ToList()) _theirs.Remove(id);
            _shared = "";   // newcomers need our counters
        }

        public void Clear()
        {
            On = false;
            _theirs.Clear();
            _rate.Clear();
            _shared = "";
            Kills.Clear();
        }
    }
}
