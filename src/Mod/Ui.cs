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

        // x, y: 0-1 screen fractions (top left of the text).
        public static void Text(string text, float x, float y, float scale, Color c)
        {
            if (string.IsNullOrEmpty(text)) return;
            // ~ < > are the game's text markup: room and player names come from strangers.
            text = new string(text.Where(ch => ch != '~' && ch != '<' && ch != '>').ToArray());
            UIDEBUG._BG_SET_TEXT_SCALE(scale, scale);
            UIDEBUG._BG_SET_TEXT_COLOR(c.R, c.G, c.B, c.A);
            ulong s = Function.Call<ulong>(VarString, 10, new InputArgument((ulong)Pin("LITERAL_STRING").ToInt64()),
                                           new InputArgument((ulong)Pin(text).ToInt64()));
            Function.Call(BgDisplayText, new InputArgument(s), x, y);
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
