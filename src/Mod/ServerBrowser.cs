// The in-game server browser (the status key twice, F10 F10, by default): co-op servers on this network
// and rooms listed online, picked with the arrow keys; Enter asks for the
// room's password with the game's own keyboard. Searching runs on a
// background thread; drawing and the keyboard run from the game's tick. F narrows the list to
// names containing what you type (the author: "let's add a search box for the server browser").
// Features add their own rows (0.9.0): ServerBrowser.Extras, one MenuExtra each, registered in CoopScript.Start;
// a row whose label comes back null is hidden for now (e.g. "Join the poker table" when there is none).
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
    // A row a feature adds to the co-op menu: its label now (null = not shown) and what Enter does (the menu closes first).
    sealed class MenuExtra
    {
        public const string PlayTogether = "Play together", More = "More";
        public readonly Func<string> Label;
        public readonly Action Act;
        public readonly string Section;   // the tab it's listed under
        public readonly Func<string> Help;  // what it does, for the menu's details panel (null: the label says it)
        public MenuExtra(Func<string> label, Action act, string section = PlayTogether, Func<string> help = null) { Label = label; Act = act; Section = section; Help = help; }
    }

    sealed class ServerBrowser
    {
        public static readonly List<MenuExtra> Extras = new List<MenuExtra>();
        public static Func<string> Status;   // one line under the title: connected or not, who's here, whose world this is
        const string Session = "Session", Games = "Games";

        enum Mode { Closed, List, Password, Search }
        const int Rows = 12;
        const int KeyboardPassword = 2;   // KTEXTTYPE_PASSWORD
        const int KeyboardText = 0;       // KTEXTTYPE_DEFAULT
        const int SearchChars = 30;

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
        int _tab;   // index into Tabs
        string _filter = "";
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
            _filter = "";
            _tab = 0;
            Refresh();
        }

        void Close()
        {
            if (_mode == Mode.Password || _mode == Mode.Search) MISC.CANCEL_ONSCREEN_KEYBOARD();
            _mode = Mode.Closed;
        }

        void Refresh()
        {
            // The menu rows (decided here, on the game's thread) show at once; servers once found.
            var actions = new List<Room>();
            if (_updateLabel() != null) actions.Add(new Room { Name = "Update", Version = Protocol.Version, Max = 4, Update = true });
            if (_partyLabel() != null) actions.Add(new Room { Name = "Mission party", Version = Protocol.Version, Max = 4, Party = true });
            if (_canHost()) actions.Add(new Room { Name = "Host your own game here", Version = Protocol.Version, Max = 4, HostHere = true });
            foreach (var section in new[] { MenuExtra.PlayTogether, MenuExtra.More })   // grouped under their headings
                for (int i = 0; i < Extras.Count; i++)
                {
                    if (Extras[i].Section != section) continue;
                    string label = null;
                    try { label = Extras[i].Label(); } catch (Exception e) { CoopScript.Log("menu row: " + e.Message); }
                    if (label != null) actions.Add(new Room { Name = label, Version = Protocol.Version, Max = 4, Extra = i });
                }
            _rooms = actions;
            _message = "Searching for games...";
            _selected = FirstIn(_tab);
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
                _message = note ?? (rooms.Count == 0 ? "No games found. R looks again." : null);
                _rooms = actions.Concat(rooms).ToList();
            }) { IsBackground = true, Name = "RDR2Coop.Browser" }.Start();
        }

        // True when the key was for the browser (the game shouldn't act on it).
        public bool OnKey(Keys key)
        {
            if (_mode != Mode.List) return _mode == Mode.Password || _mode == Mode.Search;
            var rooms = Shown();
            switch (key)
            {
                case Keys.Up: Step(-1); return true;
                case Keys.Down: Step(1); return true;
                case Keys.Left: case Keys.Q: SwitchTab(-1); return true;
                case Keys.Right: case Keys.E: case Keys.Tab: SwitchTab(1); return true;
                case Keys.R: Refresh(); return true;
                case Keys.F:
                    _mode = Mode.Search;
                    _keyboardSince = Environment.TickCount;
                    MISC.DISPLAY_ONSCREEN_KEYBOARD(KeyboardText, "", "", "", "", "", "", SearchChars + 1);   // + 1 like ScriptHookRDR2DotNet's GetUserInput
                    return true;
                case Keys.Escape: Close(); return true;
                case Keys.Enter:
                    if (rooms == null || _selected < 0 || _selected >= rooms.Count || SectionOf(rooms[_selected]) != Tabs[_tab]) return true;
                    var room = rooms[_selected];
                    if (room.Party) { Close(); _toggleParty(); return true; }
                    if (room.Extra >= 0) { Close(); if (room.Extra < Extras.Count) Extras[room.Extra].Act(); return true; }
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
            if (_mode == Mode.Search) { TickSearch(); return null; }
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

        // The search keyboard: what's typed becomes the filter (empty shows everything again).
        void TickSearch()
        {
            DrawLines(new List<(string, Color)>
            {
                ("Find a game by name", Color.FromArgb(255, 200, 40, 40)),
                ("Type part of its name, then Enter (empty shows all; Esc goes back)", Color.White),
            });
            int status = MISC.UPDATE_ONSCREEN_KEYBOARD();
            if (status == 1)
            {
                _filter = RoomList.CleanSearch(MISC.GET_ONSCREEN_KEYBOARD_RESULT(), SearchChars);
                _tab = Array.IndexOf(Tabs, Games);   // a search is for games
                _selected = FirstIn(_tab);
                _mode = Mode.List;
            }
            else if (status == 2) _mode = Mode.List;   // cancelled: the filter stays as it was
            else if (status == 3 && Environment.TickCount - _keyboardSince > 1000) { _message = "The game's keyboard didn't open."; _mode = Mode.List; }
        }

        List<Room> Shown() => RoomList.Filter(_rooms ?? new List<Room>(), _filter);

        // The menu's tabs, left to right; each row sits in the one SectionOf names.
        // (27 Oct: no "Play together" tab: the mod's own mini-games are gone from the menu; future ones are the game's own,
        // like its poker. Rows that still say PlayTogether, the partner actions, are listed under Session.)
        static readonly string[] Tabs = { Session, Games, MenuExtra.More };

        static string SectionOf(Room r) => r.Update || r.Party || r.HostHere ? Session
            : r.Extra >= 0 && r.Extra < Extras.Count ? (Extras[r.Extra].Section == MenuExtra.PlayTogether ? Session : Extras[r.Extra].Section) : Games;

        List<int> RowsIn(int tab)
        {
            var rooms = Shown();
            var rows = new List<int>();
            for (int i = 0; i < rooms.Count; i++) if (SectionOf(rooms[i]) == Tabs[tab]) rows.Add(i);
            return rows;
        }

        int FirstIn(int tab) { var rows = RowsIn(tab); return rows.Count > 0 ? rows[0] : -1; }

        void Step(int by)
        {
            var rows = RowsIn(_tab);
            if (rows.Count == 0) { _selected = -1; return; }
            int at = rows.IndexOf(_selected);
            _selected = rows[at < 0 ? 0 : (at + by + rows.Count) % rows.Count];
        }

        void SwitchTab(int by)
        {
            _tab = (_tab + by + Tabs.Length) % Tabs.Length;
            _selected = FirstIn(_tab);
        }

        // Layout on the 1280x720 grid, in the game's own pause-menu style: a framed panel on its ink texture, the game's
        // title and body fonts, tabs along the top, the list on the left, what the chosen row does on the right, and
        // the keys along the bottom. Colours: near-black panel, cream text, dimmer grey for the secondary, Rockstar red.
        const float X0 = 110f, Y0 = 56f, W = 1060f, H = 608f, Pad = 30f, RowH = 30f, ListW = 560f;
        const int MaxRows = 12;
        static readonly Color Cream = Color.FromArgb(255, 237, 233, 223), Text = Color.FromArgb(255, 220, 216, 206),   // bone
                              Dim = Color.FromArgb(255, 148, 143, 134), Faint = Color.FromArgb(255, 104, 100, 94),     // ash
                              Red = Color.FromArgb(255, 176, 26, 24), Back = Color.FromArgb(238, 10, 9, 8),             // Rockstar red, ink
                              Ink = Color.FromArgb(248, 8, 7, 6), Glass = Color.FromArgb(140, 10, 9, 8), Card = Color.FromArgb(170, 26, 25, 23),
                              Chosen = Color.FromArgb(200, 58, 55, 50), KeyCap = Color.FromArgb(255, 214, 211, 204),   // parchment
                              KeyText = Color.FromArgb(255, 22, 21, 19), RuleC = Color.FromArgb(255, 79, 75, 69);       // smoke
        const string InkDict = "pausemenu_textures", InkTexture = "menu_ink_large1";

        // Rough text widths on the grid, per character at scale 1 (the game has no text measuring native we use): for right
        // alignment and the tab strip.
        const float BodyEm = 22f, TitleEm = 19f;
        static float Width(string text, float scale, float em) => (text ?? "").Length * em * scale;

        // A key cap (parchment, dark letters); returns its width.
        static float Cap(string key, float x, float y)
        {
            float s = key.Length == 1 ? 0.34f : 0.26f;   // a single letter reads better a little bigger
            float em = Visuals.GameFont ? BodyEm : BodyEm * 1.25f;   // the plain font's capitals are wider (ENTER ran off its cap)
            float w = Math.Max(26f, 12f + Width(key, s, em));
            Ui.Box(x, y, w, 24f, KeyCap);
            Ui.TextAt(key, x + (w - Width(key, s, em)) / 2f, y + (key.Length == 1 ? 0f : 2f), s, KeyText, Ui.TitleFont);
            return w;
        }

        // The frame every screen of the menu uses: the panel on its ink, the title, the status line.
        void Frame(float height, bool status)
        {
            // The pause menu's ink brush behind the panel gives it the game's rough edges; a plain box until it has loaded.
            bool ink = Ui.Sprite(InkDict, InkTexture, X0 - 24f, Y0 - 16f, W + 48f, height + 32f, Ink);
            Ui.Box(X0, Y0, W, height, ink ? Glass : Back);
            Ui.TextAt("RDR2 Co-op", X0 + Pad, Y0 + 14f, 0.80f, Cream, Ui.TitleFont);
            if (!status) return;
            string line = null;
            try { line = Status?.Invoke(); } catch (Exception e) { CoopScript.Log("menu status: " + e.Message); }
            if (line != null) Ui.TextAt(line, X0 + Pad, Y0 + 62f, 0.30f, Dim, Ui.BodyFont);
        }

        void Draw()
        {
            Ui.Centered(true);
            try { DrawList(); } finally { Ui.Centered(false); }
        }

        void DrawList()
        {
            var rooms = Shown();
            Frame(H, true);

            // The tab strip, as the game's own menus have it: top right, Q and E caps either side, the open tab bright
            // with a red line under it.
            float ty = Y0 + 24f, gap = 30f, total = 0f;
            foreach (var t in Tabs) total += Width(t, 0.40f, TitleEm) + gap;
            float tx = X0 + W - Pad - total - 2 * 30f;
            tx += Cap("Q", tx, ty + 4f) + 14f;
            for (int t = 0; t < Tabs.Length; t++)
            {
                bool on = t == _tab;
                float w = Width(Tabs[t], 0.40f, TitleEm);
                Ui.TextAt(Tabs[t], tx, ty, 0.40f, on ? Cream : Dim, Ui.TitleFont);
                if (on) Ui.Box(tx, ty + 31f, w, 3f, Red);
                tx += w + gap;
            }
            Cap("E", tx - gap + 14f, ty + 4f);
            float top = Y0 + 96f;
            Ui.Box(X0 + Pad, top, W - 2 * Pad, 1f, RuleC);

            // The list.
            var rows = RowsIn(_tab);
            float ly = top + 20f;
            var notes = new List<string>();
            if (Tabs[_tab] == Games)
            {
                if (_filter.Length > 0) notes.Add($"Showing names with \"{_filter}\". Press F to change it.");
                if (_message != null) notes.Add(_message);
            }
            if (rows.Count == 0 && notes.Count == 0) notes.Add(Tabs[_tab] == Session ? "Nothing to do here right now." : "Nothing here yet.");
            foreach (var n in notes) { Ui.TextAt(n, X0 + Pad + 14f, ly, 0.32f, Dim, Ui.BodyFont); ly += RowH; }
            int at = Math.Max(0, rows.IndexOf(_selected));
            int first = Math.Max(0, Math.Min(at - MaxRows / 2, rows.Count - MaxRows));
            int last = Math.Min(rows.Count, first + MaxRows);
            if (first > 0) Ui.TextAt("More above", X0 + Pad + ListW - 90f, ly - 22f, 0.26f, Faint, Ui.BodyFont);
            for (int k = first; k < last; k++) { Row(rooms[rows[k]], rows[k] == _selected, ly); ly += RowH; }
            if (last < rows.Count) Ui.TextAt("More below", X0 + Pad + ListW - 90f, ly, 0.26f, Faint, Ui.BodyFont);

            // What the chosen row does, beside the list.
            float dx = X0 + Pad + ListW + 34f, dy = top + 20f, dw = W - (dx - X0) - Pad;
            Ui.Box(dx - 18f, top + 12f, 1f, H - (top - Y0) - 80f, RuleC);
            if (_selected >= 0 && _selected < rooms.Count && SectionOf(rooms[_selected]) == Tabs[_tab])
            {
                var (title, body) = Details(rooms[_selected]);
                Ui.TextAt(title, dx, dy - 4f, 0.48f, Cream, Ui.TitleFont);
                float by = dy + 40f;
                foreach (var line in Ui.Wrap(body, 46)) { Ui.TextAt(line, dx, by, 0.32f, Text, Ui.BodyFont); by += 23f; }
            }

            // The keys, bottom right, label then cap, as in the game's menus; the version bottom left.
            float fy = Y0 + H - 46f;
            Ui.Box(X0 + Pad, fy - 14f, W - 2 * Pad, 1f, RuleC);
            Ui.TextAt(BuildInfo.Version, X0 + Pad, fy + 2f, 0.28f, Faint, Ui.BodyFont);
            var keys = new List<(string what, string key)> { ("Select", "ENTER") };
            if (Tabs[_tab] == Games) { keys.Add(("Find a game", "F")); keys.Add(("Look again", "R")); }
            keys.Add(("Close", _closeKey.ToString()));
            float fx = X0 + W - Pad;
            for (int i = keys.Count - 1; i >= 0; i--)
            {
                float cw = Math.Max(26f, 12f + Width(keys[i].key, keys[i].key.Length == 1 ? 0.34f : 0.26f, BodyEm));
                fx -= cw;
                Cap(keys[i].key, fx, fy);
                fx -= 8f + Width(keys[i].what, 0.30f, BodyEm);
                Ui.TextAt(keys[i].what, fx, fy + 1f, 0.30f, Dim, Ui.BodyFont);
                fx -= 26f;
            }
        }

        // The chosen row's title and what it does.
        // A row's name as a heading: before any ": state" or "(note)", at most 28 letters.
        static string Short(string name)
        {
            int cut = name.IndexOfAny(new[] { ':', '(' });
            string t = (cut > 0 ? name.Substring(0, cut) : name).Trim();
            return t.Length > 28 ? t.Substring(0, 26) + "..." : t;
        }

        (string title, string body) Details(Room r)
        {
            if (r.Update) return ("Update", "Downloads the new version (only ones signed by the mod) and keeps your settings. Press Insert afterwards to load it.");
            if (r.Party) return ("Mission party", "When you start a story mission, your partners are asked to join it and come along. Only the host can switch this. Enter switches it on or off.");
            if (r.HostHere) return ("Host here", "Start your own co-op game on this PC: your story, your world. Friends join you from their co-op menu (give them the password).");
            if (r.Extra >= 0 && r.Extra < Extras.Count)
            {
                string help = null;
                try { help = Extras[r.Extra].Help?.Invoke(); } catch (Exception e) { CoopScript.Log("menu help: " + e.Message); }
                return (Short(r.Name), help ?? r.Name);
            }
            string where = r.Lan ? "on your home network" : "online";
            string can = r.Version != Protocol.Version ? $"It runs another co-op version (protocol {r.Version}, yours {Protocol.Version}): you can't join it."
                       : r.Players >= r.Max ? "It's full right now."
                       : "Enter joins it (you'll be asked for the room's password).";
            return (r.Name.Length > 28 ? r.Name.Substring(0, 26) + "..." : r.Name, $"{r.Players} of {r.Max} players, {where}.\n{can}");
        }

        void Row(Room r, bool chosen, float y)
        {
            bool off = r.Extra < 0 && !r.Update && !r.Party && !r.HostHere && (r.Version != Protocol.Version || r.Players >= r.Max);
            float x = X0 + Pad;
            if (chosen) { Ui.Box(x, y - 4f, ListW, RowH - 2f, Chosen); Ui.Box(x, y - 4f, 3f, RowH - 2f, Red); }
            var c = off ? Faint : chosen ? Cream : Text;
            x += 14f;
            if (r.Update) Ui.TextAt(_updateLabel() ?? "Updated: press Insert to load it", x, y, 0.34f, chosen ? Cream : Red, Ui.BodyFont);
            else if (r.Party) Ui.TextAt(_partyLabel() ?? "Mission party (host only)", x, y, 0.34f, c, Ui.BodyFont);
            else if (r.HostHere) Ui.TextAt("Host your own game here", x, y, 0.34f, c, Ui.BodyFont);
            else if (r.Extra >= 0) Ui.TextAt(r.Name, x, y, 0.34f, c, Ui.BodyFont);
            else
            {
                Ui.TextAt(r.Name.Length > 34 ? r.Name.Substring(0, 32) + "..." : r.Name, x, y, 0.34f, c, Ui.BodyFont);
                Ui.TextAt($"{r.Players}/{r.Max}", X0 + Pad + ListW - 150f, y, 0.32f, c, Ui.BodyFont);
                Ui.TextAt(r.Version != Protocol.Version ? "other version" : r.Players >= r.Max ? "full" : r.Lan ? "home" : "online", X0 + Pad + ListW - 90f, y, 0.30f, Dim, Ui.BodyFont);
            }
        }

        // The password and search screens: the same frame, a title and a line or two.
        void DrawLines(List<(string text, Color color)> lines)
        {
            Ui.Centered(true);
            try
            {
                float h = 120f + lines.Count * 30f;
                Frame(h, false);
                for (int i = 0; i < lines.Count; i++)
                    Ui.TextAt(lines[i].text, X0 + Pad, Y0 + 70f + i * 32f, i == 0 ? 0.44f : 0.32f, i == 0 ? Cream : Text, i == 0 ? Ui.TitleFont : Ui.BodyFont);
            }
            finally { Ui.Centered(false); }
        }
    }
}
