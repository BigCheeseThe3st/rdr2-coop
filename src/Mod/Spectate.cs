// Spectate (0.9.0, [Convenience] Spectate): while we're down or dead, watch a partner. The co-op menu's
// "Watch <name>" row, or the teleport key (F9: it can't take a downed player anywhere useful), puts a
// scripted camera behind and above that partner's stand-in; the key again goes to the next partner
// (SpectateOrder, src/Net/Spectate.cs). It stops when we get up or respawn, when their stand-in goes
// (they left, rode out of puppet range or it was remade as another model), on Esc, in a story cutscene,
// mission or mini-game, when the switch is off, and in ClearSession / OnAborted. Nothing is sent: partners
// don't know they're watched. Its line sits top centre (box 6-48 px on the 1280x720 layout, above the
// co-op menu and the party list, which start at y 50), centred on the screen.
// Test alone: FakePlayer "owner down" (it hits you until you're down) with a "partner walk" connected too.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RDR2;
using RDR2.Native;
using RDR2Coop.Net;
using Screen = RDR2.UI.Screen;

namespace RDR2Coop.Mod
{
    sealed class Spectator
    {
        const int CheckMs = 200;
        static readonly uint[] Pause = { 0xD82E0BD2, 0x4A903C11 };   // INPUT_FRONTEND_PAUSE, INPUT_FRONTEND_PAUSE_ALTERNATE: Esc stops watching, not opens the pause menu

        public Keys Key = Keys.F9;   // the teleport key: next partner
        public string MenuLabel { get; private set; }   // null hides the row
        int _cam, _ped;     // our camera and the stand-in it's on (0: none)
        byte _who;          // whose stand-in that is
        string _whoName = "";   // kept: once they've left, the roster no longer has it
        float _width;
        bool _can, _next, _stop;   // keys and the menu only set these: the tick does the work
        long _checkedMs;
        List<(string text, Color color)> _line;

        bool Watching => _cam != 0;

        public bool OnKey(Keys key)
        {
            if (!_can) return false;
            if (key == Key) { _next = true; return true; }
            if (key == Keys.Escape && Watching) { _stop = true; return true; }
            return false;
        }

        public void MenuAct() { if (Watching) _stop = true; else _next = true; }

        // Every tick while connected. downOrDead: DownState.IsDown or our character is dead.
        // puppetOf: a partner's stand-in handle, 0 when there's none (too far, or not spawned yet).
        public void Tick(bool downOrDead, ServerLink link, Func<byte, int> puppetOf, long now)
        {
            _can = Extras.Spectate && downOrDead && !MissionProbe.InMissionScene && !MissionProbe.StoryMission && !Activities.Busy;
            if (!_can) { _next = _stop = false; MenuLabel = null; Stop(); return; }
            if (Watching) foreach (uint input in Pause) PAD.DISABLE_CONTROL_ACTION(0, input, false);   // this frame too when Esc stops it
            if (_stop) { _stop = false; Stop(); }
            if (_next) { _next = false; Switch(link, puppetOf); }
            if (now - _checkedMs >= CheckMs)
            {
                _checkedMs = now;
                if (Watching)
                {
                    int ped = puppetOf(_who);
                    if (ped == 0) { Screen.DisplaySubtitle($"Co-op: {_whoName} is out of sight"); Stop(); }
                    else if (ped != _ped) Watch(ped);   // their stand-in was made again (another model)
                }
                byte id = Watching ? _who : Pick(link, puppetOf, out _);
                MenuLabel = id == 0 ? null : (Watching ? "Stop watching " : "Watch ") + link.NameOf(id);
            }
            if (Watching && !HUD.IS_PAUSE_MENU_ACTIVE() && !CAM.IS_SCREEN_FADED_OUT()) Ui.Panel(_line, _width, 652f - _width / 2f, 16f);   // centred on 640
        }

        // The next partner in sight, or (near false) the next one at all, for "too far to watch"; 0: nobody else here.
        byte Pick(ServerLink link, Func<byte, int> puppetOf, out bool near)
        {
            var partners = link.Roster.Select(e => e.Id).Where(p => p != link.MyId).ToList();
            byte id = SpectateOrder.Next(partners, _who, i => puppetOf(i) != 0);
            near = id != 0;
            return near ? id : SpectateOrder.Next(partners, _who, i => true);
        }

        void Switch(ServerLink link, Func<byte, int> puppetOf)
        {
            byte id = Pick(link, puppetOf, out bool near);
            if (id == 0) { Screen.DisplaySubtitle("Co-op: nobody to watch"); return; }
            if (!near) { Screen.DisplaySubtitle($"Co-op: {link.NameOf(id)} is too far to watch"); return; }
            if (id == _who && Watching) return;
            _who = id;
            Watch(puppetOf(id));
            if (!Watching) { _who = 0; return; }
            _whoName = link.NameOf(id);
            string line = $"Watching {_whoName} ({Key}: next, Esc or getting up: stop)";
            _line = new List<(string, Color)> { (line, Color.White) };
            _width = Math.Min(900f, 32f + 10f * line.Length);   // ~10 px a character, as the party list reckons
            CoopScript.Log("spectate: watching " + _whoName);
        }

        // Onto that stand-in: 4.5 m behind it and 1.8 m up (relative, so it turns with them), looking at its chest.
        void Watch(int ped)
        {
            bool fresh = _cam == 0;
            if (fresh)
            {
                _cam = CAM.CREATE_CAM("DEFAULT_SCRIPTED_CAMERA", false);
                if (!CAM.DOES_CAM_EXIST(_cam)) { _cam = 0; CoopScript.Log("spectate: no camera"); return; }
            }
            CAM.ATTACH_CAM_TO_ENTITY(_cam, ped, 0f, -4.5f, 1.8f, true);
            CAM.POINT_CAM_AT_ENTITY(_cam, ped, 0f, 0f, 0.5f, true);
            _ped = ped;
            if (!fresh) return;
            CAM.SET_CAM_ACTIVE(_cam, true);
            CAM.RENDER_SCRIPT_CAMS(true, false, 3000, true, false, 0);   // the arguments the game's poker script uses
        }

        // Back to the game's own camera. Safe to call any time.
        public void Stop()
        {
            if (_cam == 0) { _who = 0; _ped = 0; return; }
            CAM.SET_CAM_ACTIVE(_cam, false);
            CAM.RENDER_SCRIPT_CAMS(false, false, 3000, true, false, 0);
            CAM.DESTROY_CAM(_cam, false);
            _cam = 0; _ped = 0; _who = 0;
            CoopScript.Log("spectate: stopped");
        }
    }
}
