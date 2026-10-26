// A hunting contest (0.9.0, [Games] Hunting), a Contest of kind "hunting": the most points for animals killed in
// 3, 5 or 10 minutes, the bigger the animal the more (Points below, by model). params: "<minutes>".
// A kill is ours when the scoreboard's Kills credits it to us: our own shot in an area our game runs, or the area
// owner's "kill" credit when we hunt in a partner's world (src/Mod/Hunts.cs passes those on). Each hunter's game
// keeps its own kills, in order, and tells the host all of them, "kill <model> <model> ...", at once when there's a
// new one and again every 2 s until the standings show them (notices can be lost, so a report repeats the whole
// list). The host keeps a hunter's longest list, takes one only if it starts with what it had (a report only ever
// adds kills), only animals that score (no horses, dogs, cats, farm animals or people), 60 at most. It's over
// 2 s after the time (for the last reports on their way). Standings and results text, one token a hunter, most
// points first: "<id>:<points>:<kills>:<best model>" (best 0: nothing yet). The winner has the most points alone.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RDR2Coop.Net
{
    public sealed class Hunting : Contest
    {
        public const int EveryMs = 2000, GraceMs = 2000, MaxKills = 60;
        public static readonly int[] Lengths = { 3, 5, 10 };   // minutes

        // The animals that score, by their a_c_ model name (the game's own names): small game 1, foxes and birds of prey
        // 2, deer and wolves 3, elk and big cats 5, bears, bison and moose 8, the legendary models 15. Other legendary
        // animals are the ordinary model in another coat: they score as their kind does.
        static readonly (string model, int points, string name)[] Animals =
        {
            ("rabbit_01", 1, "rabbit"), ("squirrel_01", 1, "squirrel"), ("chipmunk_01", 1, "chipmunk"), ("rat_01", 1, "rat"), ("possum_01", 1, "opossum"),
            ("raccoon_01", 1, "raccoon"), ("skunk_01", 1, "skunk"), ("armadillo_01", 1, "armadillo"), ("muskrat_01", 1, "muskrat"), ("badger_01", 1, "badger"),
            ("bat_01", 1, "bat"), ("bluejay_01", 1, "blue jay"), ("cardinal_01", 1, "cardinal"), ("carolinaparakeet_01", 1, "parakeet"),
            ("cedarwaxwing_01", 1, "waxwing"), ("oriole_01", 1, "oriole"), ("robin_01", 1, "robin"), ("songbird_01", 1, "songbird"), ("sparrow_01", 1, "sparrow"),
            ("woodpecker_01", 1, "woodpecker"), ("woodpecker_02", 1, "woodpecker"), ("parrot_01", 1, "parrot"), ("crow_01", 1, "crow"), ("raven_01", 1, "raven"),
            ("pigeon", 1, "pigeon"), ("seagull_01", 1, "seagull"), ("duck_01", 1, "duck"), ("goosecanada_01", 1, "goose"), ("loon_01", 1, "loon"),
            ("cormorant_01", 1, "cormorant"), ("egret_01", 1, "egret"), ("heron_01", 1, "heron"), ("roseatespoonbill_01", 1, "spoonbill"),
            ("redfootedbooby_01", 1, "booby"), ("pheasant_01", 1, "pheasant"), ("prairiechicken_01", 1, "prairie chicken"), ("quail_01", 1, "quail"),
            ("iguana_01", 1, "iguana"), ("iguanadesert_01", 1, "desert iguana"), ("gilamonster_01", 1, "Gila monster"), ("frogbull_01", 1, "bullfrog"),
            ("toad_01", 1, "toad"), ("turtlesnapping_01", 1, "snapping turtle"), ("crab_01", 1, "crab"), ("crawfish_01", 1, "crayfish"),
            ("snake_01", 1, "rattlesnake"), ("snakeblacktailrattle_01", 1, "rattlesnake"), ("snakeferdelance_01", 1, "fer-de-lance"),
            ("snakeredboa_01", 1, "boa"), ("snakewater_01", 1, "water snake"),
            ("fox_01", 2, "fox"), ("coyote_01", 2, "coyote"), ("javelina_01", 2, "peccary"), ("beaver_01", 2, "beaver"), ("turkey_01", 2, "turkey"),
            ("turkey_02", 2, "turkey"), ("turkeywild_01", 2, "wild turkey"), ("eagle_01", 2, "eagle"), ("hawk_01", 2, "hawk"), ("owl_01", 2, "owl"),
            ("vulture_01", 2, "vulture"), ("californiacondor_01", 2, "condor"), ("pelican_01", 2, "pelican"), ("cranewhooping_01", 2, "whooping crane"),
            ("snakeredboa10ft_01", 2, "giant boa"), ("turtlesea_01", 2, "sea turtle"),
            ("deer_01", 3, "deer"), ("buck_01", 3, "buck"), ("pronghorn_01", 3, "pronghorn"), ("bighornram_01", 3, "bighorn ram"), ("boar_01", 3, "boar"),
            ("wolf", 3, "wolf"), ("wolf_medium", 3, "wolf"), ("wolf_small", 3, "wolf"), ("alligator_03", 3, "young alligator"),
            ("elk_01", 5, "elk"), ("cougar_01", 5, "cougar"), ("panther_01", 5, "panther"), ("alligator_01", 5, "alligator"), ("bearblack_01", 5, "black bear"),
            ("sharkhammerhead_01", 5, "hammerhead shark"), ("sharktiger", 5, "tiger shark"),
            ("bear_01", 8, "grizzly bear"), ("buffalo_01", 8, "bison"), ("moose_01", 8, "moose"),
            ("buffalo_tatanka_01", 15, "Tatanka bison"), ("boarlegendary_01", 15, "legendary boar"), ("alligator_02", 15, "Bull Gator"), ("lionmangy_01", 15, "lion"),
        };
        static readonly Dictionary<uint, (int points, string name)> ByModel = Animals.ToDictionary(a => Hash("a_c_" + a.model), a => (a.points, a.name));

        /// <summary>What a kill of this model scores: 0 for anything that isn't game (horses, dogs, cats, farm animals, fish, people).</summary>
        public static int Points(uint model) => ByModel.TryGetValue(model, out var a) ? a.points : 0;
        public static string AnimalName(uint model) => ByModel.TryGetValue(model, out var a) ? a.name : "animal";

        /// <summary>The game's model hash of a name (joaat: Jenkins one-at-a-time over the lower-case name).</summary>
        public static uint Hash(string name)
        {
            uint h = 0;
            foreach (char c in name.ToLowerInvariant()) { h += c; h += h << 10; h ^= h >> 6; }
            h += h << 3; h ^= h >> 11; h += h << 15;
            return h;
        }

        public override string Kind => "hunting";
        public int LimitMs { get; private set; }

        // The host's.
        List<byte> _players;
        readonly Dictionary<byte, List<uint>> _bags = new Dictionary<byte, List<uint>>();
        long _beginMs;
        bool _over;

        // Our own hunt.
        readonly List<uint> _mine = new List<uint>();
        long _startMs = -1, _sentMs;
        int _seen, _sent = -1;   // our kills the host's standings show / in our last report
        bool _rules, _ended;

        public static string Params(int minutes) => minutes.ToString(CultureInfo.InvariantCulture);

        public static bool TryParse(string prms, out int limitMs)
        {
            limitMs = 0;
            if (!int.TryParse(prms, NumberStyles.None, CultureInfo.InvariantCulture, out int m) || Array.IndexOf(Lengths, m) < 0) return false;
            limitMs = m * 60_000;
            return true;
        }

        public override void Begin(IList<byte> players, string prms, long now)
        {
            _players = new List<byte>(players);
            foreach (var p in _players) _bags[p] = new List<uint>();
            _beginMs = now;
            if (!TryParse(prms, out int limit)) _over = true;   // not one of ours: nothing to hunt for
            LimitMs = limit;
        }

        public override void OnReport(byte from, string data, long now)
        {
            if (_players == null || _over || !_players.Contains(from)) return;
            var p = (data ?? "").Split(' ');
            var bag = _bags[from];
            if (p[0] != "kill" || p.Length - 1 > MaxKills || p.Length - 1 <= bag.Count) return;   // a repeat, or an older report come late
            var kills = new List<uint>();
            for (int i = 1; i < p.Length; i++)
            {
                if (!uint.TryParse(p[i], NumberStyles.None, CultureInfo.InvariantCulture, out uint m) || Points(m) == 0) return;
                kills.Add(m);
            }
            for (int i = 0; i < bag.Count; i++) if (kills[i] != bag[i]) return;   // a report only ever adds kills
            _bags[from] = kills;
        }

        public override void Tick(long now) { if (_players != null && !_over && now - _beginMs >= LimitMs + GraceMs) _over = true; }
        public override bool Over => _over;
        public override void Left(byte player, long now) { _players?.Remove(player); _bags.Remove(player); }
        public override string Standings() => Text();
        public override string Results() => Text();

        // Most points first; ties keep the join order (OrderBy is stable).
        string Text() => _players == null ? "" : string.Join(" ", _players.OrderByDescending(id => Total(_bags[id]))
            .Select(id => $"{id}:{Total(_bags[id])}:{_bags[id].Count}:{Best(_bags[id])}"));

        static int Total(IEnumerable<uint> kills) => kills.Sum(Points);
        static uint Best(IList<uint> kills) => kills.Count == 0 ? 0 : kills.Aggregate((a, b) => Points(b) > Points(a) ? b : a);

        public override void Start(string prms, long now) { _startMs = now; _rules = TryParse(prms, out int limit); LimitMs = limit; }

        /// <summary>Started, not over, and with a time to hunt for.</summary>
        public bool Running => _startMs >= 0 && !_ended && _rules;
        public long LeftMs(long now) => Running ? Math.Max(0, LimitMs - (now - _startMs)) : 0;

        /// <summary>The game side: we killed this animal. Returns its points (0: it doesn't count: not game, out of time or not running).</summary>
        public int Killed(uint model, long now)
        {
            int points = Points(model);
            if (points == 0 || !Running || now - _startMs >= LimitMs || _mine.Count >= MaxKills) return 0;
            _mine.Add(model);
            return points;
        }

        public override string Report(long now)
        {
            if (!Running || _mine.Count <= _seen || (_mine.Count == _sent && now - _sentMs < EveryMs)) return null;
            _sent = _mine.Count; _sentMs = now;
            return "kill " + string.Join(" ", _mine);
        }

        public override void OnStandings(string text) { foreach (var r in Rows(text)) if (r.id == Me) _seen = r.kills; }

        public override IList<(string text, int tone)> Lines(string text, long now)
        {
            long left = LeftMs(now);
            var lines = new List<(string, int)> { (_ended ? "Hunt over" : $"Hunting: {left / 60000}:{left / 1000 % 60:00} left", 3) };
            var rows = Rows(text);
            if (_ended)
            {
                byte w = Winner(text);
                var best = rows.Where(r => Points(r.best) > 0).OrderByDescending(r => Points(r.best)).FirstOrDefault();
                lines.Add(w != 0 ? ($"Winner: {NameOf(w)}", 4) : rows.Any(r => r.points > 0) ? ("A draw", 2) : ("Nobody got anything", 2));
                if (best.id != 0) lines.Add(($"Biggest: {NameOf(best.id)}'s {AnimalName(best.best)} ({Pts(Points(best.best))})", 0));
            }
            int rank = 0;
            foreach (var r in rows)
            {
                bool mine = r.id == Me;
                int points = r.points, kills = r.kills;
                if (mine && !_ended && _mine.Count > kills) { points = Total(_mine); kills = _mine.Count; }   // our own row: live, not the host's 2 s old copy
                lines.Add(($"{++rank}. {NameOf(r.id)}  {Pts(points)}, {kills} animal{(kills == 1 ? "" : "s")}", mine ? 1 : 0));
            }
            return lines;
        }

        public override void End(string results) => _ended = true;

        /// <summary>The hunter with the most points, alone: 0 for a draw or when nobody got anything.</summary>
        public override byte Winner(string results)
        {
            var rows = Rows(results);
            int top = rows.Count == 0 ? 0 : rows.Max(r => r.points);
            return top > 0 && rows.Count(r => r.points == top) == 1 ? rows.First(r => r.points == top).id : (byte)0;
        }

        static string Pts(int n) => n == 1 ? "1 point" : n + " points";

        static List<(byte id, int points, int kills, uint best)> Rows(string text)
        {
            var rows = new List<(byte, int, int, uint)>();
            foreach (var tok in (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = tok.Split(':');
                if (p.Length == 4 && byte.TryParse(p[0], out byte id) && id != 0 && int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out int pts)
                    && pts <= 15 * MaxKills && int.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n <= MaxKills
                    && uint.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint best)) rows.Add((id, pts, n, best));
            }
            return rows;
        }
    }
}
