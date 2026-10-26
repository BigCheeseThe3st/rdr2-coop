// High card (0.9.0): the tables framework's hidden sample game ([Games] HighCard=Yes shows it), the smallest game
// that goes through everything: everyone antes the stakes, gets one card face down (a priv: only they see it), then
// in seat order each shows theirs (45 s each, or the table shows it for them); the highest rank takes the pot, ties
// split it (suits don't count). A seat that leaves before it's over forfeits its ante. The game packages copy this.
// Public: "<stakes> <turn> <id>:<card>,..." with turn the seat to show (-1 when it's over) and card "??" face
// down, "--" left the hand, else rank and suit ("QH", "TS"); once it's over every card still in shows.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RDR2Coop.Net
{
    public sealed class HighCard : TableGame
    {
        const string Ranks = "23456789TJQKA", Suits = "CDHS";

        public override string Name => "highcard";
        public override string Title => "High card";

        // ---- the rules (host) ----
        List<byte> _ids = new List<byte>();
        string[] _cards = new string[0];
        bool[] _shown = new bool[0], _out = new bool[0];
        int _stakes, _turn = -1;
        bool _dealt;

        public override void Begin(IList<byte> seats, int stakes, IList<int> cash, Random rng)
        {
            _ids = seats.ToList();
            _stakes = stakes;
            var deck = Ranks.SelectMany(r => Suits.Select(s => $"{r}{s}")).ToList();
            for (int i = deck.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = deck[i]; deck[i] = deck[j]; deck[j] = t; }
            _cards = deck.Take(_ids.Count).ToArray();
            _shown = new bool[_ids.Count];
            _out = new bool[_ids.Count];
            _dealt = true;
            Next();
        }

        // The first seat still in that hasn't shown; nobody once one or none is left in.
        void Next()
        {
            _turn = -1;
            if (_out.Count(o => !o) <= 1) return;
            for (int i = 0; i < _ids.Count && _turn < 0; i++) if (!_out[i] && !_shown[i]) _turn = i;
        }

        public override int Turn => _turn;
        public override bool HandOver => _dealt && _turn < 0;
        public override string DefaultAction(int seat) => "show";

        public override bool Act(int seat, string action)
        {
            if (HandOver || seat != _turn || action != "show") return false;
            _shown[seat] = true;
            Next();
            return true;
        }

        public override void Forfeit(int seat)
        {
            if (!_dealt || HandOver || seat < 0 || seat >= _ids.Count || _out[seat]) return;   // once it's decided, it stays decided
            _out[seat] = true;
            Next();
        }

        public override string Public() =>
            $"{_stakes} {_turn} " + string.Join(",", _ids.Select((id, i) => $"{id}:{(_out[i] ? "--" : _shown[i] || HandOver ? _cards[i] : "??")}"));

        public override string Private(int seat) => _dealt && seat >= 0 && seat < _cards.Length ? _cards[seat] : null;

        public override IList<int> Settlement()
        {
            int n = _ids.Count;
            var net = Enumerable.Repeat(-_stakes, n).ToArray();   // everyone antes
            var inHand = Enumerable.Range(0, n).Where(i => !_out[i]).ToList();
            if (inHand.Count == 0) return new int[n];   // everybody left: the antes go back
            int best = inHand.Max(i => Rank(_cards[i]));
            var winners = inHand.Where(i => Rank(_cards[i]) == best).ToList();
            int pot = n * _stakes, share = pot / winners.Count, odd = pot % winners.Count;
            for (int k = 0; k < winners.Count; k++) net[winners[k]] += share + (k < odd ? 1 : 0);   // odd cents to the first seats
            return net;
        }

        // ---- the view (every seat) ----
        int _vStakes, _vTurn = -1;
        List<(byte id, string card)> _vSeats = new List<(byte, string)>();
        string _mine;

        public override void OnPublic(string state)
        {
            var t = (state ?? "").Split(new[] { ' ' });
            if (t.Length != 3 || !int.TryParse(t[0], NumberStyles.None, CultureInfo.InvariantCulture, out int stakes) || stakes <= 0 || stakes > TableMsg.MaxCents
                || !int.TryParse(t[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int turn)) return;
            var seats = new List<(byte, string)>();
            foreach (var s in t[2].Split(new[] { ',' }))
            {
                var p = s.Split(new[] { ':' });
                if (p.Length != 2 || seats.Count == TableMsg.MaxSeats || !byte.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte id) || id == 0
                    || !(p[1] == "??" || p[1] == "--" || IsCard(p[1]))) return;
                seats.Add((id, p[1]));
            }
            if (turn < -1 || turn >= seats.Count) return;
            _vStakes = stakes; _vTurn = turn; _vSeats = seats;
        }

        public override void OnPrivate(string secret) => _mine = IsCard(secret) ? secret : null;

        public override IList<(string label, string action)> Options(byte me) =>
            _vTurn >= 0 && _vSeats[_vTurn].id == me ? new List<(string, string)> { ("Show my card", "show") } : new List<(string, string)>();

        public override IList<(string text, int tone)> Lines(byte me, Func<byte, string> nameOf)
        {
            var l = new List<(string, int)>();
            if (_vSeats.Count == 0) return l;
            bool over = _vTurn < 0;
            var shown = _vSeats.Where(s => IsCard(s.card)).ToList();
            int best = over && shown.Count > 0 ? shown.Max(s => Rank(s.card)) : -1;
            l.Add(($"{TableMsg.Dollars(_vStakes)} each: the highest card takes the pot", 0));
            foreach (var (id, card) in _vSeats)
            {
                string what = card == "--" ? "left the hand" : card == "??" ? (id == me && _mine != null ? Named(_mine) + " (only you see it)" : "face down") : Named(card);
                int tone = card == "--" ? 2 : IsCard(card) && Rank(card) == best ? 4 : id == me ? 1 : 0;
                l.Add(($"{nameOf(id)}: {what}", tone));
            }
            if (!over) { byte next = _vSeats[_vTurn].id; l.Add((next == me ? "Your turn: show your card" : $"{nameOf(next)} to show", next == me ? 1 : 0)); return l; }
            var won = shown.Where(s => Rank(s.card) == best).Select(s => nameOf(s.id)).ToList();
            string pot = TableMsg.Dollars(_vStakes * _vSeats.Count);
            l.Add((won.Count == 0 ? "Nobody's left in: the antes go back" : won.Count == 1 ? $"{won[0]} takes the {pot}" : $"{string.Join(" and ", won)} split the {pot}", 4));
            return l;
        }

        static bool IsCard(string c) => c != null && c.Length == 2 && Ranks.IndexOf(c[0]) >= 0 && Suits.IndexOf(c[1]) >= 0;
        static int Rank(string c) => Ranks.IndexOf(c[0]);

        static string Named(string c)
        {
            string rank = c[0] == 'T' ? "10" : c[0] == 'J' ? "Jack" : c[0] == 'Q' ? "Queen" : c[0] == 'K' ? "King" : c[0] == 'A' ? "Ace" : c[0].ToString();
            string suit = c[1] == 'C' ? "clubs" : c[1] == 'D' ? "diamonds" : c[1] == 'H' ? "hearts" : "spades";
            return $"{rank} of {suit}";
        }
    }
}
