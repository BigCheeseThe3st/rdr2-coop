// The party list (0.8.0, [Convenience] PartyList): everyone in the session with
// their health, who's down, how far away and which way (src/Net/PartyLines.cs
// words it). It shows for a few seconds with the status key (F10) and when
// someone joins, leaves or goes down; [Keys] PartyList= sets a key that keeps it
// on (and off again). Never over a story cutscene, the pause menu or map, or a
// black screen. It creates nothing in the world: it only draws.
// Where it sits, on the 1280x720 layout Ui.Panel uses: the top right, its box's
// right edge 28 px from the screen's edge, from y 50 down (22 px a line, at most
// five lines). The co-op menu and the enRage nemeses list are top left and
// subtitles bottom centre, so the top right stays ours: chat goes elsewhere.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RDR2;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class PartyList
    {
        const int ShowMs = 6000, RefreshMs = 250;
        static readonly Color Title = Color.FromArgb(255, 200, 40, 40), Alert = Color.FromArgb(255, 255, 110, 60);

        public Keys Key;                  // [Keys] PartyList: keeps it on, or off again
        bool _on;
        long _until, _refreshedMs = -RefreshMs, _healthMs = -RefreshMs;
        byte _health;
        HashSet<byte> _down = new HashSet<byte>();
        List<(string text, Color color)> _lines = new List<(string, Color)>();
        float _width;

        // For a few seconds: the status key, someone joining or leaving.
        public void Show(long now) => _until = now + ShowMs;

        public bool OnKey(Keys key)
        {
            if (!Extras.PartyList || Key == Keys.None || key != Key) return false;
            _on = !_on;
            _until = 0;   // off means off now, not after the few seconds
            return true;
        }

        // Our own health for PlayerState.Health (0, "unknown", with the switch off). Read every 250 ms at most.
        public byte Health(int ped, long now)
        {
            if (!Extras.PartyList) return 0;
            if (now - _healthMs >= RefreshMs)
            {
                _healthMs = now;
                _health = PartyLines.HealthByte(ENTITY.GET_ENTITY_HEALTH(ped), PED.GET_PED_MAX_HEALTH(ped));
            }
            return _health;
        }

        // Every tick while connected: the lines are made every 250 ms (also to notice
        // someone going down while it's hidden), drawn every frame while it shows.
        public void Tick(ServerLink link, Func<byte, PlayerState?> stateOf, Ped me, bool meDown, long now)
        {
            if (!Extras.PartyList) return;
            if (now - _refreshedMs >= RefreshMs) { _refreshedMs = now; Refresh(link, stateOf, me, meDown, now); }
            if (!_on && now >= _until) return;
            if (MissionProbe.InMissionScene || HUD.IS_PAUSE_MENU_ACTIVE() || CAM.IS_SCREEN_FADED_OUT() || CAM.IS_SCREEN_FADING_OUT()) return;
            Ui.Panel(_lines, _width, 1264f - _width);
        }

        void Refresh(ServerLink link, Func<byte, PlayerState?> stateOf, Ped me, bool meDown, long now)
        {
            var at = CoopScript.ToNet(me.Position);
            var mine = new PlayerState { Position = at, Health = Health(me.Handle, now),
                                         Flags = me.IsDead ? StateFlags.Dead : meDown ? StateFlags.Downed : StateFlags.None };
            var lines = new List<(string text, Color color)> { ("Co-op party", Title) };
            var down = new HashSet<byte>();
            foreach (var e in link.Roster.OrderBy(e => e.Id != link.MyId))   // us first
            {
                bool you = e.Id == link.MyId;
                var s = you ? mine : stateOf(e.Id);
                var flags = s?.Flags ?? StateFlags.None;
                if ((flags & StateFlags.Downed) != 0) down.Add(e.Id);
                lines.Add((PartyLines.Line(e.Name, e.Role == PlayerRole.Host, you, s, at),
                           (flags & (StateFlags.Downed | StateFlags.Dead)) != 0 ? Alert : Color.White));
            }
            if (down.Except(_down).Any()) Show(now);   // someone just went down
            _down = down;
            _lines = lines;
            // About 9 px a character at the panel's text size, plus room: the box grows with the longest line.
            _width = Math.Min(560f, 32f + 10f * lines.Max(l => l.text.Length));
        }
    }
}
