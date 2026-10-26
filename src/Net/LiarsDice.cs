// Liar's dice (0.9.0), the Red Dead Redemption 1 classic, at a mod table ([Games] Dice=Yes): 2 to 4 players, five
// dice each, rolled by the table's host; your dice go to you alone (a priv), how many each has left is public.
// In turn each bids a quantity and a face ("4 fives": at least four dice on the table show a five); a bid must raise
// the quantity, or keep it and raise the face (1 is the lowest face). Ones are wild, counting for any face, except
// when ones are bid. Instead of raising, the next player calls "Liar!" (the bid is false: the bidder loses a die,
// else the caller does) or "Spot on" (exactly right: everyone else loses a die, else the caller does). Everyone's
// dice are shown, fresh ones rolled, and the round's loser starts the next (the caller after a right "Spot on"; the
// next player along when the loser is out of dice). The last player with dice wins: everyone put the stakes in, the
// winner takes the pot. One framework "hand" is one whole game. A player who leaves loses their dice and the round
// starts again with fresh dice. 45 s a move, or the table bids the lowest raise (or calls Liar! when there is none).
// Bids on ones stay legal but are never offered (nor made by the timer): with no halving rule they're always the
// worst bid at their quantity.
// The panel is the tables framework's (top right, under the party list).
// Public: "<stakes> <turn> <round> <bid> <seats> <last>": turn the seat to move (-1 once it's over); round counts up
// across games (a priv for another round is stale); bid "-" or "<q>,<face>,<bidder seat>"; seats "<id>:<dice left>"
// ("-": left the table); last "-" or the last call, shown: "<L|S>,<caller>,<bidder>,<q>,<face>,<d/d/...>" with each
// seat's dice as digits in seat order. Private: "<round> <my dice as digits, low to high>".
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RDR2Coop.Net
{
    public sealed class LiarsDice : TableGame
    {
        public const int Dice = 5;
        static readonly string[] One = { "", "one", "two", "three", "four", "five", "six" };
        static readonly string[] Many = { "", "ones", "twos", "threes", "fours", "fives", "sixes" };

        public override string Name => "dice";
        public override string Title => "Liar's dice";

        /// <summary>A bid of q faces is higher than (prevQ, prevF) (0, 0: no bid yet) and can be true with total dice in play.</summary>
        public static bool Legal(int q, int face, int prevQ, int prevF, int total) =>
            face >= 1 && face <= 6 && q >= 1 && q <= total && (q > prevQ || q == prevQ && face > prevF);

        /// <summary>How many of these dice count for the face: ones are wild unless ones are bid.</summary>
        public static int Count(IEnumerable<string> dice, int face) =>
            dice.Sum(d => d.Count(c => c - '0' == face || face != 1 && c == '1'));

        /// <summary>The bids offered after (q, face) (0, 0: an opening, around a third of the dice): the same quantity
        /// with each higher face, then one more of each face; never ones. 10 at most.</summary>
        public static List<(int q, int face)> Bids(int q, int face, int total)
        {
            if (q == 0) { q = Math.Max(1, total / 3); face = 0; }
            var l = new List<(int, int)>();
            for (int f = Math.Max(face + 1, 2); f <= 6; f++) l.Add((q, f));
            if (q < total) for (int f = 2; f <= 6; f++) l.Add((q + 1, f));
            return l;
        }

        public static string Say(int q, int face) => $"{q} {(q == 1 ? One[face] : Many[face])}";

        // ---- the rules (host) ----
        List<byte> _ids = new List<byte>();
        int[] _count = new int[0];
        string[] _dice = new string[0];
        bool[] _left = new bool[0];
        int _stakes, _turn = -1, _round, _bidQ, _bidF, _bidder = -1;
        string _last = "-";
        bool _dealt;
        Random _rng = new Random();

        int N => _ids.Count;
        int Total => _count.Sum();

        public override void Begin(IList<byte> seats, int stakes, IList<int> cash, Random rng)
        {
            _ids = seats.ToList();
            _stakes = stakes;
            _rng = rng;
            _count = Enumerable.Repeat(Dice, N).ToArray();
            _dice = new string[N];
            _left = new bool[N];
            _last = "-";
            _dealt = true;
            NewRound(rng.Next(N));
        }

        // Fresh dice for everyone still in; the start seat (or the next one with dice) bids first. Over once one is left.
        void NewRound(int start)
        {
            _round++;
            _bidQ = _bidF = 0;
            _bidder = -1;
            for (int i = 0; i < N; i++) _dice[i] = new string(Enumerable.Range(0, _count[i]).Select(_ => (char)('1' + _rng.Next(6))).OrderBy(c => c).ToArray());
            _turn = _count.Count(c => c > 0) <= 1 ? -1 : _count[start] > 0 ? start : After(start);
        }

        int After(int seat)
        {
            for (int k = 1; k < N; k++) { int i = (seat + k) % N; if (_count[i] > 0) return i; }
            return -1;
        }

        public override int Turn => _turn;
        public override bool HandOver => _dealt && _turn < 0;

        public override string DefaultAction(int seat) =>
            _bidder < 0 ? "bid 1 2" : _bidF < 6 ? $"bid {_bidQ} {Math.Max(_bidF + 1, 2)}" : _bidQ < Total ? $"bid {_bidQ + 1} 2" : "liar";

        public override bool Act(int seat, string action)
        {
            if (HandOver || seat != _turn || action == null) return false;
            if (action == "liar" || action == "spot")
            {
                if (_bidder < 0) return false;
                Call(action == "spot");
                return true;
            }
            var t = action.Split(new[] { ' ' });
            if (t.Length != 3 || t[0] != "bid" || !Num(t[1], out int q) || !Num(t[2], out int f) || !Legal(q, f, _bidQ, _bidF, Total)) return false;
            _bidQ = q; _bidF = f; _bidder = seat;
            _turn = After(seat);
            return true;
        }

        void Call(bool spot)
        {
            int caller = _turn, count = Count(_dice, _bidF);
            _last = $"{(spot ? "S" : "L")},{caller},{_bidder},{_bidQ},{_bidF},{string.Join("/", _dice)}";
            int start = caller;
            if (!spot) { start = count < _bidQ ? _bidder : caller; _count[start]--; }
            else if (count == _bidQ) { for (int i = 0; i < N; i++) if (i != caller && _count[i] > 0) _count[i]--; }
            else _count[caller]--;
            NewRound(start);
        }

        public override void Forfeit(int seat)
        {
            if (!_dealt || HandOver || seat < 0 || seat >= N || _left[seat]) return;   // once it's decided, it stays decided
            _left[seat] = true;
            _count[seat] = 0;
            NewRound(_turn);   // the dice on the table changed: everyone rolls again, the same seat to bid (or the next)
        }

        public override string Public() =>
            $"{_stakes} {_turn} {_round} {(_bidder < 0 ? "-" : $"{_bidQ},{_bidF},{_bidder}")} "
            + string.Join(",", _ids.Select((id, i) => $"{id}:{(_left[i] ? "-" : _count[i].ToString(CultureInfo.InvariantCulture))}")) + " " + _last;

        public override string Private(int seat) => _dealt && seat >= 0 && seat < N && _count[seat] > 0 ? $"{_round} {_dice[seat]}" : null;

        public override IList<int> Settlement()
        {
            var net = Enumerable.Repeat(-_stakes, N).ToArray();   // everyone put the stakes in
            int w = Array.FindIndex(_count, c => c > 0);
            if (w < 0) return new int[N];   // nobody's left: the stakes go back
            net[w] += N * _stakes;
            return net;
        }

        // ---- the view (every seat) ----
        int _vStakes, _vTurn = -1, _vRound, _vQ, _vF, _vBidder = -1;
        List<(byte id, int dice)> _vSeats = new List<(byte, int)>();   // dice -1: left the table
        string _vKind;   // the last call: "L" or "S", null for none
        int _vCaller, _vCalled, _vLastQ, _vLastF;
        string[] _vShown;
        string _mine;
        int _mineRound;

        /// <summary>Our dice this round as digits ("" when we have none or they haven't come yet): for FakePlayer's bot.</summary>
        public string MyDice => _mine != null && _mineRound == _vRound ? _mine : "";
        public int DiceInPlay => _vSeats.Sum(s => Math.Max(0, s.dice));
        public int BidQuantity => _vQ;
        public int BidFace => _vF;

        public override void OnPublic(string state)
        {
            var t = (state ?? "").Split(new[] { ' ' });
            if (t.Length != 6 || !Num(t[0], out int stakes) || stakes <= 0 || stakes > TableMsg.MaxCents
                || !int.TryParse(t[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int turn) || !Num(t[2], out int round) || round < 1) return;
            var seats = new List<(byte, int)>();
            foreach (var s in t[4].Split(new[] { ',' }))
            {
                var p = s.Split(new[] { ':' });
                int d = -1;
                if (p.Length != 2 || seats.Count == TableMsg.MaxSeats || !byte.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte id)
                    || id == 0 || id == Protocol.Broadcast || seats.Any(x => x.Item1 == id) || p[1] != "-" && (!Num(p[1], out d) || d > Dice)) return;
                seats.Add((id, d));
            }
            int n = seats.Count, total = seats.Sum(x => Math.Max(0, x.Item2));
            if (turn < -1 || turn >= n || turn >= 0 && seats[turn].Item2 <= 0) return;
            int q = 0, f = 0, b = -1;
            if (t[3] != "-")
            {
                var p = t[3].Split(new[] { ',' });
                if (p.Length != 3 || !Num(p[0], out q) || !Num(p[1], out f) || !Num(p[2], out b) || !Legal(q, f, 0, 0, total) || b >= n) return;
            }
            string kind = null; int c = 0, cb = 0, lq = 0, lf = 0; string[] shown = null;
            if (t[5] != "-")
            {
                var p = t[5].Split(new[] { ',' });
                if (p.Length != 6 || p[0] != "L" && p[0] != "S" || !Num(p[1], out c) || c >= n || !Num(p[2], out cb) || cb >= n
                    || !Num(p[3], out lq) || !Num(p[4], out lf) || !Legal(lq, lf, 0, 0, n * Dice)) return;
                shown = p[5].Split(new[] { '/' });
                if (shown.Length != n || shown.Any(x => !IsDice(x))) return;
                kind = p[0];
            }
            _vStakes = stakes; _vTurn = turn; _vRound = round; _vSeats = seats;
            _vQ = q; _vF = f; _vBidder = b;
            _vKind = kind; _vCaller = c; _vCalled = cb; _vLastQ = lq; _vLastF = lf; _vShown = shown;
        }

        public override void OnPrivate(string secret)
        {
            var t = (secret ?? "").Split(new[] { ' ' });
            if (t.Length == 2 && Num(t[0], out int round) && t[1].Length > 0 && IsDice(t[1])) { _mine = t[1]; _mineRound = round; }
            else _mine = null;
        }

        public override IList<(string label, string action)> Options(byte me)
        {
            var o = new List<(string, string)>();
            if (_vTurn < 0 || _vTurn >= _vSeats.Count || _vSeats[_vTurn].id != me) return o;
            foreach (var (q, f) in Bids(_vQ, _vF, DiceInPlay)) o.Add(($"Bid {Say(q, f)}", $"bid {q} {f}"));
            if (_vBidder >= 0) { o.Add(($"Liar! ({Say(_vQ, _vF)} is too many)", "liar")); o.Add(($"Spot on (exactly {Say(_vQ, _vF)})", "spot")); }
            return o;
        }

        public override IList<(string text, int tone)> Lines(byte me, Func<byte, string> nameOf)
        {
            var l = new List<(string, int)>();
            if (_vSeats.Count == 0) return l;
            bool over = _vTurn < 0;
            string pot = TableMsg.Dollars(_vStakes * _vSeats.Count);
            l.Add(($"{TableMsg.Dollars(_vStakes)} each, ones wild: the last with dice takes {pot}", 0));
            for (int i = 0; i < _vSeats.Count; i++)
            {
                var (id, d) = _vSeats[i];
                string what = d < 0 ? "left the table" : d == 0 ? "out" : d == 1 ? "1 die" : $"{d} dice";
                if (_vShown != null && _vShown[i].Length > 0) what += ", showed " + string.Join(" ", _vShown[i].ToCharArray());
                l.Add(($"{nameOf(id)}: {what}", d <= 0 ? 2 : over ? 4 : id == me ? 1 : 0));
            }
            if (!over && MyDice.Length > 0) l.Add(("Your dice: " + string.Join(" ", MyDice.ToCharArray()) + " (only you see them)", 1));
            if (_vKind != null)
            {
                string caller = nameOf(_vSeats[_vCaller].id), said = Say(_vLastQ, _vLastF);
                int there = Count(_vShown, _vLastF);
                string lost = _vKind == "L" ? (there < _vLastQ ? nameOf(_vSeats[_vCalled].id) : caller) + " lost a die"
                                            : there == _vLastQ ? "everyone else lost a die" : caller + " lost a die";
                l.Add(($"{caller}: {(_vKind == "L" ? "Liar!" : "Spot on!")} {said}, there were {there}: {lost}", 3));
            }
            if (over)
            {
                var alive = _vSeats.Where(s => s.dice > 0).ToList();
                l.Add((alive.Count == 1 ? $"{nameOf(alive[0].id)} wins the {pot}" : "Nobody's left: the stakes go back", 4));
                return l;
            }
            byte next = _vSeats[_vTurn].id;
            string bid = _vBidder < 0 ? "New dice" : $"{nameOf(_vSeats[_vBidder].id)} bid {Say(_vQ, _vF)}";
            l.Add((next == me ? bid + (_vBidder < 0 ? ": your bid to open" : ": raise it, or call Liar! or Spot on")
                              : bid + $": {nameOf(next)} to {(_vBidder < 0 ? "open" : "raise or call")}", next == me ? 1 : 0));
            return l;
        }

        static bool IsDice(string d) => d.Length <= Dice && d.All(c => c >= '1' && c <= '6');
        static bool Num(string s, out int v) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v);
    }
}
