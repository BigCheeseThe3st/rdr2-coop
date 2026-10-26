// Session stats (0.9.0): plain counters any feature adds to ("kills.animal", "race.wins",
// "money.won"...), kept for this session and shared by the scoreboard. Pure: no game calls.
using System;
using System.Collections.Generic;
using System.Linq;

namespace RDR2Coop.Net
{
    public static class Stats
    {
        static readonly Dictionary<string, int> _n = new Dictionary<string, int>(StringComparer.Ordinal);

        public static void Add(string key, int n = 1)
        {
            if (string.IsNullOrEmpty(key) || n == 0) return;
            lock (_n) _n[key] = Get(key) + n;
        }

        public static int Get(string key) { lock (_n) return _n.TryGetValue(key ?? "", out int v) ? v : 0; }

        public static Dictionary<string, int> All() { lock (_n) return new Dictionary<string, int>(_n); }

        public static void Clear() { lock (_n) _n.Clear(); }

        /// <summary>"key=n key=n ...": what goes to partners (keys are letters, digits and dots; counts clamped).</summary>
        public static string Write(IDictionary<string, int> stats) =>
            string.Join(" ", stats.Where(kv => Valid(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                  .Select(kv => kv.Key + "=" + Math.Max(0, Math.Min(999_999, kv.Value))));

        /// <summary>The other way: bad parts are dropped, at most 64 counters.</summary>
        public static Dictionary<string, int> Parse(string text)
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var part in (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0 || d.Count >= 64) continue;
                string k = part.Substring(0, eq);
                if (!Valid(k) || !int.TryParse(part.Substring(eq + 1), out int v) || v < 0 || v > 999_999) continue;
                d[k] = v;
            }
            return d;
        }

        static bool Valid(string k) => k.Length >= 1 && k.Length <= 24 && k.All(c => char.IsLetterOrDigit(c) || c == '.');
    }
}
