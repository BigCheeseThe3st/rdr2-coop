// Texas hold 'em (0.9.0, [Games] Poker): no-limit poker against each other at a mod table (src/Net/Table.cs runs
// the table: seats, the cash each brings, the messages, the money). The table's host deals from a deck shuffled with
// its Random. Blinds are half the stakes and the stakes; the button moves one seat a hand (the seat after it takes it
// when its player has gone); heads-up the button posts the small blind, acts first before the flop and last after.
// Four betting rounds (pre-flop, flop, turn, river): check, call, bet or raise (by at least the last raise, the big
// blind at least; all in for less is allowed but doesn't reopen the betting for those who already acted, unless
// short all-ins add up to a full raise), fold. A seat's stack for the hand is the cash it brought (at most 50 big
// blinds). All in, side pots; best five of seven at the showdown, ties split, odd cents to the first winner left of
// the button. The 45 s timer checks, or folds facing a bet. A seat that leaves folds: what it put in stays in.
// Public: "<hand> <stakes> <button> <street> <turn> <bet> <minTo> <raise> <board> <seat>,<seat>,..." with street
// 0-3 (pre-flop to river) or 4 (over), turn the seat to act (-1: nobody), bet the highest bet this round, minTo the
// smallest raise to, raise 1 when the seat to act may raise, board the cards out ("-": none) and each seat
// "<id>:<stack>:<bet>:<put in this hand>:<p playing|a all in|f folded|l left>:<cards or ->:<won from the pots>",
// cards shown only at a showdown. Private: "<hand> <two cards>". Cards: rank 23456789TJQKA, suit s h d c ("As", "Td").
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RDR2Coop.Net
{
    public sealed class Poker : TableGame
    {
        const string Ranks = "23456789TJQKA", Suits = "shdc";
        static readonly string[] Words = { "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Jack", "Queen", "King", "Ace" };
        static readonly string[] Plurals = { "Twos", "Threes", "Fours", "Fives", "Sixes", "Sevens", "Eights", "Nines", "Tens", "Jacks", "Queens", "Kings", "Aces" };
        static readonly string[] Streets = { "Pre-flop", "Flop", "Turn", "River", "Showdown" };

        public override string Name => "poker";

        // ---- cards and hands (cards are 0..51: rank * 4 + suit) ----

        public static string Code(int c) => $"{Ranks[c >> 2]}{Suits[c & 3]}";

        /// <summary>"AsKhTd" -> the cards, or null when any isn't a card or one repeats.</summary>
        public static List<int> Cards(string s)
        {
            if (s == null || s.Length % 2 != 0 || s.Length > 14) return null;
            var l = new List<int>();
            for (int i = 0; i < s.Length; i += 2)
            {
                int r = Ranks.IndexOf(s[i]), u = Suits.IndexOf(s[i + 1]);
                if (r < 0 || u < 0 || l.Contains(r * 4 + u)) return null;
                l.Add(r * 4 + u);
            }
            return l;
        }

        /// <summary>A hand's value, higher wins: the rank (0 high card .. 8 straight flush) then five tie-break ranks.</summary>
        public static int Score5(IList<int> h)
        {
            var counts = new int[13];
            bool flush = true;
            foreach (int c in h) { counts[c >> 2]++; if ((c & 3) != (h[0] & 3)) flush = false; }
            var order = Enumerable.Range(0, 13).Where(r => counts[r] > 0).OrderByDescending(r => counts[r]).ThenByDescending(r => r).ToList();
            int high = order.Count < 5 ? -1 : order[0] - order[4] == 4 ? order[0] : order[0] == 12 && order[1] == 3 ? 3 : -1;   // A-2-3-4-5 is five high
            int top = counts[order[0]], second = order.Count > 1 ? counts[order[1]] : 0;
            int cat = high >= 0 && flush ? 8 : top == 4 ? 7 : top == 3 && second == 2 ? 6 : flush ? 5 : high >= 0 ? 4 : top == 3 ? 3 : top == 2 && second == 2 ? 2 : top == 2 ? 1 : 0;
            if (cat == 8 || cat == 4) return cat << 20 | high << 16;
            int s = cat;
            for (int i = 0; i < 5; i++) s = s << 4 | (i < order.Count ? order[i] : 0);
            return s;
        }

        /// <summary>The best five of 5 to 7 cards (-1 for fewer).</summary>
        public static int Best(IList<int> cards)
        {
            int n = cards.Count, best = -1;
            var h = new int[5];
            for (int a = 0; a < n; a++) for (int b = a + 1; b < n; b++) for (int c = b + 1; c < n; c++) for (int d = c + 1; d < n; d++) for (int e = d + 1; e < n; e++)
            {
                h[0] = cards[a]; h[1] = cards[b]; h[2] = cards[c]; h[3] = cards[d]; h[4] = cards[e];
                best = Math.Max(best, Score5(h));
            }
            return best;
        }

        public static string HandName(int score)
        {
            int r0 = score >> 16 & 15, r1 = score >> 12 & 15;
            switch (score >> 20)
            {
                case 8: return r0 == 12 ? "a royal flush" : $"a straight flush, {Words[r0]} high";
                case 7: return $"four {Plurals[r0]}";
                case 6: return $"a full house, {Plurals[r0]} over {Plurals[r1]}";
                case 5: return $"a flush, {Words[r0]} high";
                case 4: return $"a straight, {Words[r0]} high";
                case 3: return $"three {Plurals[r0]}";
                case 2: return $"two pair, {Plurals[r0]} and {Plurals[r1]}";
                case 1: return $"a pair of {Plurals[r0]}";
                default: return $"{Words[r0]} high";
            }
        }

        /// <summary>The pots, main first: one per level a counted seat put in (an all-in makes a side pot above it), with
        /// the counted seats that can win it; what anyone put in above the last level goes to the last pot.</summary>
        public static List<(int amount, List<int> seats)> Pots(IList<int> put, IList<bool> counted)
        {
            var pots = new List<(int amount, List<int> seats)>();
            int prev = 0;
            long given = 0;
            foreach (int level in Enumerable.Range(0, put.Count).Where(i => counted[i]).Select(i => put[i]).Distinct().OrderBy(v => v))
            {
                int amount = put.Sum(p => Math.Max(0, Math.Min(p, level) - prev));
                pots.Add((amount, Enumerable.Range(0, put.Count).Where(i => counted[i] && put[i] >= level).ToList()));
                given += amount;
                prev = level;
            }
            if (pots.Count > 0) pots[pots.Count - 1] = (pots[pots.Count - 1].amount + (int)(put.Sum(p => (long)p) - given), pots[pots.Count - 1].seats);
            pots.RemoveAll(p => p.amount == 0);
            return pots;
        }

        // ---- the rules (host) ----
        List<byte> _ids = new List<byte>();
        int[] _stack = new int[0], _bet = new int[0], _put = new int[0], _acted = new int[0], _won = new int[0], _hole = new int[0], _board = new int[0];
        bool[] _folded = new bool[0], _gone = new bool[0];
        int _stakes, _button, _street, _turn = -1, _curBet, _fullBet, _minRaise, _hands;
        byte _buttonId;
        bool _dealt, _over, _showdown;

        public override void Begin(IList<byte> seats, int stakes, IList<int> cash, Random rng)
        {
            int n = seats.Count, was = _hands == 0 ? -1 : seats.IndexOf(_buttonId);
            _button = _hands == 0 ? 0 : was >= 0 ? (was + 1) % n : _button % n;   // its player gone: the next one slid into its place
            _ids = seats.ToList();
            _buttonId = _ids[_button];
            _hands++;
            _stakes = stakes;
            _stack = Enumerable.Range(0, n).Select(i => Math.Max(0, Math.Min(i < cash.Count ? cash[i] : 0, TableMsg.CashCap(stakes)))).ToArray();
            var deck = Enumerable.Range(0, 52).ToArray();
            for (int i = deck.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); int t = deck[i]; deck[i] = deck[j]; deck[j] = t; }
            _hole = deck.Take(2 * n).ToArray();
            _board = deck.Skip(2 * n).Take(5).ToArray();
            _bet = new int[n]; _put = new int[n]; _won = new int[n];
            _acted = Enumerable.Repeat(-1, n).ToArray();
            _folded = new bool[n]; _gone = new bool[n];
            _street = 0;
            _over = _showdown = false;
            _dealt = true;
            int sb = n == 2 ? _button : (_button + 1) % n, bb = (sb + 1) % n;
            Put(sb, stakes / 2);
            Put(bb, stakes);
            _curBet = _bet.Max();
            _fullBet = _minRaise = stakes;
            MoveOn(bb);
        }

        int N => _ids.Count;
        void Put(int i, int cents) { int a = Math.Min(cents, _stack[i]); _stack[i] -= a; _bet[i] += a; _put[i] += a; }
        bool CanAct(int i) => !_folded[i] && _stack[i] > 0;
        bool OthersCanAct(int i) => Enumerable.Range(0, N).Any(j => j != i && CanAct(j));
        bool Needs(int i) => CanAct(i) && (_bet[i] < _curBet || _acted[i] < 0 && OthersCanAct(i));
        bool CanRaise(int i) => _acted[i] < _fullBet && OthersCanAct(i) && _stack[i] > _curBet - _bet[i];

        // The next seat after `after` that has to act; when nobody has, the next round (or the showdown).
        void MoveOn(int after)
        {
            for (;;)
            {
                if (_folded.Count(f => !f) <= 1) { Finish(); return; }
                for (int k = 1; k <= N; k++) { int i = (after + k) % N; if (Needs(i)) { _turn = i; return; } }
                if (_street == 3) { Finish(); return; }
                _street++;
                for (int i = 0; i < N; i++) { _bet[i] = 0; _acted[i] = -1; }
                _curBet = _fullBet = 0;
                _minRaise = _stakes;
                after = _button;
            }
        }

        void Finish()
        {
            _turn = -1;
            _over = true;
            var live = Enumerable.Range(0, N).Where(i => !_folded[i]).ToList();
            _showdown = live.Count >= 2;
            _won = new int[N];
            if (live.Count == 0) { _won = _put.ToArray(); return; }   // everybody left: what they put in goes back
            var score = new int[N];
            foreach (int i in live) score[i] = Best(new[] { _hole[2 * i], _hole[2 * i + 1] }.Concat(_board).ToList());
            foreach (var (amount, seats) in Pots(_put, _folded.Select(f => !f).ToList()))
            {
                int best = seats.Max(i => score[i]);
                var winners = seats.Where(i => score[i] == best).OrderBy(i => (i - _button - 1 + 2 * N) % N).ToList();   // left of the button first
                for (int k = 0; k < winners.Count; k++) _won[winners[k]] += amount / winners.Count + (k < amount % winners.Count ? 1 : 0);
            }
        }

        public override int Turn => _turn;
        public override bool HandOver => _dealt && _over;
        public override string DefaultAction(int seat) => seat >= 0 && seat < N && _bet[seat] < _curBet ? "fold" : "check";

        public override bool Act(int seat, string action)
        {
            if (!_dealt || _over || seat != _turn || action == null) return false;
            var t = action.Split(new[] { ' ' });
            int toCall = _curBet - _bet[seat];
            switch (t[0])
            {
                case "fold": if (t.Length != 1) return false; _folded[seat] = true; break;
                case "check": if (t.Length != 1 || toCall > 0) return false; break;
                case "call": if (t.Length != 1 || toCall <= 0) return false; Put(seat, toCall); break;
                case "raise":
                    if (t.Length != 2 || !int.TryParse(t[1], NumberStyles.None, CultureInfo.InvariantCulture, out int to) || !CanRaise(seat) || to <= _curBet) return false;
                    int add = to - _bet[seat];
                    if (add > _stack[seat] || to - _curBet < _minRaise && add != _stack[seat]) return false;   // under the min raise only all in
                    Put(seat, add);
                    if (to - _curBet >= _minRaise) { _minRaise = to - _curBet; _fullBet = to; }
                    else if (to - _fullBet >= _minRaise) _fullBet = to;   // short all-ins that add up to a full raise reopen it
                    _curBet = to;
                    break;
                default: return false;
            }
            _acted[seat] = _fullBet;
            MoveOn(seat);
            return true;
        }

        public override void Forfeit(int seat)
        {
            if (!_dealt || _over || seat < 0 || seat >= N) return;   // once it's decided, it stays decided
            _gone[seat] = true;
            if (_folded[seat]) return;
            _folded[seat] = true;
            MoveOn(seat == _turn ? seat : (_turn - 1 + N) % N);   // the seat to act may not need to any more
        }

        public override string Public()
        {
            if (!_dealt) return "";
            int shown = _over && _showdown ? 5 : _street == 0 ? 0 : _street + 2;
            var seats = _ids.Select((id, i) => $"{id}:{_stack[i]}:{_bet[i]}:{_put[i]}:{(_gone[i] ? 'l' : _folded[i] ? 'f' : _stack[i] == 0 ? 'a' : 'p')}:"
                                              + $"{(_over && _showdown && !_folded[i] ? Code(_hole[2 * i]) + Code(_hole[2 * i + 1]) : "-")}:{_won[i]}");
            return $"{_hands} {_stakes} {_button} {(_over ? 4 : _street)} {_turn} {_curBet} {_curBet + _minRaise} {(_turn >= 0 && CanRaise(_turn) ? 1 : 0)} "
                 + $"{(shown == 0 ? "-" : string.Concat(_board.Take(shown).Select(Code)))} {string.Join(",", seats)}";
        }

        public override string Private(int seat) => _dealt && seat >= 0 && seat < N ? $"{_hands} {Code(_hole[2 * seat])}{Code(_hole[2 * seat + 1])}" : null;

        public override IList<int> Settlement() => Enumerable.Range(0, N).Select(i => _over ? _won[i] - _put[i] : 0).ToList();

        // ---- the view (every seat) ----
        sealed class Seat { public byte Id; public int Stack, Bet, Put, Won; public char State; public List<int> Cards; }
        int _vHand, _vStakes, _vButton, _vStreet, _vTurn = -1, _vBet, _vMinTo, _mineHand, _ver;
        bool _vRaise;
        (int ver, byte me) _linesFor = (-1, 0);
        List<(string, int)> _lines;
        List<int> _vBoard = new List<int>(), _mine;
        List<Seat> _vSeats = new List<Seat>();

        public override void OnPublic(string state)
        {
            var t = (state ?? "").Split(new[] { ' ' });
            if (t.Length != 10 || !Num(t[0], out int hand) || hand < 1 || !Num(t[1], out int stakes) || stakes <= 0 || stakes > TableMsg.MaxCents
                || !Num(t[2], out int button) || !Num(t[3], out int street) || street > 4 || !int.TryParse(t[4], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int turn)
                || !Num(t[5], out int bet) || !Num(t[6], out int minTo) || bet > TableMsg.MaxCents || minTo > 2 * TableMsg.MaxCents || (t[7] != "0" && t[7] != "1")) return;
            var board = t[8] == "-" ? new List<int>() : Cards(t[8]);
            if (board == null || board.Count > 5) return;
            var seats = new List<Seat>();
            foreach (var s in t[9].Split(new[] { ',' }))
            {
                var p = s.Split(new[] { ':' });
                if (p.Length != 7 || seats.Count == TableMsg.MaxSeats || !byte.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte id) || id == 0
                    || id == Protocol.Broadcast || seats.Any(x => x.Id == id) || !Num(p[1], out int stack) || !Num(p[2], out int b) || !Num(p[3], out int put)
                    || !Num(p[6], out int won) || stack > TableMsg.MaxCents || b > TableMsg.MaxCents || put > TableMsg.MaxCents || won > TableMsg.MaxSeats * TableMsg.MaxCents
                    || p[4].Length != 1 || "pafl".IndexOf(p[4][0]) < 0) return;
                var cards = p[5] == "-" ? null : Cards(p[5]);
                if (p[5] != "-" && (cards == null || cards.Count != 2)) return;
                seats.Add(new Seat { Id = id, Stack = stack, Bet = b, Put = put, Won = won, State = p[4][0], Cards = cards });
            }
            if (seats.Count < 2 || button >= seats.Count || turn < -1 || turn >= seats.Count) return;
            _vHand = hand; _vStakes = stakes; _vButton = button; _vStreet = street; _vTurn = turn; _vBet = bet; _vMinTo = minTo; _vRaise = t[7] == "1";
            _vBoard = board; _vSeats = seats;
            _ver++;
        }

        public override void OnPrivate(string secret)
        {
            var t = (secret ?? "").Split(new[] { ' ' });
            var cards = t.Length == 2 ? Cards(t[1]) : null;
            if (cards == null || cards.Count != 2 || !Num(t[0], out int hand) || hand < _mineHand) return;
            if (hand != _mineHand) Stats.Add("poker.hands");   // dealt in: once a hand
            _mine = cards;
            _mineHand = hand;
            _ver++;
        }

        /// <summary>For the bots: what we have to put in to call (0: we can check), the stakes (the big blind).</summary>
        public int ToCall(byte me) { var s = _vSeats.Find(x => x.Id == me); return s == null ? 0 : Math.Max(0, _vBet - s.Bet); }
        public int Stakes => _vStakes;

        /// <summary>Our hand's rank so far: -1 no cards, 0 high card, 1 a pair (a pocket pair before the flop)... 8.</summary>
        public int MyRank()
        {
            var mine = Mine;
            if (mine == null) return -1;
            if (_vBoard.Count < 3) return mine[0] >> 2 == mine[1] >> 2 ? 1 : 0;
            return Best(mine.Concat(_vBoard).ToList()) >> 20;
        }

        List<int> Mine => _mine != null && _mineHand == _vHand ? _mine : null;

        public override IList<(string label, string action)> Options(byte me)
        {
            var o = new List<(string, string)>();
            int s = _vSeats.FindIndex(x => x.Id == me);
            if (s < 0 || s != _vTurn || _vStreet > 3) return o;
            var my = _vSeats[s];
            int toCall = Math.Max(0, _vBet - my.Bet), all = my.Bet + my.Stack;
            if (toCall == 0) o.Add(("Check", "check"));
            else o.Add((toCall >= my.Stack ? $"Call {D(my.Stack)} (all in)" : $"Call {D(toCall)}", "call"));
            if (_vRaise && my.Stack > toCall)
            {
                string verb = _vBet == 0 ? "Bet" : "Raise to";
                var seen = new HashSet<int>();
                foreach (var (what, to) in new[] { ("min", _vMinTo), ("2x", 2 * _vMinTo - _vBet), ("pot", _vBet + _vSeats.Sum(x => x.Put) + toCall) })
                    if (to >= _vMinTo && to < all && seen.Add(to)) o.Add(($"{verb} {D(to)} ({what})", "raise " + to));
                o.Add(($"All in ({D(all)})", "raise " + all));
            }
            if (toCall > 0) o.Add(("Fold", "fold"));
            return o;
        }

        public override IList<(string text, int tone)> Lines(byte me, Func<byte, string> nameOf)
        {
            // shortcut: built again only when the hand or our cards change (it's drawn every frame), so a partner's new name shows from the next move
            if (_linesFor == (_ver, me)) return _lines;
            var l = new List<(string, int)>();
            if (_vSeats.Count == 0) return l;
            bool over = _vStreet == 4;
            var live = _vSeats.Where(x => x.State == 'p' || x.State == 'a').ToList();
            int max = live.Count == 0 ? 0 : live.Max(x => x.Put);   // the pots so far: a side pot above each all-in
            var pots = Pots(_vSeats.Select(x => x.Put).ToList(), _vSeats.Select(x => x.State == 'a' || (x.State == 'p' && x.Put == max)).ToList()).Select(p => D(p.amount)).ToList();
            string pot = pots.Count == 0 ? "" : "   pot " + pots[0] + (pots.Count > 1 ? ", side " + string.Join(", ", pots.Skip(1)) : "");
            l.Add(("s spades, c clubs, h hearts, d diamonds; T = 10", 2));
            l.Add(($"{Streets[_vStreet]}: {(_vBoard.Count == 0 ? $"blinds {D(_vStakes / 2)}/{D(_vStakes)}" : Show(_vBoard))}{pot}", 0));
            for (int i = 0; i < _vSeats.Count; i++)
            {
                var x = _vSeats[i];
                string who = nameOf(x.Id) + (i == _vButton ? " (D)" : "");
                string what = x.State == 'l' ? "left" : x.State == 'f' ? "folded"
                    : x.Cards != null ? $"{Show(x.Cards)}, {HandName(Best(x.Cards.Concat(_vBoard).ToList()))}"
                    : D(x.Stack) + (x.Bet > 0 ? $", bet {D(x.Bet)}" : "") + (x.State == 'a' ? ", all in" : "") + (i == _vTurn ? ", to act" : "");
                if (over && x.Won > 0) what += $", takes {D(x.Won)}";
                l.Add(($"{who}: {what}", over && x.Won > 0 ? 4 : x.State == 'f' || x.State == 'l' ? 2 : x.Id == me ? 1 : 0));
            }
            var mine = Mine;
            if (mine != null && _vSeats.Any(x => x.Id == me))
                l.Add(($"Your cards: {Show(mine)}" + (_vBoard.Count >= 3 ? $" ({HandName(Best(mine.Concat(_vBoard).ToList()))})" : mine[0] >> 2 == mine[1] >> 2 ? $" (a pair of {Plurals[mine[0] >> 2]})" : ""), 1));
            if (over)
            {
                var shown = live.Where(x => x.Cards != null).Select(x => (x, score: Best(x.Cards.Concat(_vBoard).ToList()))).ToList();
                int best = shown.Count > 0 ? shown.Max(v => v.score) : -1;
                var top = shown.Where(v => v.score == best).Select(v => nameOf(v.x.Id)).ToList();
                l.Add((live.Count == 0 ? "Everyone left: the bets go back" : live.Count == 1 ? $"{nameOf(live[0].Id)} wins: everyone else folded"
                    : top.Count == 1 ? $"{top[0]} wins with {HandName(best)}" : $"{string.Join(" and ", top)} split it with {HandName(best)}", 4));
            }
            else if (_vTurn >= 0)
            {
                var next = _vSeats[_vTurn];
                int toCall = Math.Max(0, _vBet - next.Bet);
                l.Add(next.Id == me ? ($"Your move: {(toCall > 0 ? D(toCall) + " to call" : "check or bet")}", 1) : ($"{nameOf(next.Id)} to act", 0));
            }
            _linesFor = (_ver, me);
            return _lines = l;
        }

        static string Show(IEnumerable<int> cards) => string.Join(" ", cards.Select(Code));
        static string D(int cents) => TableMsg.Dollars(cents);
        static bool Num(string s, out int v) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v);
    }
}
