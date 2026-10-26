// A pick-list for features (0.9.0): a title and rows, Up/Down to pick, Enter to choose, Esc to
// close, drawn like the co-op menu (top left) with the game's controls off while it's open, so a
// poker player choosing "Raise" doesn't also draw a gun. One at a time: opening another replaces
// it. Ask() is the game's own keyboard for a number or a short text (an amount, a stake), whose
// answer arrives on a later tick through the callback (null = cancelled or the keyboard didn't open).
// CoopScript ticks it and gives it the keys first; features never need to touch the key handler.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using RDR2.Native;
using Keys = System.Windows.Forms.Keys;

namespace RDR2Coop.Mod
{
    sealed class PickList
    {
        const int Rows = 10;
        const int KeyboardText = 0, KeyboardNumeric = 3;   // KTEXTTYPE_DEFAULT, KTEXTTYPE_NUMERIC
        static readonly Color Title = Color.FromArgb(255, 200, 40, 40);

        public static PickList Current { get; private set; }
        public static bool IsOpen => Current != null || _ask != null;

        readonly string _title, _hint;
        readonly IList<(string label, Action act)> _rows;
        readonly Action _cancel;
        int _selected;

        PickList(string title, IList<(string, Action)> rows, Action cancel, string hint) { _title = title; _rows = rows; _cancel = cancel; _hint = hint; }

        /// <summary>Open a list. Each row's action runs after the list closes; onCancel on Esc. The label can carry ~codes~: they're stripped.</summary>
        public static void Open(string title, IList<(string label, Action act)> rows, Action onCancel = null, string hint = null)
        {
            if (rows == null || rows.Count == 0) return;
            Current = new PickList(title, rows, onCancel, hint);
        }

        public static void Close() { Current = null; if (_ask != null) { MISC.CANCEL_ONSCREEN_KEYBOARD(); _ask = null; } }

        /// <summary>True when the key was ours (the game and the other features shouldn't act on it).</summary>
        public static bool OnKey(Keys key)
        {
            if (_ask != null) return true;   // typing into the game's keyboard
            var p = Current;
            if (p == null) return false;
            switch (key)
            {
                case Keys.Up: p._selected = (p._selected + p._rows.Count - 1) % p._rows.Count; return true;
                case Keys.Down: p._selected = (p._selected + 1) % p._rows.Count; return true;
                case Keys.Escape: Current = null; try { p._cancel?.Invoke(); } catch (Exception e) { CoopScript.Log("pick-list cancel: " + e.Message); } return true;
                case Keys.Enter:
                    Current = null;
                    try { p._rows[p._selected].act?.Invoke(); } catch (Exception e) { CoopScript.Log("pick-list: " + e.Message); }
                    return true;
            }
            return true;   // every other key waits while a list is open
        }

        /// <summary>Every tick while something is open: draws the list, or minds the keyboard.</summary>
        public static void Tick()
        {
            if (_ask != null) { TickAsk(); return; }
            var p = Current;
            if (p == null) return;
            PAD.DISABLE_ALL_CONTROL_ACTIONS(0);
            int first = Math.Max(0, Math.Min(p._selected - Rows / 2, p._rows.Count - Rows));
            var lines = new List<(string, Color)> { (p._title + "   (Up/Down pick, Enter choose, Esc close)", Title) };
            for (int i = first; i < Math.Min(p._rows.Count, first + Rows); i++)
                lines.Add(((i == p._selected ? "> " : "   ") + p._rows[i].label, i == p._selected ? Color.Gold : Color.White));
            if (p._hint != null) lines.Add((p._hint, Color.LightGray));
            Ui.Panel(lines);
        }

        // ---- a number or short text from the game's keyboard ----
        sealed class Ask { public string Title, Hint; public Action<string> Done; public long Since; public bool Shown; }
        static Ask _ask;

        /// <summary>Ask for a number (digits only) or a short text. done(null) when cancelled or the keyboard never opened.</summary>
        public static void AskFor(string title, string hint, bool numeric, int maxChars, Action<string> done)
        {
            if (done == null) return;
            if (MISC.UPDATE_ONSCREEN_KEYBOARD() == 0) { done(null); return; }   // another keyboard is open
            Current = null;
            MISC.DISPLAY_ONSCREEN_KEYBOARD(numeric ? KeyboardNumeric : KeyboardText, "", "", "", "", "", "", Math.Max(1, maxChars) + 1);   // + 1 like ScriptHookRDR2DotNet's GetUserInput
            _ask = new Ask { Title = title, Hint = hint, Done = done, Since = Environment.TickCount };
        }

        static void TickAsk()
        {
            var a = _ask;
            Ui.Panel(new List<(string, Color)> { (a.Title, Title), (a.Hint ?? "Type it, then Enter (Esc goes back)", Color.White) });
            int status = MISC.UPDATE_ONSCREEN_KEYBOARD();
            if (status == 0) { a.Shown = true; return; }
            string result = null;
            if (status == 1) result = MISC.GET_ONSCREEN_KEYBOARD_RESULT() ?? "";
            else if (status != 2 && Environment.TickCount - a.Since <= 1000) return;   // 3 for a moment: still opening
            if (status != 1 && status != 2) CoopScript.Log("pick-list: the game's keyboard didn't open");
            _ask = null;
            try { a.Done(result); } catch (Exception e) { CoopScript.Log("pick-list answer: " + e.Message); }
        }

        /// <summary>Dollars typed as "12" or "12.50" -> cents, or -1 when it isn't an amount.</summary>
        public static int Cents(string text)
        {
            var t = (text ?? "").Trim().TrimStart('$');
            var m = System.Text.RegularExpressions.Regex.Match(t, @"^(\d{1,6})(?:\.(\d{1,2}))?$");
            if (!m.Success) return -1;
            return int.Parse(m.Groups[1].Value) * 100 + (m.Groups[2].Success ? int.Parse(m.Groups[2].Value.PadRight(2, '0')) : 0);
        }
    }
}
