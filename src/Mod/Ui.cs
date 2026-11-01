// On-screen text and boxes that don't flicker. The game draws script text when
// it renders the frame, reading the string then; ScriptHookRDR2DotNet's
// TextElement hands it a temporary copy that is already freed by then, so the
// text breaks up or vanishes on some frames. Here every string stays in
// unmanaged memory until it hasn't been drawn for a few seconds.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using RDR2.Native;

namespace RDR2Coop.Mod
{
    static class Ui
    {
        const ulong VarString = 0xFA925AC00EB830B9, BgDisplayText = 0x16794E044C9EFB58;
        const int KeepMs = 5000;

        static readonly Dictionary<string, (IntPtr ptr, int used)> Pinned = new Dictionary<string, (IntPtr, int)>();
        static int _lastSweep;

        static IntPtr Pin(string s)
        {
            int now = Environment.TickCount;
            if (Pinned.TryGetValue(s, out var e)) { Pinned[s] = (e.ptr, now); return e.ptr; }
            var bytes = Encoding.UTF8.GetBytes(s + "\0");
            var p = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            Pinned[s] = (p, now);
            if (now - _lastSweep > KeepMs)
            {
                _lastSweep = now;
                foreach (var old in Pinned.Where(kv => now - kv.Value.used > KeepMs).Select(kv => kv.Key).ToList())
                {
                    Marshal.FreeHGlobal(Pinned[old].ptr);
                    Pinned.Remove(old);
                }
            }
            return p;
        }

        // The game's own menu fonts (its pause menu's titles and body text), for Text's font.
        public const string TitleFont = "$title", BodyFont = "$body";

        // x, y: 0-1 screen fractions (top left of the text). font: one of the game's fonts, or null for the default.
        public static void Text(string text, float x, float y, float scale, Color c, string font = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!Visuals.GameFont) font = null;   // a translation mod replacing the game's fonts left every menu text blank (30 Oct)
            // ~ < > are the game's text markup: room and player names come from strangers.
            text = new string(text.Where(ch => ch != '~' && ch != '<' && ch != '>').ToArray());
            if (font != null) text = "<FONT FACE='" + font + "'>" + text + "</FONT>";   // markup we add after the strip
            UIDEBUG._BG_SET_TEXT_SCALE(scale, scale);
            UIDEBUG._BG_SET_TEXT_COLOR(c.R, c.G, c.B, c.A);
            ulong s = Function.Call<ulong>(VarString, 10, new InputArgument((ulong)Pin("LITERAL_STRING").ToInt64()),
                                           new InputArgument((ulong)Pin(text).ToInt64()));
            // A literal string is drawn as it is; the game reads markup (<FONT>) only in a colour string made from it.
            if (font != null) s = Function.Call<ulong>(VarString, 42, new InputArgument((ulong)Pin("COLOR_STRING").ToInt64()), 0, new InputArgument(s));
            Function.Call(BgDisplayText, new InputArgument(s), x, y);
        }

