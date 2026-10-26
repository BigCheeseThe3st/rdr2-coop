// Dominoes (0.9.0): All Fives at a mod table, 2 to 4 players ([Games] Dominoes). A double-six set: 7 tiles each for
// two players, 5 each for three or four, the rest the boneyard. One hand of the tables framework is a whole game, first
// to 100 points: the winner takes the stakes from every other player (the settlement adds up to zero). Its rounds deal
// themselves inside it, 5 s apart (the last round's result shows meanwhile).
// The rules: the round's first player (at random in the first round, then whoever won the last) lays any tile; after
// that a tile must match an open end. The first double laid is the spinner: once tiles lie on both its sides, its top
// and bottom take tiles too (four ends). After each play the open ends add up (a double at an end counts both halves,
// and so does the spinner until both its sides are taken): a multiple of 5 scores that many. Nothing to play: you draw
// from the boneyard until you can; it's empty: you pass. A round ends when someone plays their last tile (they score
// the others' pips, rounded to the nearest 5) or nobody can play (the fewest pips scores the difference to each of the
// others, rounded; a tie scores nothing). The game ends the moment someone has 100. A seat that leaves is out: its
// tiles go with it, it can't win, it pays its stake to the winner (its game waits for that pay while the table's host
// is heard, up to TableRun.SettleWaitMs); the last one left wins. The framework's 45 s turn timer plays DefaultAction:
// a random legal play, else draw, else pass. The panel is the framework's (top right, under the party list).
//
// Public: "<stakes> <turn> <bone> <winner> <move> <seats> <line> <spin> <up> <down> <note>"
//   turn: the seat to move (-1 between rounds and once it's over); bone: tiles in the boneyard; winner: the seat that
//   won, or -1; move: counts every change (a seat's priv carries it, so a view never offers tiles from an old hand);
//   seats: "<id>:<score>:<tiles>,..." (tiles -1: left the game); line: the row left to right, each tile its two pips
//   in the order they lie ("63.33.31"), "-" when empty; spin: the spinner's index in the line, or -1; up, down: the
//   spinner's arms from it outward, the same way, or "-"; note: what just happened, "-" or items "<what><seat>.<n>"
//   joined by "/": s scored n with a play, o went out (+n), b won a blocked round (+n), t a blocked round tied,
//   d drew n tiles, p passed, l left the game.
// Private: "<move> <tiles>" (tiles as in the line, "-" for none).
// Actions: "play <tile> <end>" (tile "63" either way round; end L, R, U (the spinner's top) or D (its bottom); a
// round's first tile takes L), "draw" (until you can play or the boneyard's empty), "pass" (nothing to play or draw).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RDR2Coop.Net
{
    // The tiles on the table: the line, left to right, each tile (a, b) as it lies (a its left pip, b its right), and
    // the spinner's arms, outward (a touches the spinner, b is the open pip).
    public sealed class DominoBoard
    {
        public readonly List<(int a, int b)> Line = new List<(int, int)>(), Up = new List<(int, int)>(), Down = new List<(int, int)>();
        public int Spin = -1;

        public bool Empty => Line.Count == 0;
        public bool ArmsOpen => Spin > 0 && Spin < Line.Count - 1;   // tiles on both sides of the spinner
        public int Tiles => Line.Count + Up.Count + Down.Count;

        /// <summary>The open ends: (L R U D, the pip a tile there must match). None on an empty board (any tile leads).</summary>
        public List<(char end, int pip)> Ends()
        {
            var e = new List<(char, int)>();
            if (Empty) return e;
            e.Add(('L', Line[0].a));
            e.Add(('R', Line[Line.Count - 1].b));
            if (ArmsOpen) { int s = Line[Spin].a; e.Add(('U', Up.Count == 0 ? s : Up[Up.Count - 1].b)); e.Add(('D', Down.Count == 0 ? s : Down[Down.Count - 1].b)); }
            return e;
        }

        public bool Can((int a, int b) t, char end) => Empty ? end == 'L' : Ends().Any(e => e.end == end && (t.a == e.pip || t.b == e.pip));

        /// <summary>Every legal play of these tiles now, in their order.</summary>
        public List<((int a, int b) tile, char end)> Plays(IEnumerable<(int a, int b)> hand)
        {
            var p = new List<((int, int), char)>();
            var ends = Ends();
            foreach (var t in hand)
                if (Empty) p.Add((t, 'L'));
                else foreach (var e in ends) if (t.a == e.pip || t.b == e.pip) p.Add((t, e.end));
            return p;
        }

        /// <summary>Lays a tile where Can said yes, the matching pip against the end; the first double is the spinner.</summary>
        public void Place((int a, int b) t, char end)
        {
            bool dbl = t.a == t.b;
            if (Empty) { Line.Add(t); if (dbl) Spin = 0; return; }
            int pip = Ends().First(e => e.end == end).pip, other = t.a == pip ? t.b : t.a;
            switch (end)
            {
                case 'L': Line.Insert(0, (other, pip)); if (Spin >= 0) Spin++; else if (dbl) Spin = 0; break;
                case 'R': Line.Add((pip, other)); if (Spin < 0 && dbl) Spin = Line.Count - 1; break;
                case 'U': Up.Add((pip, other)); break;
                default: Down.Add((pip, other)); break;
            }
        }

        /// <summary>The open ends added up: a play scores this when it's a multiple of 5.</summary>
        public int Count()
        {
            if (Empty) return 0;
            if (Line.Count == 1) return Line[0].a + Line[0].b;
            int n = End(Line[0], Line[0].a) + End(Line[Line.Count - 1], Line[Line.Count - 1].b);
            if (Up.Count > 0) n += End(Up[Up.Count - 1], Up[Up.Count - 1].b);
            if (Down.Count > 0) n += End(Down[Down.Count - 1], Down[Down.Count - 1].b);
            return n;
        }

        static int End((int a, int b) t, int open) => t.a == t.b ? 2 * open : open;   // a double lies across: both halves count

        public DominoBoard Copy() { var c = new DominoBoard { Spin = Spin }; c.Line.AddRange(Line); c.Up.AddRange(Up); c.Down.AddRange(Down); return c; }
    }

    public sealed class Dominoes : TableGame
    {
        public const int Target = 100, RoundPauseMs = 5000;

        public override string Name => "dominoes";

        // ---- the rules (host) ----
        List<byte> _ids = new List<byte>();
        List<(int a, int b)>[] _hands = new List<(int, int)>[0];
        List<(int a, int b)> _bone = new List<(int, int)>();
        DominoBoard _board = new DominoBoard();
        int[] _score = new int[0];
        bool[] _out = new bool[0];
        int _stakes, _turn = -1, _winner = -1, _lead, _passes, _move;
        bool _pause;
        long _pauseMs = -1;
        Random _rng = new Random();
        readonly List<string> _note = new List<string>();

        internal int[] Scores => _score;   // the tests set a score near 100

        public override void Begin(IList<byte> seats, int stakes, IList<int> cash, Random rng)
        {
            _ids = seats.ToList();
            _stakes = stakes;
            _rng = rng ?? new Random();
            _score = new int[_ids.Count];
            _out = new bool[_ids.Count];
            _winner = -1;
            NewRound(_rng.Next(_ids.Count));
        }

        int Active => _out.Count(o => !o);

        // A fresh shuffle for the seats still in.
        void NewRound(int lead)
        {
            var set = new List<(int a, int b)>();
            for (int a = 0; a <= 6; a++) for (int b = 0; b <= a; b++) set.Add((a, b));   // the higher pip first, as the hand shows them
            for (int i = set.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); var t = set[i]; set[i] = set[j]; set[j] = t; }
            int each = Active == 2 ? 7 : 5, k = 0;
            var hands = new List<(int a, int b)>[_ids.Count];
            for (int i = 0; i < hands.Length; i++) { hands[i] = new List<(int, int)>(); if (!_out[i]) { hands[i].AddRange(set.GetRange(k, each)); k += each; } }
            Deal(hands, set.GetRange(k, set.Count - k), lead);
        }

        /// <summary>A round with these tiles (the tests rig it): the lead seat, or the next one still in, plays first.</summary>
        internal void Deal(List<(int a, int b)>[] hands, List<(int a, int b)> bone, int lead)
        {
            _hands = hands; _bone = bone; _board = new DominoBoard();
            _passes = 0; _pause = false; _pauseMs = -1; _move++; _turn = -1;
            _note.Clear();
            for (int k = 0; k < _ids.Count && _turn < 0; k++) { int s = (lead + k) % _ids.Count; if (!_out[s]) _turn = s; }
        }

        public override int Turn => _turn;
        public override bool HandOver => _winner >= 0;

        public override bool Act(int seat, string action)
        {
            if (HandOver || _pause || seat != _turn || action == null) return false;
            var hand = _hands[seat];
            bool stuck = _board.Plays(hand).Count == 0;
            if (action == "draw")
            {
                if (!stuck || _bone.Count == 0) return false;
                int n = 0;
                for (; _bone.Count > 0 && stuck; n++) { hand.Add(_bone[_bone.Count - 1]); _bone.RemoveAt(_bone.Count - 1); stuck = _board.Plays(hand).Count == 0; }
                Note($"d{seat}.{n}");
                return true;   // still our turn
            }
            if (action == "pass")
            {
                if (!stuck || _bone.Count > 0) return false;
                Note($"p{seat}.0");
                if (++_passes >= Active) Blocked(); else Next();
                return true;
            }
            if (!ParsePlay(action, out var tile, out char end)) return false;
            int at = hand.FindIndex(t => t.a == tile.a && t.b == tile.b || t.a == tile.b && t.b == tile.a);
            if (at < 0 || !_board.Can(hand[at], end)) return false;
            _board.Place(hand[at], end);
            hand.RemoveAt(at);
            _passes = 0;
            Note(null);
            int pts = _board.Count();
            if (pts > 0 && pts % 5 == 0) { _note.Add($"s{seat}.{pts}"); if (Score(seat, pts)) return true; }
            if (hand.Count == 0)
            {
                int got = Round5(Enumerable.Range(0, _ids.Count).Where(i => i != seat).Sum(i => Pips(_hands[i])));
                _note.Add($"o{seat}.{got}");
                if (!Score(seat, got)) EndRound(seat);
                return true;
            }
            Next();
            return true;
        }

        void Note(string item) { _note.Clear(); if (item != null) _note.Add(item); _move++; }

        // True when that wins the game.
        bool Score(int seat, int pts)
        {
            _score[seat] += pts;
            if (_score[seat] < Target) return false;
            _winner = seat; _turn = -1;
            return true;
        }

        // Nobody can play: the fewest pips scores the difference to each of the others; a tie scores nothing.
        void Blocked()
        {
            var live = Enumerable.Range(0, _ids.Count).Where(i => !_out[i]).ToList();
            int low = live.Min(i => Pips(_hands[i]));
            var lows = live.Where(i => Pips(_hands[i]) == low).ToList();
            if (lows.Count > 1) { _note.Add($"t{lows[0]}.0"); EndRound(lows[0]); return; }
            int w = lows[0], got = Round5(live.Where(i => i != w).Sum(i => Pips(_hands[i]) - low));
            _note.Add($"b{w}.{got}");
            if (!Score(w, got)) EndRound(w);
        }

        void EndRound(int lead) { _lead = lead; _pause = true; _pauseMs = -1; _turn = -1; }

        void Next()
        {
            for (int k = 1; k <= _ids.Count; k++) { int s = (_turn + k) % _ids.Count; if (!_out[s]) { _turn = s; return; } }
        }

        public override void Tick(long now)
        {
            if (_pause && !HandOver)
            {
                if (_pauseMs < 0) _pauseMs = now;
                else if (now - _pauseMs >= RoundPauseMs) NewRound(_lead);
            }
            base.Tick(now);
        }

        public override void Forfeit(int seat)
        {
            if (HandOver || seat < 0 || seat >= _ids.Count || _out[seat]) return;   // once it's decided, it stays decided
            _out[seat] = true;
            _hands[seat].Clear();   // its tiles leave with it
            Note($"l{seat}.0");
            _passes = 0;
            if (Active == 1) { _winner = Array.IndexOf(_out, false); _turn = -1; return; }
            if (_turn == seat) Next();
        }

        public override string DefaultAction(int seat)
        {
            var plays = seat >= 0 && seat < _hands.Length ? _board.Plays(_hands[seat]) : new List<((int a, int b) tile, char end)>();
            if (plays.Count > 0) { var p = plays[_rng.Next(plays.Count)]; return Play(p.tile, p.end); }
            return _bone.Count > 0 ? "draw" : "pass";
        }

        public override string Public() =>
            $"{_stakes} {_turn} {_bone.Count} {_winner} {_move} "
            + string.Join(",", _ids.Select((id, i) => $"{id}:{_score[i]}:{(_out[i] ? -1 : _hands[i].Count)}"))
            + $" {Tiles(_board.Line)} {_board.Spin} {Tiles(_board.Up)} {Tiles(_board.Down)} {(_note.Count == 0 ? "-" : string.Join("/", _note))}";

        public override string Private(int seat) => seat >= 0 && seat < _hands.Length ? $"{_move} {Tiles(_hands[seat])}" : null;

        public override IList<int> Settlement()
        {
            var net = new int[_ids.Count];
            if (_winner < 0) return net;
            for (int i = 0; i < net.Length; i++) if (i != _winner) { net[i] = -_stakes; net[_winner] += _stakes; }
            return net;
        }

        // ---- the view (every seat) ----
        int _vStakes, _vTurn = -1, _vBone, _vWinner = -1, _vMove = -1, _mineMove = -2;
        List<(byte id, int score, int tiles)> _vSeats = new List<(byte, int, int)>();
        DominoBoard _vBoard = new DominoBoard();
        List<(char what, int seat, int n)> _vNote = new List<(char, int, int)>();
        List<(int a, int b)> _mine = new List<(int, int)>();

        public override void OnPublic(string state)
        {
            var t = (state ?? "").Split(new[] { ' ' });
            if (t.Length != 11 || !Num(t[0], 1, TableMsg.MaxCents, out int stakes) || !Num(t[1], -1, 3, out int turn) || !Num(t[2], 0, 28, out int bone)
                || !Num(t[3], -1, 3, out int winner) || !Num(t[4], 0, int.MaxValue, out int move) || !Num(t[7], -1, 27, out int spin)) return;
            var seats = new List<(byte, int, int)>();
            foreach (var s in t[5].Split(new[] { ',' }))
            {
                var p = s.Split(new[] { ':' });
                if (p.Length != 3 || seats.Count == TableMsg.MaxSeats || !byte.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte id) || id == 0
                    || id == Protocol.Broadcast || seats.Any(x => x.Item1 == id) || !Num(p[1], 0, 999, out int score) || !Num(p[2], -1, 28, out int tiles)) return;
                seats.Add((id, score, tiles));
            }
            var b = new DominoBoard { Spin = spin };
            if (seats.Count < 2 || turn >= seats.Count || winner >= seats.Count || !ParseTiles(t[6], b.Line) || !ParseTiles(t[8], b.Up) || !ParseTiles(t[9], b.Down)
                || spin >= b.Line.Count || spin >= 0 && b.Line[spin].a != b.Line[spin].b || b.Up.Count + b.Down.Count > 0 && !b.ArmsOpen || b.Tiles > 28) return;
            var note = new List<(char, int, int)>();
            if (t[10] != "-")
                foreach (var n in t[10].Split(new[] { '/' }))
                {
                    var q = n.Length >= 4 ? n.Substring(1).Split(new[] { '.' }) : new string[0];
                    if (note.Count == 3 || q.Length != 2 || "sobtdpl".IndexOf(n[0]) < 0 || !Num(q[0], 0, seats.Count - 1, out int who) || !Num(q[1], 0, 999, out int pts)) return;
                    note.Add((n[0], who, pts));
                }
            _vStakes = stakes; _vTurn = turn; _vBone = bone; _vWinner = winner; _vMove = move; _vSeats = seats; _vBoard = b; _vNote = note;
        }

        public override void OnPrivate(string secret)
        {
            var t = (secret ?? "").Split(new[] { ' ' });
            var mine = new List<(int a, int b)>();
            if (t.Length != 2 || !Num(t[0], 0, int.MaxValue, out int move) || !ParseTiles(t[1], mine)) return;
            _mine = mine; _mineMove = move;
        }

        int MySeat(byte me) => _vSeats.FindIndex(s => s.id == me);

        public override IList<(string label, string action)> Options(byte me)
        {
            var o = new List<(string, string)>();
            if (_vTurn < 0 || _vWinner >= 0 || MySeat(me) != _vTurn || _mineMove != _vMove) return o;   // not ours, or our tiles not in yet
            foreach (var (tile, end) in _vBoard.Plays(_mine))
            {
                int pts = Gain(Play(tile, end)), pip = _vBoard.Empty ? 0 : _vBoard.Ends().First(e => e.end == end).pip;
                string where = end == 'L' ? "on the left" : end == 'R' ? "on the right" : end == 'U' ? "up from the spinner" : "down from the spinner";
                o.Add(((_vBoard.Empty ? $"Lead with {Show(tile)}" : $"Play {Show(tile)} {where} (end {pip})") + (pts > 0 ? $": scores {pts}" : ""), Play(tile, end)));
            }
            if (o.Count == 0) o.Add(_vBone > 0 ? ($"Draw from the boneyard ({_vBone} left)", "draw") : ("Pass: nothing to play or draw", "pass"));
            return o;
        }

        /// <summary>What that play would score on the board we see (0: nothing, or not a legal play). For the bots.</summary>
        public int Gain(string action)
        {
            if (!ParsePlay(action, out var tile, out char end) || !_vBoard.Can(tile, end)) return 0;
            var b = _vBoard.Copy();
            b.Place(tile, end);
            int pts = b.Count();
            return pts > 0 && pts % 5 == 0 ? pts : 0;
        }

        /// <summary>The pips on the tile a play lays, -1 for draw or pass. For the bots.</summary>
        public static int TilePips(string action) => ParsePlay(action, out var t, out _) ? t.a + t.b : -1;

        public override IList<(string text, int tone)> Lines(byte me, Func<byte, string> nameOf)
        {
            var l = new List<(string, int)>();
            if (_vSeats.Count == 0) return l;
            int mine = MySeat(me);
            string Who(int i, bool start) => _vSeats[i].id == me ? (start ? "You" : "you") : nameOf(_vSeats[i].id);
            l.Add(("To 100: " + string.Join(", ", _vSeats.Select((s, i) => $"{Who(i, false)} {s.score}{(s.tiles < 0 ? " (left)" : "")}")), 0));
            l.Add(("Board: " + (_vBoard.Empty ? "empty, the first tile leads" : Row(_vBoard)), 0));
            if (_vBoard.ArmsOpen) l.Add(($"Spinner up: {Arm(_vBoard.Up)}, down: {Arm(_vBoard.Down)}", 0));
            if (mine >= 0 && _vSeats[mine].tiles >= 0)
            {
                var tiles = _mine.OrderByDescending(x => Math.Max(x.a, x.b)).ThenByDescending(x => Math.Min(x.a, x.b)).Select(x => Show(x)).ToList();
                l.Add(("Yours: " + (tiles.Count == 0 ? "none" : string.Concat(tiles.Take(10))), 1));
                if (tiles.Count > 10) l.Add(("       " + string.Concat(tiles.Skip(10).Take(10)) + (tiles.Count > 20 ? $" +{tiles.Count - 20}" : ""), 1));
            }
            else if (mine < 0) l.Add(("You play from the next game", 2));
            var others = _vSeats.Select((s, i) => (s, i)).Where(x => x.i != mine && x.s.tiles >= 0).Select(x => $"{Who(x.i, false)} {x.s.tiles}");
            l.Add(($"Ends add to {_vBoard.Count()}, boneyard {_vBone}; tiles: {string.Join(", ", others)}", 2));
            if (_vNote.Count > 0) l.Add((string.Join("; ", _vNote.Select(n => Said(n, Who))), _vNote.Any(n => "sob".IndexOf(n.what) >= 0) ? 4 : 2));
            string stakes = TableMsg.Dollars(_vStakes);
            if (_vWinner >= 0) l.Add(($"{Who(_vWinner, true)} {(_vSeats[_vWinner].id == me ? "win" : "wins")} the game and {stakes} from each player", 4));
            else if (_vTurn < 0) l.Add(("The next round deals in a moment", 2));
            else if (_vTurn == mine) l.Add(("Your turn", 1));
            else l.Add(($"{Who(_vTurn, true)} to play", 0));
            return l;
        }

        static string Said((char what, int seat, int n) e, Func<int, bool, string> who)
        {
            string w = who(e.seat, true);
            switch (e.what)
            {
                case 's': return $"{w} scored {e.n}";
                case 'o': return $"{w} went out: +{e.n}";
                case 'b': return $"Nobody can play: {who(e.seat, false)} had the fewest pips, +{e.n}";
                case 't': return "Nobody can play and the fewest pips tie: no score";
                case 'd': return $"{w} drew {e.n} tile{(e.n == 1 ? "" : "s")}";
                case 'p': return $"{w} passed";
                default: return $"{w} left the game";
            }
        }

        // The line, long ones cut to their ends and the spinner's neighbours (a panel line holds about 60 characters).
        static string Row(DominoBoard b)
        {
            int n = b.Line.Count, last = -1;
            var s = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (n > 9 && i >= 3 && i < n - 3 && (b.Spin < 0 || Math.Abs(i - b.Spin) > 1)) continue;
                if (last >= 0 && i > last + 1) s.Append("...");
                s.Append(Show(b.Line[i], i == b.Spin));
                last = i;
            }
            return s.ToString();
        }

        static string Arm(List<(int a, int b)> arm) =>
            arm.Count == 0 ? "none" : (arm.Count > 3 ? "..." : "") + string.Concat(arm.Skip(Math.Max(0, arm.Count - 3)).Select(t => Show(t)));

        // ---- the tiles in text ----
        static string Show((int a, int b) t, bool spinner = false) => $"[{t.a}|{t.b}{(spinner ? "*" : "")}]";
        static string Play((int a, int b) t, char end) => $"play {t.a}{t.b} {end}";
        static string Tiles(List<(int a, int b)> l) => l.Count == 0 ? "-" : string.Join(".", l.Select(t => $"{t.a}{t.b}"));
        static int Pips(List<(int a, int b)> l) => l.Sum(t => t.a + t.b);
        static int Round5(int x) => (x + 2) / 5 * 5;

        static bool ParseTile(string s, out (int a, int b) t)
        {
            t = default;
            if (s == null || s.Length != 2 || s[0] < '0' || s[0] > '6' || s[1] < '0' || s[1] > '6') return false;
            t = (s[0] - '0', s[1] - '0');
            return true;
        }

        static bool ParseTiles(string s, List<(int a, int b)> into)
        {
            if (s == "-") return true;
            var p = (s ?? "").Split(new[] { '.' });
            if (p.Length > 28) return false;
            foreach (var x in p) { if (!ParseTile(x, out var t)) return false; into.Add(t); }
            return true;
        }

        static bool ParsePlay(string action, out (int a, int b) tile, out char end)
        {
            tile = default; end = ' ';
            var t = (action ?? "").Split(new[] { ' ' });
            if (t.Length != 3 || t[0] != "play" || !ParseTile(t[1], out tile) || t[2].Length != 1 || "LRUD".IndexOf(t[2][0]) < 0) return false;
            end = t[2][0];
            return true;
        }

        static bool Num(string s, int min, int max, out int v) =>
            int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v) && v >= min && v <= max;
    }
}
