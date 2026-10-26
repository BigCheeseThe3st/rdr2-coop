// A horse race (0.9.0, [Games] Race), a Contest of kind "race": first to the finish, the host's map waypoint,
// any way you like or on horseback only. params: "<x> <y> <z> <h|->".
// Each racer's own game watches its distance to the finish and tells the host "done <elapsedMs>" at once when
// it's within 15 m (on a horse when required; only after it has been outside the zone, so standing at the
// finish at GO is no finish), again every 2 s until the standings show it; the rest of the time "dist <m>"
// every 2 s. The host ranks by the order the done reports ARRIVE (the claimed times are only shown) and takes
// a done only from a racer it last saw within 45 m of the finish (PosOf). It's over when everyone's in, a
// minute after the first finisher, or after 10 minutes (the rest didn't finish). Standings and results
// text, one token a player: "f<id>:<ms>" finished (in order), "r<id>:<m>" still racing (-1: no distance
// yet), "d<id>" didn't finish. The game side (the finish blip and marker, where we are) is src/Mod/Races.cs.
using System;
using System.Collections.Generic;
using System.Linq;

namespace RDR2Coop.Net
{
    public sealed class Race : Contest
    {
        public const float FinishM = 15f;
        public const int TimeLimitMs = 600_000, GraceMs = 60_000, EveryMs = 2000, MaxDistM = 99_999;

        public override string Kind => "race";
        public Vec3 Finish;
        public bool HorseOnly;

        // The host's.
        List<byte> _players;
        readonly List<byte> _order = new List<byte>();   // finished, in the order their reports came
        readonly Dictionary<byte, int> _claimed = new Dictionary<byte, int>(), _dist = new Dictionary<byte, int>();
        long _beginMs, _firstDoneMs;
        bool _over;

        // Our own race.
        long _startMs = -1, _doneMs = -1, _sentMs = -EveryMs;
        float _myDist = -1f;
        bool _course, _armed, _seen, _ended;

        public static string Params(Vec3 finish, bool horseOnly) => WaypointNotice.Encode(finish) + (horseOnly ? " h" : " -");

        public static bool TryParse(string prms, out Vec3 finish, out bool horseOnly)
        {
            finish = default(Vec3);
            horseOnly = false;
            if (!WaypointNotice.TryParse(prms, out var at) || at == null) return false;
            finish = at.Value;
            var f = prms.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            horseOnly = f.Length > 3 && f[3] == "h";
            return true;
        }

        public override void Begin(IList<byte> players, string prms, long now) { _players = new List<byte>(players); _beginMs = now; TryParse(prms, out Finish, out HorseOnly); }

        public override void OnReport(byte from, string data, long now)
        {
            if (_players == null || !_players.Contains(from) || _order.Contains(from)) return;
            var p = (data ?? "").Split(' ');
            if (p.Length != 2 || !int.TryParse(p[1], out int n) || n < 0) return;
            if (p[0] == "done")
            {
                var at = PosOf(from);   // a partner's done must come from near the finish, as we last saw them (a 2 s old position at a gallop)
                if (from != Me && (at == null || Ground(at.Value, Finish) > 3 * FinishM)) return;
                if (_order.Count == 0) _firstDoneMs = now;
                _order.Add(from); _claimed[from] = Math.Min(n, TimeLimitMs);
            }
            else if (p[0] == "dist") _dist[from] = Math.Min(n, MaxDistM);
        }

        public override void Tick(long now)
        {
            if (_players != null && !_over) _over = _players.All(_order.Contains) || now - _beginMs >= TimeLimitMs || (_order.Count > 0 && now - _firstDoneMs >= GraceMs);
        }
        public override bool Over => _over;
        public override void Left(byte player, long now) { if (_players != null && !_order.Contains(player)) _players.Remove(player); }
        public override string Standings() => Text(false);
        public override string Results() => Text(true);

        string Text(bool final) => _players == null ? "" : string.Join(" ", _order.Select(id => $"f{id}:{_claimed[id]}")
            .Concat(_players.Where(id => !_order.Contains(id)).Select(id => final ? "d" + id : $"r{id}:{(_dist.TryGetValue(id, out int m) ? m : -1)}")));

        public override void Start(string prms, long now) { _startMs = now; _course = TryParse(prms, out Finish, out HorseOnly); }

        /// <summary>Started, not over, and with a finish to ride to.</summary>
        public bool Running => _startMs >= 0 && !_ended && _course;
        public bool Finished => _doneMs >= 0;

        /// <summary>The game side, every 200 ms while we race: where we are and whether we're on a horse.</summary>
        public void Observe(Vec3 me, bool onHorse, long now)
        {
            if (!Running || Finished) return;
            _myDist = Ground(me, Finish);
            if (_myDist > FinishM) _armed = true;   // the finish counts only once we've been outside its zone
            else if (_armed && (onHorse || !HorseOnly)) { _doneMs = Math.Max(1, now - _startMs); _sentMs = -EveryMs; }   // done goes at once, not behind the 2 s dist gate
        }

        /// <summary>Distance on the ground, not the height: a waypoint's z is only approximate.</summary>
        static float Ground(Vec3 a, Vec3 b) { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }

        public override string Report(long now)
        {
            if (!Running || _seen || now - _sentMs < EveryMs) return null;
            if (Finished) { _sentMs = now; return "done " + _doneMs; }
            if (_myDist < 0) return null;
            _sentMs = now;
            return "dist " + (int)_myDist;
        }

        public override void OnStandings(string text)
        {
            foreach (var tok in Tokens(text)) if (Parse(tok, out char k, out byte id, out _) && k == 'f' && id == Me) _seen = true;
        }

        public override IList<(string text, int tone)> Lines(string text, long now)
        {
            var lines = new List<(string, int)> { (_ended ? "Race over" : HorseOnly ? "Race (on horseback)" : "Race", 3) };
            string winner = null;
            int rank = 0;
            foreach (var tok in Tokens(text))
            {
                if (!Parse(tok, out char k, out byte id, out int n)) continue;
                string name = NameOf(id);
                bool mine = id == Me;
                if (k == 'f') { lines.Add(($"{++rank}. {name}  {Time(n)}", mine ? 1 : 0)); if (winner == null) winner = name; }
                else if (k == 'r')
                {
                    if (mine && _myDist >= 0) n = (int)_myDist;   // our own row: live, not the host's 2 s old copy
                    lines.Add(($"   {name}  racing{(n >= 0 ? $", {n} m to go" : "")}", mine ? 1 : 0));
                }
                else lines.Add(($"   {name}  did not finish", mine ? 1 : 2));
            }
            if (_ended) lines.Insert(1, winner != null ? ($"Winner: {winner}", 4) : ("Nobody finished", 2));
            return lines;
        }

        public override void End(string results) => _ended = true;

        public override byte Winner(string results)
        {
            var first = Tokens(results).FirstOrDefault();
            return first != null && Parse(first, out char k, out byte id, out _) && k == 'f' ? id : (byte)0;
        }

        static string[] Tokens(string text) => (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        static bool Parse(string tok, out char kind, out byte id, out int n)
        {
            kind = tok[0]; id = 0; n = -1;
            if (kind != 'f' && kind != 'r' && kind != 'd') return false;
            var p = tok.Substring(1).Split(':');
            return byte.TryParse(p[0], out id) && (p.Length == 1 || int.TryParse(p[1], out n));
        }

        static string Time(int ms) => ms < 0 ? "?" : $"{ms / 60000}:{ms / 1000 % 60:00}.{ms % 1000 / 100}";
    }
}
