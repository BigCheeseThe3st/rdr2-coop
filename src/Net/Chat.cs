// Text chat's rules (0.8.0, [Convenience] Chat; the game side is src/Mod/Chat.cs): what a message
// may hold, how it wraps, which lines are on screen, and how much one partner may say. A message
// travels as NoticeKind.Chat with only its text; receivers put the sender's roster name in front
// ("name: text"), so nobody can write under someone else's name.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RDR2Coop.Net
{
    public sealed class ChatLog
    {
        public const int MaxChars = 200;   // per message
        public const int ShowMs = 10000;   // each message stays on screen this long
        public const int Width = 70;       // characters per line on the panel
        public const int MaxLines = 4;     // the newest lines kept, and shown
        const int PerSecond = 3;           // from one partner (a game sends 1 a second): more is a flood, dropped

        readonly List<(string text, long until)> _lines = new List<(string, long)>();
        readonly Dictionary<byte, (long start, int count)> _heard = new Dictionary<byte, (long, int)>();

        // Puts a message on screen for ShowMs; returns it as shown ("name: text"), or null when nothing is left of it.
        public string Add(string name, string text, long now)
        {
            text = Clean(text);
            if (text.Length == 0) return null;
            string who = Clean(name);
            string line = (who.Length > 0 ? who : "?") + ": " + text;
            foreach (var l in Wrap(line, Width)) _lines.Add((l, now + ShowMs));
            if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
            return line;
        }

        // The lines to draw, oldest first: those still showing, or every kept one (while typing a reply).
        public List<string> Visible(long now, bool all) => _lines.Where(l => all || l.until > now).Select(l => l.text).ToList();

        // False once a partner has said PerSecond things within a second: a flood can't fill the log or stall the game.
        public bool Allow(byte from, long now)
        {
            if (!_heard.TryGetValue(from, out var h) || now - h.start >= 1000) h = (now, 0);
            _heard[from] = (h.start, h.count + 1);
            return h.count < PerSecond;
        }

        public void Clear() { _lines.Clear(); _heard.Clear(); }

        // What may be drawn: no ~codes~ (the game's colour and button markup), control characters or invisible
        // formatting characters; line breaks and runs of spaces become one space; at most MaxChars.
        public static string Clean(string s)
        {
            var b = new StringBuilder();
            foreach (char c in Regex.Replace(s ?? "", "~[^~]*~", ""))
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) { if (b.Length > 0 && b[b.Length - 1] != ' ') b.Append(' '); }
                else if (c != '~' && char.GetUnicodeCategory(c) != UnicodeCategory.Format) b.Append(c);
            }
            string t = b.ToString().Trim();
            if (t.Length <= MaxChars) return t;
            return t.Substring(0, char.IsHighSurrogate(t[MaxChars - 1]) ? MaxChars - 1 : MaxChars).TrimEnd();   // never half an emoji
        }

        // Lines of at most `width` characters, broken at a space where there is one.
        public static List<string> Wrap(string s, int width)
        {
            var lines = new List<string>();
            while (s.Length > width)
            {
                int cut = s.LastIndexOf(' ', width);
                if (cut <= 0) cut = char.IsHighSurrogate(s[width - 1]) ? width - 1 : width;   // one long word: cut it
                lines.Add(s.Substring(0, cut));
                s = s.Substring(cut).TrimStart();
            }
            lines.Add(s);
            return lines;
        }
    }
}