        // Inside Centered(), the 1280x720 grid is a 16:9 area in the middle of a wider screen (an ultrawide one stretched
        // the co-op menu across all of it); outside it, the grid is the whole screen as before.
        static float _k = 1f;
        public static void Centered(bool on) => _k = on ? Math.Min(1f, (16f / 9f) / Aspect()) : 1f;
        static float X(float x) => 0.5f + (x / 1280f - 0.5f) * _k;
        static float Wd(float w) => w / 1280f * _k;

        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out Rect r);
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        struct Rect { public int Left, Top, Right, Bottom; }
        static float _aspect = 16f / 9f;
        static int _aspectAt = int.MinValue;
        static bool _aspectSaid;

        // The game window's width / height (read from Windows every 2 s; the game's own natives report 1280x720).
        public static float Aspect()
        {
            int now = Environment.TickCount;
            if (_aspectSaid && now - _aspectAt < 2000) return _aspect;   // (_aspectAt starts at int.MinValue: now - it overflows)
            _aspectAt = now;
            try
            {
                // The game's window: the active one of this (the game's) thread, else the foreground one if it's ours.
                // Process.MainWindowHandle comes back empty under Proton.
                var h = GetActiveWindow();
                if (h == IntPtr.Zero)
                {
                    h = GetForegroundWindow();
                    if (h != IntPtr.Zero && (GetWindowThreadProcessId(h, out uint pid) == 0 || pid != (uint)System.Diagnostics.Process.GetCurrentProcess().Id)) h = IntPtr.Zero;
                }
                if (h == IntPtr.Zero) h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (h != IntPtr.Zero && GetClientRect(h, out var r) && r.Bottom > r.Top && r.Right > r.Left)
                {
                    float a = (r.Right - r.Left) / (float)(r.Bottom - r.Top);
                    if (Math.Abs(a - _aspect) > 0.01f) CoopScript.Log($"screen: {r.Right - r.Left}x{r.Bottom - r.Top} (the co-op menu keeps to the middle 16:9)");
                    _aspect = a;
                }
            }
            catch (Exception e) { if (!_aspectSaid) CoopScript.Log("screen: window size unknown: " + e.Message); }
            if (!_aspectSaid)
            {
                _aspectSaid = true;
                int nx = 0, ny = 0;
                unsafe { GRAPHICS.GET_SCREEN_RESOLUTION(&nx, &ny); }
                CoopScript.Log($"screen: active {GetActiveWindow()}, foreground {GetForegroundWindow()}, main {System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle}, game says {nx}x{ny}, aspect used {_aspect:0.00}");
            }
            return _aspect;
        }

        // A filled box on the 1280x720 layout grid: x, y its top left, in pixels.
        public static void Box(float x, float y, float w, float h, Color c) =>
            GRAPHICS.DRAW_RECT(X(x + w / 2f), (y + h / 2f) / 720f, Wd(w), h / 720f, c.R, c.G, c.B, c.A, true, false);

        // Text at a pixel position on the same grid.
        public static void TextAt(string text, float x, float y, float scale, Color c, string font = null) => Text(text, X(x), y / 720f, scale, c, font);

        // One of the game's textures over a box on the grid (false while its dictionary is still loading).
        public static bool Sprite(string dict, string name, float x, float y, float w, float h, Color c)
        {
            TXD.REQUEST_STREAMED_TEXTURE_DICT(dict, false);
            if (!TXD.HAS_STREAMED_TEXTURE_DICT_LOADED(dict)) return false;
            GRAPHICS.DRAW_SPRITE(dict, name, X(x + w / 2f), (y + h / 2f) / 720f, Wd(w), h / 720f, 0f, c.R, c.G, c.B, c.A, false);
            return true;
        }

        // Text cut into lines of at most `width` characters, at spaces.
        public static List<string> Wrap(string text, int width)
        {
            var lines = new List<string>();
            foreach (var para in (text ?? "").Split('\n'))
            {
                var line = new StringBuilder();
                foreach (var word in para.Split(' '))
                {
                    if (line.Length > 0 && line.Length + 1 + word.Length > width) { lines.Add(line.ToString()); line.Clear(); }
                    if (line.Length > 0) line.Append(' ');
                    line.Append(word);
                }
                lines.Add(line.ToString());
            }
            return lines;
        }

        // A panel of lines, laid out on a 1280x720 screen like the old ScaledDraw; x: where its text starts (the
        // box begins 12 px left of it), y: its first line. The top left (40, 60) unless given: the co-op menu and
        // enRage's lists; the party list sits top right, chat lower on the left (Chat.Top).
        public static void Panel(IList<(string text, Color color)> lines, float width = 640f, float x = 40f, float y = 60f)
        {
            const float line = 22f;
            float h = line * lines.Count + 20f;
            GRAPHICS.DRAW_RECT((x - 12f + width / 2f) / 1280f, (y - 10f + h / 2f) / 720f, width / 1280f, h / 720f, 0, 0, 0, 190, true, false);
            for (int i = 0; i < lines.Count; i++)
                Text(lines[i].text, x / 1280f, (y + i * line) / 720f, i == 0 ? 0.40f : 0.36f, lines[i].color);
        }
    }
}
