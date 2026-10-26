// Blackjack (0.9.0): a game for the mod's tables (src/Net/Table.cs, TableGame.cs). The player who opened the table
// deals and is the house: seat 0 of every hand (HostIsHouse: no round is dealt while the host hasn't the stakes).
// The other seats bet 1, 2 or 5 times the stakes or sit the round out, all at once (the turn timer waits on the
// first seat still to bet: 45 s, then it sits out). Each betting seat gets two cards from a six-deck
// shoe (shuffled with the host's Random, again whenever under a quarter of it is left), the dealer an up-card and a
// hole card; a dealer's blackjack ends the round there. Otherwise each seat in turn hits, stands or doubles (two
// cards only: twice the bet, one card, done); 21 stands by itself. No split, no insurance. The dealer then draws to
// 17 and stands on every 17, soft ones too. Blackjack pays 3:2, a win 1:1, a push gives the bet back, a bust loses
// (even when the dealer busts too). The house takes what the seats lose and pays what they win, but loses at most
// 10 x stakes x seats a round, and never more than the cash it brought: past that every win is cut by the same share,
// so the round still adds up to zero. A seat that leaves stands where it is: a losing hand still loses, a winning one
// comes back as a push (the framework never pays a seat that left). If the house itself gets up, the seats stand
// where they are and its hand plays out, so walking off never calls off a round it's losing.
// Public: "<stakes> <phase> <turn> <cap> <seat> <seat>..." with phase b (bets), p (play), d (done) or c (done, wins
// cut to the cap), turn the seat to move (-1 none), cap the most the house loses this round, and per seat, the
// dealer first, "<id>:<bet>:<cards>:<flag>:<net>": cards "AS.TD" ("??" the hole card, "-" none), flag h (the house),
// w (to bet), o (sat out), p (to play), s (stood), d (doubled), x (bust), b (blackjack), l (left); net once it's done.
// Private: the cash that seat brought (cents), so its view offers only the bets it can cover.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RDR2Coop.Net
{
    public sealed class Blackjack : TableGame
    {
        const string Ranks = "23456789TJQKA", Suits = "CDHS";
        public const int Decks = 6, HouseTimesStakes = 10, MaxCards = 22;
        static readonly int[] Bets = { 1, 2, 5 };

        public override string Name => "blackjack";
        public override bool HostIsHouse => true;

        // ---- the rules (host) ----
        List<string> _shoe = new List<string>();
        int _pos;
        Random _rng = new Random();
        List<byte> _ids = new List<byte>();
        List<string>[] _hands = new List<string>[0];
        int[] _bet = new int[0], _cash = new int[0], _net = new int[0];
        char[] _flag = new char[0];
        bool[] _left = new bool[0];
        int _stakes, _cap, _turn = -1;
        char _phase = 'b';

        public int ShoeLeft => _shoe.Count - _pos;
        public int Shuffles { get; private set; }

        public override void Begin(IList<byte> seats, int stakes, IList<int> cash, Random rng)
        {
            _rng = rng ?? _rng;
            if (ShoeLeft < Decks * 52 / 4) Shuffle();
            int n = seats.Count;
            _ids = seats.ToList(); _stakes = stakes; _cash = cash.ToArray();
            _hands = Enumerable.Range(0, n).Select(_ => new List<string>()).ToArray();
            _bet = new int[n]; _net = new int[n]; _left = new bool[n];
            _flag = Enumerable.Range(0, n).Select(i => i == 0 ? 'h' : 'w').ToArray();
            _cap = n == 0 ? 0 : (int)Math.Min((long)HouseTimesStakes * stakes * (n - 1), cash[0]);
            _phase = 'b';
            Next();
        }

        void Shuffle()
        {
            _shoe = Enumerable.Range(0, Decks).SelectMany(_ => Ranks.SelectMany(r => Suits.Select(s => $"{r}{s}"))).ToList();
            for (int i = _shoe.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); var t = _shoe[i]; _shoe[i] = _shoe[j]; _shoe[j] = t; }
            _pos = 0;
            Shuffles++;
        }

        string Draw() { if (ShoeLeft <= 0) Shuffle(); return _shoe[_pos++]; }

        /// <summary>Tests: these cards come off the shoe next.</summary>
        internal void Stack(params string[] cards) => _shoe.InsertRange(_pos, cards);

        // Bets until none is waiting, then the deal, the seats in turn, the dealer, the settlement.
        void Next()
        {
            if (_phase == 'b' && (_turn = Array.IndexOf(_flag, 'w')) < 0) Deal();
            if (_phase == 'p' && (_turn = Array.IndexOf(_flag, 'p')) < 0) Dealer();
        }

        void Deal()
        {
            _phase = 'p';
            if (!_flag.Contains('p')) return;   // everyone sat out: Next settles a round of nothing
            for (int i = 1; i < _ids.Count; i++)
                if (_flag[i] == 'p') { _hands[i].Add(Draw()); _hands[i].Add(Draw()); if (Natural(_hands[i])) _flag[i] = 'b'; }
            _hands[0].Add(Draw()); _hands[0].Add(Draw());
            if (Natural(_hands[0])) for (int i = 1; i < _ids.Count; i++) if (_flag[i] == 'p') _flag[i] = 's';   // the dealer's blackjack: nobody plays on
        }

        void Dealer()
        {
            _turn = -1;
            bool live = Enumerable.Range(1, Math.Max(0, _ids.Count - 1)).Any(i => _hands[i].Count > 0 && Total(_hands[i]).total <= 21 && !Natural(_hands[i]));
            if (live) while (Total(_hands[0]).total < 17) _hands[0].Add(Draw());   // stands on all 17s
            _phase = 'd';
            for (int i = 1; i < _ids.Count; i++)
                if (_bet[i] > 0) { _net[i] = _bet[i] * Outcome(_hands[i], _hands[0]) / 2; if (_left[i]) _net[i] = Math.Min(_net[i], 0); }
            long won = _net.Where(x => x > 0).Sum(x => (long)x), lost = -_net.Where(x => x < 0).Sum(x => (long)x);
            if (won - lost > _cap)   // more than the house covers: every win cut by the same share
            {
                _phase = 'c';
                for (int i = 1; i < _ids.Count; i++) if (_net[i] > 0) _net[i] = (int)(_net[i] * (_cap + lost) / won);
            }
            _net[0] = -_net.Skip(1).Sum();
        }

        public override int Turn => HandOver ? -1 : _turn;
        public override bool HandOver => _phase == 'd' || _phase == 'c';
        public override string DefaultAction(int seat) => _phase == 'b' ? "sit" : "stand";

        public override bool Act(int seat, string action)
        {
            if (HandOver || seat <= 0 || seat >= _ids.Count) return false;
            if (_phase == 'b')
            {
                if (_flag[seat] != 'w') return false;
                if (action == "sit") _flag[seat] = 'o';
                else
                {
                    int k = action != null && action.StartsWith("bet ", StringComparison.Ordinal)
                        && int.TryParse(action.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out int x) ? x : 0;
                    if (Array.IndexOf(Bets, k) < 0 || k * _stakes > _cash[seat]) return false;
                    _bet[seat] = k * _stakes;
                    _flag[seat] = 'p';
                }
                Next();
                return true;
            }
            if (seat != _turn) return false;
            var h = _hands[seat];
            switch (action)
            {
                case "stand": _flag[seat] = 's'; break;
                case "hit": h.Add(Draw()); break;
                case "double":
                    if (h.Count != 2 || 2 * _bet[seat] > _cash[seat]) return false;
                    _bet[seat] *= 2; h.Add(Draw()); _flag[seat] = 'd';
                    break;
                default: return false;
            }
            int t = Total(h).total;
            if (t > 21) _flag[seat] = 'x';
            else if (t == 21 && _flag[seat] == 'p') _flag[seat] = 's';
            Next();
            return true;
        }

        public override void Forfeit(int seat)
        {
            if (HandOver || seat < 0 || seat >= _ids.Count || _left[seat]) return;
            _left[seat] = true;
            if (seat == 0)   // the house got up: everyone stands, its hand plays out
            {
                for (int i = 1; i < _ids.Count; i++) _flag[i] = _flag[i] == 'w' ? 'o' : _flag[i] == 'p' ? 's' : _flag[i];
                Dealer();
                return;
            }
            if (_flag[seat] == 'w') _flag[seat] = 'o';
            if (_flag[seat] == 'p') _flag[seat] = 's';   // a player leaving stands
            Next();
        }

        public override string Public() =>
            $"{_stakes} {_phase} {Turn} {_cap} " + string.Join(" ", _ids.Select((id, i) => $"{id}:{_bet[i]}:{Cards(i)}:{(_left[i] ? 'l' : _flag[i])}:{_net[i]}"));

        string Cards(int i)
        {
            var h = _hands[i];
            if (h.Count == 0) return "-";
            return i == 0 && _phase == 'p' ? h[0] + ".??" : string.Join(".", h);   // the hole card stays down until the seats are done
        }

        public override string Private(int seat) => seat > 0 && seat < _cash.Length ? _cash[seat].ToString(CultureInfo.InvariantCulture) : null;

        public override IList<int> Settlement() => _net.ToArray();

        // ---- the hands (both sides) ----

        /// <summary>The best total of some cards ("AS", "TD"...) and whether an ace counts 11 in it (a soft total).</summary>
        public static (int total, bool soft) Total(IEnumerable<string> cards)
        {
            int t = 0;
            bool ace = false;
            foreach (var c in cards) { int r = Ranks.IndexOf(c[0]); t += r == 12 ? 1 : Math.Min(10, r + 2); ace |= r == 12; }
            return ace && t <= 11 ? (t + 10, true) : (t, false);
        }

        public static bool Natural(IList<string> cards) => cards.Count == 2 && Total(cards).total == 21;

        /// <summary>A hand against the dealer's, in half bets: 3 blackjack, 2 win, 0 push, -2 lose.</summary>
        public static int Outcome(IList<string> hand, IList<string> dealer)
        {
            int p = Total(hand).total, d = Total(dealer).total;
            if (p > 21) return -2;
            bool pbj = Natural(hand), dbj = Natural(dealer);
            if (pbj || dbj) return pbj && dbj ? 0 : pbj ? 3 : -2;
            return d > 21 || p > d ? 2 : p == d ? 0 : -2;
        }

        // ---- the view (every seat) ----
        sealed class Seat { public byte Id; public int Bet, Net; public string[] Cards; public char Flag; }
        int _vStakes, _vTurn = -1, _vCap, _vCash = -1;
        char _vPhase;
        List<Seat> _v = new List<Seat>();

        public override void OnPublic(string state)
        {
            var t = (state ?? "").Split(new[] { ' ' });
            if (t.Length < 6 || t.Length > 4 + TableMsg.MaxSeats || !Num(t[0], out int stakes) || stakes == 0 || t[1].Length != 1 || "bpdc".IndexOf(t[1][0]) < 0
                || !int.TryParse(t[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int turn) || turn < -1 || turn == 0 || turn >= t.Length - 4
                || !Num(t[3], out int cap)) return;
            var seats = new List<Seat>();
            for (int i = 4; i < t.Length; i++)
            {
                var p = t[i].Split(new[] { ':' });
                if (p.Length != 5 || !byte.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte id) || id == 0 || id == Protocol.Broadcast
                    || seats.Any(s => s.Id == id) || !Num(p[1], out int bet) || p[3].Length != 1 || (i == 4 ? "hl" : "wospdxbl").IndexOf(p[3][0]) < 0
                    || !int.TryParse(p[4], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int net) || Math.Abs((long)net) > TableMsg.MaxCents) return;
                var cards = p[2] == "-" ? new string[0] : p[2].Split(new[] { '.' });
                if (cards.Length > MaxCards || cards.Any(c => !IsCard(c) && !(c == "??" && i == 4))) return;
                seats.Add(new Seat { Id = id, Bet = bet, Cards = cards, Flag = p[3][0], Net = net });
            }
            _vStakes = stakes; _vPhase = t[1][0]; _vTurn = turn; _vCap = cap; _v = seats;
        }

        public override void OnPrivate(string secret) => _vCash = Num(secret, out int c) ? c : -1;

        /// <summary>The view's cards for a player (none: empty), and the dealer's up-card (null before the deal): for bots.</summary>
        public IList<string> CardsOf(byte id) => _v.FirstOrDefault(s => s.Id == id)?.Cards ?? new string[0];
        public string UpCard => _v.Count > 0 && _v[0].Cards.Length > 0 ? _v[0].Cards[0] : null;

        public override IList<(string label, string action)> Options(byte me)
        {
            var o = new List<(string, string)>();
            int i = _v.FindIndex(x => x.Id == me);
            if (i <= 0) return o;
            var s = _v[i];
            if (_vPhase == 'b' && s.Flag == 'w')
            {
                foreach (int k in Bets) if (k == 1 || k * _vStakes <= _vCash) o.Add(($"Bet {TableMsg.Dollars(k * _vStakes)}" + (k > 1 ? $" ({k}x)" : ""), "bet " + k));
                o.Add(("Sit this round out", "sit"));
            }
            else if (_vPhase == 'p' && _vTurn == i && s.Flag == 'p')
            {
                o.Add(($"Hit (you have {Says(s.Cards)})", "hit"));
                o.Add(("Stand", "stand"));
                if (s.Cards.Length == 2 && 2 * s.Bet <= _vCash) o.Add(($"Double: {TableMsg.Dollars(s.Bet)} more, one card", "double"));
            }
            return o;
        }

        public override IList<(string text, int tone)> Lines(byte me, Func<byte, string> nameOf)
        {
            var l = new List<(string, int)>();
            if (_v.Count == 0) return l;
            bool done = _vPhase == 'd' || _vPhase == 'c';
            var house = _v[0];
            if (house.Flag == 'l') l.Add(($"{nameOf(house.Id)} (the house) got up" + (house.Cards.Length == 0 ? ": nothing dealt" : ": " + Show(house.Cards) + (done ? Result(house.Net, "even") : "")), 3));
            else l.Add(($"{nameOf(house.Id)} deals" + (house.Cards.Length == 0 ? " (the house)" : ": " + Show(house.Cards) + (done ? Result(house.Net, "even") : "")),
                        done && house.Net > 0 ? 4 : house.Id == me ? 1 : 0));
            for (int i = 1; i < _v.Count; i++)
            {
                var s = _v[i];
                string what = s.Flag == 'w' ? "placing a bet" : s.Flag == 'o' ? "sits this round out" : s.Bet == 0 ? "left the table"
                    : $"{TableMsg.Dollars(s.Bet)} on " + (s.Cards.Length == 0 ? "the deal" : Show(s.Cards))
                      + (s.Flag == 'd' ? ", doubled" : s.Flag == 'l' ? ", left" : "") + (done ? Result(s.Net, "push") : "");
                l.Add(($"{nameOf(s.Id)}: {what}", done && s.Net > 0 ? 4 : s.Flag == 'o' || s.Flag == 'l' ? 2 : s.Id == me ? 1 : 0));
            }
            int mine = _v.FindIndex(x => x.Id == me);
            if (_vPhase == 'b') l.Add(mine > 0 && _v[mine].Flag == 'w' ? ("Your bet: pick it from the list", 1) : ("Waiting for bets", 2));
            else if (_vPhase == 'p' && _vTurn > 0) l.Add(_vTurn == mine ? ("Your move: hit, stand or double", 1) : ($"{nameOf(_v[_vTurn].Id)} to play", 0));
            if (_vPhase == 'c') l.Add(($"The house covers only {TableMsg.Dollars(_vCap)} a round: wins cut", 3));
            else if (!done) l.Add(($"The house covers up to {TableMsg.Dollars(_vCap)} a round", 2));
            return l;
        }

        // "K 7 (17)", "A 6 (soft 17)", "A K (blackjack)", "K 6 9 (25, bust)"; the dealer's "K ?" until the hole card shows.
        static string Show(string[] cards)
        {
            string ranks = string.Join(" ", cards.Select(c => c == "??" ? "?" : c[0] == 'T' ? "10" : c[0].ToString()));
            return cards.Contains("??") ? ranks : $"{ranks} ({Says(cards)})";
        }

        static string Says(string[] cards)
        {
            var (total, soft) = Total(cards);
            return Natural(cards) ? "blackjack" : total > 21 ? $"{total}, bust" : soft ? $"soft {total}" : total.ToString(CultureInfo.InvariantCulture);
        }

        static string Result(int net, string zero) => net > 0 ? $", won {TableMsg.Dollars(net)}" : net < 0 ? $", lost {TableMsg.Dollars(-net)}" : ", " + zero;

        static bool IsCard(string c) => c != null && c.Length == 2 && Ranks.IndexOf(c[0]) >= 0 && Suits.IndexOf(c[1]) >= 0;

        static bool Num(string s, out int v) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v) && v <= TableMsg.MaxCents;
    }
}
