// The in-game server browser (the status key twice, F10 F10, by default): co-op servers on this network
// and rooms listed online, picked with the arrow keys; Enter asks for the
// room's password with the game's own keyboard. Searching runs on a
// background thread; drawing and the keyboard run from the game's tick.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using RDR2.Native;
using RDR2.UI;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class ServerBrowser
    {
        enum Mode { Closed, List, Password }
        const int Rows = 12;
        const int KeyboardPassword = 2;   // KTEXTTYPE_PASSWORD

        readonly string _listServer;          // empty: online list off
        readonly Func<Room, bool> _isOwn;     // our own built-in server, left out
        readonly Func<bool> _canHost;         // not running our own server: offer to
        readonly Func<string> _hostPassword;  // our own room's password, offered again
        readonly Func<string> _partyLabel;    // the mission party row's text, null when we don't host
        readonly Func<string> _updateLabel;   // the update row's text, null when there is none
        readonly Action _update;
        readonly Action _toggleParty;
        readonly Keys _closeKey;
        Mode _mode;
        volatile List<Room> _rooms;
        volatile string _message;
        int _selected;
        Room _picked;
        long _keyboardSince;

        public ServerBrowser(string listServer, Func<Room, bool> isOwn, Func<bool> canHost, Func<string> hostPassword,
                             Func<string> partyLabel, Action toggleParty, Keys closeKey, Func<string> updateLabel, Action update)
        {
            _partyLabel = partyLabel;
            _updateLabel = updateLabel;
            _update = update;
            _toggleParty = toggleParty;
            _closeKey = closeKey;
            _listServer = listServer ?? "";
            _isOwn = isOwn;
            _canHost = canHost;
            _hostPassword = hostPassword;
        }

        public bool IsOpen => _mode != Mode.Closed;

        public void Toggle()
        {
            if (IsOpen) { Close(); return; }
            _mode = Mode.List;
            Refresh();
        }

        void Close()
        {
            if (_mode == Mode.Password) MISC.CANCEL_ONSCREEN_KEYBOARD();
            _mode = Mode.Closed;
        }

        void Refresh()
        {
            // The menu rows (decided here, on the game's thread) show at once; servers once found.
            var actions = new List<Room>();
            if (_updateLabel() != null) actions.Add(new Room { Name = "Update", Version = Protocol.Version, Max = 4, Update = true });
            if (_partyLabel() != null) actions.Add(new Room { Name = "Mission party", Version = Protocol.Version, Max = 4, Party = true });
            if (_canHost()) actions.Add(new Room { Name = "Host your own game here", Version = Protocol.Version, Max = 4, HostHere = true });
            _rooms = actions;
            _message = "Searching for games...";
            _selected = 0;
            new Thread(() =>
            {
                var rooms = new List<Room>();
                string note = null;
                try { rooms.AddRange(RoomList.QueryLan(ServerLink.DefaultPort)); } catch (Exception e) { CoopScript.Log("browser LAN: " + e.Message); }
                if (_listServer.Length > 0)
                {
                    try { rooms.AddRange(RoomList.FetchOnline(_listServer).Where(r => r != null)); }
                    catch (Exception e) { note = "Online list unavailable: " + e.Message; CoopScript.Log("browser online: " + e.Message); }
                }
                else note = "Online list is off (ListServer= in RDR2Coop.ini).";
                rooms = rooms.Where(r => !_isOwn(r)).ToList();
                _message = note ?? (rooms.Count == 0 ? "No games found. R searches again." : null);
                _rooms = actions.Concat(rooms).ToList();
            }) { IsBackground = true, Name = "RDR2Coop.Browser" }.Start();
        }

        // True when the key was for the browser (the game shouldn't act on it).
        public bool OnKey(Keys key)
        {
            if (_mode != Mode.List) return _mode == Mode.Password;
            var rooms = _rooms;
            switch (key)
            {
                case Keys.Up: if (rooms?.Count > 0) _selected = (_selected + rooms.Count - 1) % rooms.Count; return true;
                case Keys.Down: if (rooms?.Count > 0) _selected = (_selected + 1) % rooms.Count; return true;
                case Keys.R: Refresh(); return true;
                case Keys.Escape: Close(); return true;
                case Keys.Enter:
                    if (rooms == null || _selected >= rooms.Count) return true;
                    var room = rooms[_selected];
                    if (room.Party) { Close(); _toggleParty(); return true; }
                    if (room.Update) { _update(); return true; }
                    if (!room.HostHere && room.Version != Protocol.Version) { _message = $"'{room.Name}' runs another co-op version (protocol {room.Version}, yours {Protocol.Version})."; return true; }
                    if (!room.HostHere && room.Players >= room.Max) { _message = $"'{room.Name}' is full."; return true; }
                    _picked = room;
                    _mode = Mode.Password;
                    _keyboardSince = Environment.TickCount;
                    // Empty, never prefilled: a hidden prefilled password got typed in a second time.
                    MISC.DISPLAY_ONSCREEN_KEYBOARD(KeyboardPassword, "", "", "", "", "", "", 64);
                    return true;
            }
            return false;
        }

        // Every tick while open. A room to join (and its password) once picked, else null.
        public Room Tick(string fallbackPassword, out string password)
        {
            password = null;
            if (_mode == Mode.List)
            {
                PAD.DISABLE_ALL_CONTROL_ACTIONS(0);
                Draw();
                return null;
            }
            if (_mode != Mode.Password) return null;
            // The game's keyboard only shows built-in text as its title: ours goes above it.
            DrawLines(new List<(string, Color)>
            {
                _picked.HostHere ? ("Password for your game", Color.FromArgb(255, 200, 40, 40))
                                 : ($"Room password for '{_picked.Name}'", Color.FromArgb(255, 200, 40, 40)),
                (_picked.HostHere ? (string.IsNullOrEmpty(_hostPassword()) ? "Type one (tell your friends), then Enter (Esc goes back)"
                                                                           : "Enter keeps your saved one, or type a new one (Esc goes back)")
                                  : "Type it, then Enter to join (Esc goes back to the list)", Color.White),
            });
            int status = MISC.UPDATE_ONSCREEN_KEYBOARD();
            if (status == 1)
            {
                password = MISC.GET_ONSCREEN_KEYBOARD_RESULT() ?? "";
                _mode = Mode.Closed;
                return _picked;
            }
            if (status == 2) { _mode = Mode.List; return null; }   // cancelled
            if (status == 3 && Environment.TickCount - _keyboardSince > 1000)
            {
                // The game's keyboard didn't open: use the password from the settings file.
                CoopScript.Log("browser: on-screen keyboard unavailable, using the ini password");
                password = fallbackPassword;
                _mode = Mode.Closed;
                return _picked;
            }
            return null;
        }

        void Draw()
        {
            var rooms = _rooms ?? new List<Room>();
            int first = Math.Max(0, Math.Min(_selected - Rows / 2, rooms.Count - Rows));
            var lines = new List<(string text, Color color)>
            {
                ($"Co-op   (Up/Down pick, Enter choose, R search again, {_closeKey} close)", Color.FromArgb(255, 200, 40, 40))
            };
            for (int i = first; i < Math.Min(rooms.Count, first + Rows); i++)
            {
                var r = rooms[i];
                string where = r.Lan ? "home network" : "online";
                string text = r.Update ? $"{(i == _selected ? "> " : "   ")}{_updateLabel() ?? "Updated: press Insert to load it"}"
                            : r.Party ? $"{(i == _selected ? "> " : "   ")}{_partyLabel() ?? "Mission party (host only)"}"
                            : r.HostHere ? $"{(i == _selected ? "> " : "   ")}Host your own game here (your server, your story world)"
                            : $"{(i == _selected ? "> " : "   ")}{r.Name}   {r.Players}/{r.Max}   {where}" +
                              (r.Version != Protocol.Version ? "   (other version)" : "");
                lines.Add((text, r.Version != Protocol.Version || r.Players >= r.Max ? Color.Gray : i == _selected ? Color.Gold : Color.White));
            }
            if (_message != null) lines.Add((_message, Color.LightGray));
            DrawLines(lines);
        }

        static void DrawLines(List<(string text, Color color)> lines) => Ui.Panel(lines);
    }
}
