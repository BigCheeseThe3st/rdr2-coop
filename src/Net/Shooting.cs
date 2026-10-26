// A shooting contest (0.9.0, [Games] Shooting), a Contest of kind "shooting": who breaks the most bottles in the
// time. params: "<x> <y> <z> <heading> <seed> <count> <seconds>": where the host stood and faced at setup, a seed,
// the bottles (10 or 20) and the seconds (30 or 60). At GO every player's own game stands the same bottles on the
// ground (Spots: from the seed, so they're in the same places for everyone) in a row 12-18 m ahead of that spot,
// 8 m across. Each player shoots their own: a partner's stand-in shoots at nothing you can see (by design). Each
// game counts its own broken bottles (src/Mod/Shootings.cs, every 200 ms, into Observe) and tells the host
// "score <n>" when it rises, again every 2 s until the standings show it. The host can't see a partner's
// bottles, so a score is taken on trust, but only upward and never above the count; it ranks by score, ties by
// whose score ARRIVED first, and it's over at the time or once everyone has broken every bottle. Standings and
// results, one token a player, best first: "<id>:<n>". The winner is the first one, if they broke any.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RDR2Coop.Net
{
    public sealed class Shooting : Contest
    {
        public const float AheadM = 12f, DeepM = 6f, AcrossM = 8f;
        public const int MaxCount = 20, MinSeconds = 10, MaxSeconds = 120, EveryMs = 2000;
        public static readonly int[] Counts = { 10, 20 }, Times = { 30, 60 };   // what the setup offers

        public override string Kind => "shooting";
        public Vec3 At;
        public float Heading;
        public int Seed, Count, Seconds;

        // The host's.
        List<byte> _players;
        readonly Dictionary<byte, int> _score = new Dictionary<byte, int>();
        readonly Dictionary<byte, long> _reachedMs = new Dictionary<byte, long>();   // when their score last rose
        long _beginMs;
        bool _over;

        // Our own.
        long _startMs = -1, _sentMs;
        int _mine, _sent = -1, _shown;
        bool _ok, _ended;

        public static string Params(Vec3 at, float heading, int seed, int count, int seconds) =>
            WaypointNotice.Encode(at) + string.Format(CultureInfo.InvariantCulture, " {0:0.#} {1} {2} {3}", heading, seed, count, seconds);

        public static bool TryParse(string prms, out Vec3 at, out float heading, out int seed, out int count, out int seconds)
        {
            at = default(Vec3);
            heading = 0f;
            seed = count = seconds = 0;
            if (!WaypointNotice.TryParse(prms, out var where) || where == null) return false;
            var f = prms.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length != 7 || !float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out heading) || !(Math.Abs(heading) <= 720f)
                || !int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)
                || !int.TryParse(f[5], NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1 || count > MaxCount
                || !int.TryParse(f[6], NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds < MinSeconds || seconds > MaxSeconds) return false;
            at = where.Value;   // NaN fails the range checks, the heading's above and WaypointNotice's
            return true;
        }

        /// <summary>Where the bottles stand (z: the host's; the game puts them on the ground): the same for everyone.</summary>
        public static Vec3[] Spots(Vec3 at, float heading, int seed, int count)
        {
            double r = heading * Math.PI / 180.0;
            float fx = (float)-Math.Sin(r), fy = (float)Math.Cos(r);   // the way the host faced (the game's heading: 0 is north, counter-clockwise)
            uint s = (uint)seed;
            var spots = new Vec3[Math.Max(0, Math.Min(count, MaxCount))];
            for (int i = 0; i < spots.Length; i++)
            {
                s = s * 1664525u + 1013904223u;   // our own generator: System.Random's isn't promised to match between runtimes
                float ahead = AheadM + DeepM * (s >> 8) / 16777216f, side = AcrossM * ((i + 0.5f) / spots.Length - 0.5f);
                spots[i] = new Vec3(at.X + fx * ahead + fy * side, at.Y + fy * ahead - fx * side, at.Z);
            }
            return spots;
        }

        public Vec3[] Spots() => Spots(At, Heading, Seed, Count);

        public override void Begin(IList<byte> players, string prms, long now) { _players = new List<byte>(players); _beginMs = now; TryParse(prms, out At, out Heading, out Seed, out Count, out Seconds); }

        public override void OnReport(byte from, string data, long now)
        {
            if (_players == null || !_players.Contains(from)) return;
            var p = (data ?? "").Split(' ');
            if (p.Length != 2 || p[0] != "score" || !int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return;
            n = Math.Min(n, Count);
            if (n <= Score(from)) return;   // a repeat (or one that came late) keeps the time it first got there
            _score[from] = n;
            _reachedMs[from] = now;
        }

        int Score(byte id) => _score.TryGetValue(id, out int n) ? n : 0;

        public override void Tick(long now)
        {
            if (_players != null && !_over) _over = now - _beginMs >= Seconds * 1000L || _players.All(id => Score(id) >= Count);
        }
        public override bool Over => _over;
        public override void Left(byte player, long now) { _players?.Remove(player); _score.Remove(player); }
        public override string Standings() => Text();
        public override string Results() => Text();

        string Text() => _players == null ? "" : string.Join(" ", _players
            .OrderByDescending(Score).ThenBy(id => _reachedMs.TryGetValue(id, out long t) ? t : long.MaxValue)   // stable: then in the order they came in
            .Select(id => $"{id}:{Score(id)}"));

        public override void Start(string prms, long now) { _startMs = now; _ok = TryParse(prms, out At, out Heading, out Seed, out Count, out Seconds); }

        /// <summary>Started, not over, and with bottles to shoot.</summary>
        public bool Running => _startMs >= 0 && !_ended && _ok;

        /// <summary>The game side, every 200 ms while we shoot: how many of our bottles are broken.</summary>
        public void Observe(int broken, long now)
        {
            if (Running) _mine = Math.Max(_mine, Math.Min(broken, Count));
        }

        public override string Report(long now)
        {
            if (!Running || _mine <= _shown || (_mine == _sent && now - _sentMs < EveryMs)) return null;
            _sent = _mine;
            _sentMs = now;
            return "score " + _mine;
        }

        public override void OnStandings(string text)
        {
            foreach (var tok in Tokens(text)) if (Parse(tok, out byte id, out int n) && id == Me) _shown = n;
        }

        public override IList<(string text, int tone)> Lines(string text, long now)
        {
            int left = _startMs < 0 ? Seconds : (int)Math.Max(0, (Seconds * 1000L - (now - _startMs) + 999) / 1000);
            var lines = new List<(string, int)> { (_ended ? "Shooting over" : $"Shooting: {Count} bottles, {left} s left", 3) };
            if (_ended)
            {
                byte w = Winner(text);
                lines.Add(w != 0 ? ($"Winner: {NameOf(w)}", 4) : ("Nobody broke a bottle", 2));
            }
            else lines.Add(("Shoot your bottles; partners shoot theirs", 2));
            int rank = 0;
            foreach (var tok in Tokens(text))
            {
                if (!Parse(tok, out byte id, out int n)) continue;
                bool mine = id == Me;
                if (mine && !_ended) n = Math.Max(n, _mine);   // our own row: live, not the host's 2 s old copy
                lines.Add(($"{++rank}. {NameOf(id)}  {n}/{Count}", mine ? 1 : 0));
            }
            return lines;
        }

        public override void End(string results) => _ended = true;

        public override byte Winner(string results)
        {
            var first = Tokens(results).FirstOrDefault();
            return first != null && Parse(first, out byte id, out int n) && n > 0 ? id : (byte)0;
        }

        static string[] Tokens(string text) => (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        static bool Parse(string tok, out byte id, out int n)
        {
            id = 0; n = 0;
            var p = tok.Split(':');
            return p.Length == 2 && byte.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0
                   && int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out n) && n <= MaxCount;
        }
    }
}
