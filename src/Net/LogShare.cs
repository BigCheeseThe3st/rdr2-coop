// Sharing logs from beta builds with the author (the author, 25 Oct: "have peoples logs autosend if they use
// beta versions so we don't have to wait for bugs"). Opt-in: a beta asks once in game ([Testing] SendLogs=
// empty), the answer is kept in the ini, and only a Yes ever sends anything. Before a log leaves the PC
// addresses, server names and user folders are blanked (Scrub); player names stay, they're what a report is
// matched against. Pure: what to ask, what to send and how it's cleaned; the game side is Mod/LogSender.cs.
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace RDR2Coop.Net
{
    public static class LogShare
    {
        public const int MaxBytes = 1_000_000;          // the tail of a log that goes up (the store caps at 1 MB)
        public const int EveryMs = 10 * 60_000;         // a running session's log goes up this often
        public const int AskForMs = 90_000;             // how long the question stays on screen

        public enum Consent { Unasked, Yes, No }

        /// <summary>Only test and beta builds (a dash in the version) ask or send.</summary>
        public static bool IsBeta(string version) => (version ?? "").Contains("-");

        public static Consent Parse(string setting)
        {
            string s = (setting ?? "").Trim();
            if (s.StartsWith("y", StringComparison.OrdinalIgnoreCase)) return Consent.Yes;
            if (s.StartsWith("n", StringComparison.OrdinalIgnoreCase)) return Consent.No;
            return Consent.Unasked;
        }

        public const string Question = "Beta: send your co-op logs to the developer automatically, to help fix bugs? Y = yes, N = no. Addresses and folders are removed first.";

        static readonly Regex Ipv4 = new Regex(@"\b\d{1,3}(\.\d{1,3}){3}\b");
        static readonly Regex Ipv6 = new Regex(@"(?<![\w:])(?:[0-9a-fA-F]{0,4}:){2,7}[0-9a-fA-F]{0,4}(?![\w:])");   // then checked: "::" or 5+ colons, so 14:50:08 stays
        static readonly Regex ServerName = new Regex(@"(\b(?:calling|joining|joined|server|Server|at)\s+)(?!\[)[A-Za-z0-9._-]+\.[A-Za-z]{2,}(?=:\d+)");   // host names before a port
        static readonly Regex WinUser = new Regex(@"([A-Za-z]:\\[Uu]sers\\)[^\\\r\n]+");
        static readonly Regex HomeDir = new Regex(@"(/home/|/Users/)[^/\r\n]+");

        /// <summary>The log text with addresses, server names and user folders blanked.</summary>
        public static string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = Ipv4.Replace(text, "[ip]");
            text = Ipv6.Replace(text, m => m.Value.Contains("::") || m.Value.Split(':').Length > 5 ? "[ip]" : m.Value);
            text = ServerName.Replace(text, "$1[server]");
            text = WinUser.Replace(text, "$1[user]");
            text = HomeDir.Replace(text, "$1[user]");
            return text;
        }

        /// <summary>The last MaxBytes of a log, cut at a line start.</summary>
        public static string Tail(string text)
        {
            if (text == null) return "";
            if (Encoding.UTF8.GetByteCount(text) <= MaxBytes) return text;
            int from = text.Length - MaxBytes / 2;   // UTF-8 can be up to 2 bytes per char here (names); plain ASCII mostly
            int nl = text.IndexOf('\n', Math.Max(0, from));
            return "[earlier lines cut]\n" + text.Substring(nl < 0 ? Math.Max(0, from) : nl + 1);
        }

        /// <summary>An install id the log store accepts (hex, 8-36): anything else (empty, typed by hand) gets a new one.</summary>
        public static bool ValidInstall(string id) => Regex.IsMatch(id ?? "", "^[0-9a-fA-F-]{8,36}$");

        public static string NewInstall() => Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>Whether a session upload is due now (the first one soon after the start, then every EveryMs).</summary>
        public static bool Due(long now, long lastSentMs, long startedMs) =>
            lastSentMs < 0 ? now - startedMs > 60_000 : now - lastSentMs > EveryMs;
    }
}
