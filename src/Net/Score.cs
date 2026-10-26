// The scoreboard's wire format and rules (0.9.0), pure so the tests can run them.
// NoticeKind.Score texts, first token the verb:
//   kill <human|animal> <model>   one line per kill (\n between, 20 at most), from the game that runs the
//                                 area (its owner, the only one that sees the real death) to the player who killed
//   stats <key=n ...>             Stats.Write of the sender's counters, broadcast every 5 s when they changed
using System;
using System.Collections.Generic;
using System.Linq;

namespace RDR2Coop.Net
{
    public static class Score
    {
        public const int MaxKills = 20;   // kills credited by one notice (a stick of dynamite's worth)

        public static string KillLine(bool human, uint model) => "kill " + (human ? "human" : "animal") + " " + model;

        /// <summary>The kills in a notice; a line that isn't one is dropped, 20 at most.</summary>
        public static List<(bool human, uint model)> ParseKills(string text)
        {
            var kills = new List<(bool, uint)>();
            foreach (var line in (text ?? "").Split(new[] { '\n' }))
            {
                var p = line.Split(new[] { ' ' });
                if (p.Length != 3 || p[0] != "kill" || (p[1] != "human" && p[1] != "animal") || !uint.TryParse(p[2], out uint model)) continue;
                kills.Add((p[1] == "human", model));
                if (kills.Count == MaxKills) break;
            }
            return kills;
        }

        public static string StatsLine(IDictionary<string, int> stats) => "stats " + Stats.Write(stats);

        /// <summary>A "stats" notice's counters, or null for any other text.</summary>
        public static Dictionary<string, int> ParseStats(string text) =>
            text != null && text.StartsWith("stats ", StringComparison.Ordinal) ? Stats.Parse(text.Substring(6)) : null;

        /// <summary>Only the game that runs the area we're in may credit us with kills; our own we count ourselves.</summary>
        public static bool MayCredit(byte from, byte owner, byte me) => owner != 0 && from == owner && from != me;

        /// <summary>The panel: a title, a header and one line per player, ours first. Tones: 3 title, 2 header, 1 you, 0 others.</summary>
        public static List<(string text, int tone)> Lines(IEnumerable<(byte id, string name)> players, byte me, Func<byte, IDictionary<string, int>> statsOf)
        {
            var lines = new List<(string, int)> { ("Scoreboard", 3), ("kills people/animals | down | revived | won/lost | wins race/duel/shoot/hunt", 2) };
            foreach (var p in players.OrderBy(p => p.id != me))
            {
                var s = statsOf(p.id);
                string name = (p.name ?? "?").Length > 16 ? p.name.Substring(0, 16) : p.name ?? "?";
                lines.Add((s == null ? name + ": no stats yet" :
                    $"{name}: {N(s, "kills.human")}/{N(s, "kills.animal")} | {N(s, "downs")} | {N(s, "revives")} | {Dollars(N(s, "money.won"))}/{Dollars(N(s, "money.lost"))}" +
                    $" | {N(s, "race.wins")}/{N(s, "duel.wins")}/{N(s, "shoot.wins")}/{N(s, "hunt.wins")}", p.id == me ? 1 : 0));
            }
            return lines;
        }

        static int N(IDictionary<string, int> s, string key) => s.TryGetValue(key, out int v) ? v : 0;
        static string Dollars(int cents) => "$" + cents / 100 + "." + (cents % 100).ToString("00");
    }
}
