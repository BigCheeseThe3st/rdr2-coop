// Guests at the game's own poker (0.9.0-beta.8.4). The host plays poker_sp in its own game, as usual (hold E at a
// table). A guest within 10 m takes one of its card players' seats: the host's game sends that guest the hand as
// that seat sees it, holds the seat's action inbox in poker_sp empty on its turns (so the card player's AI choice
// never goes through; the dealer waits) and writes the guest's move there instead (src/Mod/GamePoker.cs). The guest
// plays the seat's table chips, not its own money. Pure: the messages, the guest's options, the host's per-frame gate.
//
// MESSAGES (NoticeKind.GamePoker), one line each, space-separated:
//   table <x> <y> <z> <free>          host -> all, every 3 s while it plays the game's poker: where it sits and how many
//                                     card players' seats a guest could take
//   join                              guest -> host
//   seat <n>                          host -> guest: the seat it plays (poker_sp's 0..5); -1: none for you
//   state <turn> <you> <bb> <board> <s0> ... <s5>   host -> its guest, on change (at most every 0.3 s, again every 3 s):
//                                     board 5 cards, "-" for none, comma-separated ("Qh,Kh,7h,-,-"); each seat
//                                     "<stack>:<bet>:<status>:<cards>" in cents (status: 0 in, 1 folded, 2 shown/all in,
//                                     -1 empty), cards "Ah7d" for the guest's own seat or a shown hand, else "-"
//   act <type> <amount>               guest -> host on its turn: 3 call, 4 check, 5 fold, 2 raise (amount: cents over the call)
//   leave                             guest -> host (got up) or host -> guest (the game's poker ended)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RDR2Coop.Net
{
    public static class GamePokerMsg
    {
        public const int Seats = 6, Raise = 2, Call = 3, Check = 4, Fold = 5, MaxCents = 10_000_000;

        /// <summary>poker_sp's card ({rank 2-14, suit 0-3 hearts diamonds spades clubs}) as text ("Ah"), or "-".</summary>
        public static string Card(int rank, int suit) => rank >= 2 && rank <= 14 && suit >= 0 && suit <= 3 ? "--23456789TJQKA"[rank] + "hdsc"[suit].ToString() : "-";

        /// <summary>The card's face name in the game's card_set textures ("hearts_a").</summary>
        public static string Face(string card)
        {
            if (card == null || card.Length != 2) return null;
            int r = "23456789TJQKA".IndexOf(card[0]), s = "hdsc".IndexOf(card[1]);
            if (r < 0 || s < 0) return null;
            return new[] { "hearts", "diamonds", "spades", "clubs" }[s] + "_" + new[] { "2", "3", "4", "5", "6", "7", "8", "9", "10", "j", "q", "k", "a" }[r];
        }

        static bool IsCard(string c) => c == "-" || Face(c) != null;
        static bool Num(string s, out int v) => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);

        public struct Seat { public int Stack, Bet, Status; public string Cards; }

        public static string State(int turn, int you, int bigBlind, IList<string> board, IList<Seat> seats) =>
            $"state {turn} {you} {bigBlind} {string.Join(",", board)} " + string.Join(" ", seats.Select(x => $"{x.Stack}:{x.Bet}:{x.Status}:{x.Cards ?? "-"}"));

        /// <summary>Parses a state line's fields (after "state"). False for anything odd (it comes over the network).</summary>
        public static bool ParseState(string rest, out int turn, out int you, out int bigBlind, out List<string> board, out List<Seat> seats)
        {
            turn = you = bigBlind = 0; board = null; seats = null;
            var t = (rest ?? "").Split(' ');
            if (t.Length != 4 + Seats || !Num(t[0], out turn) || !Num(t[1], out you) || !Num(t[2], out bigBlind)
                || turn < -1 || turn >= Seats || you < 0 || you >= Seats || bigBlind < 0 || bigBlind > MaxCents) return false;
            board = t[3].Split(',').ToList();
            if (board.Count != 5 || !board.All(IsCard)) return false;
            seats = new List<Seat>();
            for (int i = 0; i < Seats; i++)
            {
                var p = t[4 + i].Split(':');
                if (p.Length != 4 || !Num(p[0], out int stack) || !Num(p[1], out int bet) || !Num(p[2], out int status)
                    || stack < 0 || stack > MaxCents || bet < 0 || bet > MaxCents || status < -1 || status > 3) return false;
                if (p[3] != "-" && !(p[3].Length == 4 && Face(p[3].Substring(0, 2)) != null && Face(p[3].Substring(2)) != null)) return false;
                seats.Add(new Seat { Stack = stack, Bet = bet, Status = status, Cards = p[3] });
            }
            return true;
        }

        public static bool ParseAct(string rest, out int type, out int amount)
        {
            type = amount = 0;
            var t = (rest ?? "").Split(' ');
            return t.Length == 2 && Num(t[0], out type) && Num(t[1], out amount) && (type == Raise || type == Call || type == Check || type == Fold)
                && amount >= 0 && amount <= MaxCents && (type == Raise) == (amount > 0);
        }
    }

    /// <summary>The guest's view: the latest state, its options on its turn, the panel.</summary>
    public sealed class GamePokerView
    {
        public int Turn = -1, You = -1, BigBlind;
        public List<string> Board = new List<string>();
        public List<GamePokerMsg.Seat> Seats = new List<GamePokerMsg.Seat>();
        public int Version { get; private set; }

        public bool OnState(string rest)
        {
            if (!GamePokerMsg.ParseState(rest, out int turn, out int you, out int bb, out var board, out var seats)) return false;
            Turn = turn; You = you; BigBlind = bb; Board = board; Seats = seats;
            Version++;
            return true;
        }

        public bool MyTurn => You >= 0 && Turn == You && Seats.Count == GamePokerMsg.Seats && Seats[You].Status == 0;
        int MaxBet => Seats.Count == 0 ? 0 : Seats.Max(x => x.Bet);
        public int ToCall => MyTurn ? Math.Max(0, MaxBet - Seats[You].Bet) : 0;
        public int Pot => Seats.Sum(x => x.Bet);   // shortcut: this round's bets only (earlier rounds' pot isn't in the map yet)

        /// <summary>Our moves now: (label, "type amount"). Empty when it isn't our turn.</summary>
        public List<(string label, string action)> Options()
        {
            var o = new List<(string, string)>();
            if (!MyTurn) return o;
            var me = Seats[You];
            int toCall = ToCall, left = me.Stack - toCall;
            if (toCall == 0) o.Add(("Check", $"{GamePokerMsg.Check} 0"));
            else o.Add((me.Stack <= toCall ? $"Call all in ({Dollars(me.Stack)})" : $"Call {Dollars(toCall)}", $"{GamePokerMsg.Call} 0"));
            int bb = Math.Max(1, BigBlind);
            foreach (var (what, by) in new[] { ("min", bb), ("2x", 2 * bb), ("pot", Math.Max(bb, Pot + toCall)) })
                if (by < left && !o.Any(x => x.Item2 == $"{GamePokerMsg.Raise} {by}"))
                    o.Add(($"Raise {Dollars(by)} more ({what})", $"{GamePokerMsg.Raise} {by}"));
            if (left > 0) o.Add(($"All in ({Dollars(me.Stack)})", $"{GamePokerMsg.Raise} {left}"));
            if (toCall > 0) o.Add(("Fold", $"{GamePokerMsg.Fold} 0"));
            return o;
        }

        public List<(string text, int tone)> Lines(Func<int, string> seatName)
        {
            var l = new List<(string, int)>();
            if (Seats.Count != GamePokerMsg.Seats) { l.Add(("Waiting for the hand...", 2)); return l; }
            l.Add(("Board: " + (Board.All(c => c == "-") ? "nothing yet" : string.Join(" ", Board.Where(c => c != "-"))), 0));
            for (int i = 0; i < GamePokerMsg.Seats; i++)
            {
                var s = Seats[i];
                if (s.Status == -1) continue;
                string who = i == You ? "You" : seatName(i);
                string state = s.Status == 1 ? "folded" : s.Status == 2 ? "shown" : i == Turn ? "to act" : "";
                string cards = s.Cards != "-" && i != You ? " " + s.Cards.Substring(0, 2) + " " + s.Cards.Substring(2) : "";
                l.Add(($"{who}: {Dollars(s.Stack)}{(s.Bet > 0 ? ", bet " + Dollars(s.Bet) : "")}{(state.Length > 0 ? ", " + state : "")}{cards}", i == You ? 1 : s.Status == 1 ? 2 : 0));
            }
            if (MyTurn) l.Add((ToCall > 0 ? $"Your move: {Dollars(ToCall)} to call" : "Your move", 1));
            return l;
        }

        public static string Dollars(int cents) => "$" + cents / 100 + "." + (cents % 100).ToString("00");
    }

    /// <summary>
    /// The host's per-frame gate for one guest's seat: what goes into its poker_sp inbox this frame. On the seat's turn
    /// the inbox is held empty (type 0: the dealer waits and the card player's AI choice is wiped) until the guest's move
    /// comes, then that move, every frame until the turn passes. A guest silent for TimeoutMs folds.
    /// </summary>
    public sealed class SeatGate
    {
        public const int TimeoutMs = 40_000;
        public readonly int Seat;
        (int type, int amount)? _move;
        long _since = -1;
        public bool TimedOut { get; private set; }
        public SeatGate(int seat) { Seat = seat; }

        /// <summary>A move from the guest: only on its turn (the caller checks), the first one counts.</summary>
        public void Move(int type, int amount) { if (_since >= 0 && _move == null) _move = (type, amount); }

        public bool Waiting => _since >= 0 && _move == null;

        /// <summary>Every frame: the (type, amount) to write into the seat's inbox, or null (not its turn: leave it alone).</summary>
        public (int type, int amount)? Frame(int turn, long now)
        {
            if (turn != Seat) { _since = -1; _move = null; TimedOut = false; return null; }
            if (_since < 0) _since = now;
            if (_move == null && now - _since >= TimeoutMs) { _move = (GamePokerMsg.Fold, 0); TimedOut = true; }
            return _move ?? (0, 0);
        }
    }
}
