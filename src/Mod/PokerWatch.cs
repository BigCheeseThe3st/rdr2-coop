// A recorder for the game's own poker (0.9.0-beta.8.4, [Testing] PokerTrace=Yes): while poker_sp runs, its statics are
// read twice a second (ScriptMemory) and every change goes to scripts\RDR2Coop.poker.log, labelled from the 1491.50
// map of the script (seats at 1444 + 56 per seat, the board at 1420, turn 1410, phase 1939, the per-seat action inbox
// at 1917 + 2 per seat, the deck at 2011), plus one decoded line of the table whenever the turn, phase or an inbox
// changes. Read only: nothing is written to the game. It's the groundwork for guests taking an NPC's seat.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using RDR2.Native;

namespace RDR2Coop.Mod
{
    static class PokerWatch
    {
        public static bool On;
#if NET48
        public static void Tick(long now) { }   // the game's script memory needs the mod's own loader
        public static void FrameTick() { }
        public static string ArmFold() => "Co-op: needs the mod's own loader";
        public static string ArmDeck() => "Co-op: needs the mod's own loader";
        public static string SendAway() => "Co-op: needs the mod's own loader";
#else
        public const uint Script = 0x012A452C;   // joaat("poker_sp")
        const int Statics = 4844, EveryMs = 500, NoisyAfter = 40, MaxLinesPerRead = 300;
        const int Seat0 = 1444, SeatStride = 56, Seats = 6, Board = 1420, Turn = 1410, Phase = 1939, Inbox = 1917, Deck = 2011, DeckNext = 2115;

        static ulong[] _last;
        static readonly ulong[] _now = new ulong[Statics];
        static readonly Dictionary<int, int> _changes = new Dictionary<int, int>();
        static readonly HashSet<int> _noisy = new HashSet<int>();
        static long _readMs = -EveryMs;
        static int _thread;
        static string _lastSummary;
        static string _path;
        static int _failures;

        // ---- the first takeover test ([Keys] PokerForceFold=): the next card player to act folds, by the mod ----
        // Every action reaches the dealer through its seat's inbox (type at 1917 + 2 * seat, amount after it; 5 = fold);
        // the card player's AI writes its choice there on the first frame of its turn and the dealer takes it on the
        // next. Writing "fold" there every frame of that turn replaces the AI's choice: the same path a guest's move
        // will take. Only those two slots of that one seat, only while it's that seat's turn, then it stops.
        const int HumanSeat = 137;   // uLocal_14.f_114.f_9: the seat the player plays (the map)
        static bool _armed;
        static int _forced = -1, _forcedFrames;

        public static string ArmFold()
        {
            if (SCRIPTS.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(Script) == 0) return "Co-op test: sit at a poker table first";
            if (!ScriptMemory.Check(GameDir())) return "Co-op test: script memory is off (RDR2Coop.log says why)";
            _armed = true; _forced = -1; _forcedFrames = 0;
            var one = new ulong[1];
            ScriptMemory.Read(Script, HumanSeat, one);
            Write($"TEST armed: the next card player to act folds (the player's seat reads {(int)(uint)one[0]})");
            return "Co-op test: the next card player to act will fold";
        }

        /// <summary>Every tick (every frame) while armed.</summary>
        // ---- the deck test ([Keys] PokerDeckTest=): the next hand is dealt from a deck the mod writes ----
        // The dealer shuffles (state 1), then stops at a sync point before it deals; the deck is 52 {rank, suit} pairs
        // at 2011 (2010 holds its size), the next card's index at 2115. Hole cards go two at a time to seats 0..5 in
        // order, then the board. Written while the state is 1-3 and nothing is drawn yet, so the dealer deals ours.
        const int DealerState = 1939, SyncPoint = 1940, DeckAt = 2011;
        static bool _deckArmed, _deckWritten;

        public static string ArmDeck()
        {
            if (SCRIPTS.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(Script) == 0) return "Co-op test: sit at a poker table first";
            if (!ScriptMemory.Check(GameDir())) return "Co-op test: script memory is off (RDR2Coop.log says why)";
            _deckArmed = true; _deckWritten = false;
            Write("TEST deck armed: the next hand is dealt from the mod's deck (As Ah first, then spades, hearts, diamonds, clubs high to low)");
            return "Co-op test: next hand, you should get the ace of spades and the ace of hearts";
        }

        // As Ah first, then every other card: spades, hearts, diamonds, clubs, ace down to two (suit 0 hearts 1 diamonds 2 spades 3 clubs).
        static ulong[] TestDeck()
        {
            var cards = new List<(int rank, int suit)> { (14, 2), (14, 0) };
            foreach (int suit in new[] { 2, 0, 1, 3 })
                for (int rank = 14; rank >= 2; rank--)
                    if (!cards.Contains((rank, suit))) cards.Add((rank, suit));
            return cards.SelectMany(c => new[] { (ulong)c.rank, (ulong)c.suit }).ToArray();
        }

        static void DeckFrame()
        {
            if (!_deckArmed) return;
            var two = new ulong[1];
            if (ScriptMemory.Read(Script, DealerState, two) <= 0) { _deckArmed = false; return; }
            int state = (int)(uint)two[0];
            ScriptMemory.Read(Script, DeckNext, two);
            int next = (int)(uint)two[0];
            if (!_deckWritten)
            {
                if (state < 1 || state > 3 || next != 0) return;
                ScriptMemory.Read(Script, SyncPoint, two);
                if ((int)(uint)two[0] == 0) return;   // the dealer hasn't stopped at its post-shuffle sync point yet: a shuffle could still come
                var size = new ulong[1];
                ScriptMemory.Read(Script, DeckAt - 1, size);
                if ((int)(uint)size[0] != 52) { Write($"TEST deck: the deck's size slot reads {(int)(uint)size[0]}, not 52: not written"); _deckArmed = false; return; }
                ScriptMemory.Write(Script, DeckAt, TestDeck());
                _deckWritten = true;
                Write($"TEST deck written (dealer state {state}, nothing drawn yet): seat 0 should get As Ah");
                return;
            }
            if (next < 4) return;   // wait until the hole cards are out
            var seat = new ulong[12];
            ScriptMemory.Read(Script, Seat0, seat);
            Write($"TEST deck result: seat 0 was dealt {Card2((int)(uint)seat[8], (int)(uint)seat[9])} {Card2((int)(uint)seat[10], (int)(uint)seat[11])} ({((int)(uint)seat[8] == 14 && (int)(uint)seat[9] == 2 && (int)(uint)seat[10] == 14 && (int)(uint)seat[11] == 0 ? "OUR DECK WAS DEALT" : "not ours")})");
            _deckArmed = false;
        }

        // ---- the send-away test ([Keys] PokerSendAway=): a random card player leaves after this hand, the game's way ----
        // Each seat's record (uLocal_14.f_114.f_2147: 6 x 13 slots from 2276): +0 seated, +2 hands played (counted at
        // every hand's end; confirmed 26 Oct), +4 hands before it may leave (8-13 at random), +5 its share of the chip
        // leader's stack (float). At a hand's end the game lets a card player go when there are 3 or more players, the
        // seat isn't the player's, it has the table's minimum, it isn't the chip leader (share < 1) and hands played >=
        // hands before leaving. Setting the last to 0 sends it off with the game's own get-up and walk-off.
        const int Records = 2276, RecordSize = 13;

        public static string SendAway()
        {
            if (SCRIPTS.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(Script) == 0) return "Co-op test: sit at a poker table first";
            if (!ScriptMemory.Check(GameDir())) return "Co-op test: script memory is off (RDR2Coop.log says why)";
            var rec = new ulong[RecordSize * Seats];
            var one = new ulong[1];
            if (ScriptMemory.Read(Script, Records, rec) <= 0 || ScriptMemory.Read(Script, HumanSeat, one) <= 0) return "Co-op test: couldn't read the table";
            int human = (int)(uint)one[0];
            var seated = Enumerable.Range(0, Seats).Where(i => i != human && (uint)rec[RecordSize * i] != 0).ToList();
            float Share(int i) => BitConverter.ToSingle(BitConverter.GetBytes((uint)rec[RecordSize * i + 5]), 0);
            var candidates = seated.Where(i => Share(i) < 1f).ToList();   // the chip leader never leaves
            if (candidates.Count == 0) return "Co-op test: no card player can leave now (only the chip leader, or nobody)";
            int seat = candidates[new Random().Next(candidates.Count)];
            int played = (int)(uint)rec[RecordSize * seat + 2], before = (int)(uint)rec[RecordSize * seat + 4];
            ScriptMemory.Write(Script, Records + RecordSize * seat + 4, new ulong[] { 0 });
            Write($"TEST send-away: seat {seat} (hands played {played}, was leaving after {before}, share {Share(seat):0.00}) set to leave after this hand");
            return $"Co-op test: card player {seat + 1} will leave after this hand";
        }

        static string Card2(int rank, int suit) => rank >= 2 && rank <= 14 && suit >= 0 && suit <= 3 ? "--23456789TJQKA"[rank] + "hdsc"[suit].ToString() : "?";

        public static void FrameTick()
        {
            DeckFrame();
            if (!_armed) return;
            var one = new ulong[1];
            if (ScriptMemory.Read(Script, Turn, one) <= 0) { _armed = false; Write("TEST stopped: poker_sp is gone"); return; }
            int turn = (int)(uint)one[0];
            ScriptMemory.Read(Script, HumanSeat, one);
            int human = (int)(uint)one[0];
            if (_forced >= 0 && turn != _forced)
            {
                var seat = new ulong[8];
                ScriptMemory.Read(Script, Seat0 + SeatStride * _forced, seat);
                int status = (int)(uint)seat[6];
                Write($"TEST done: seat {_forced}'s turn passed after {_forcedFrames} frames of 'fold' in its inbox; its status is now {status} ({(status == 1 ? "FOLDED: the takeover works" : "not folded: the AI's choice went through")})");
                _armed = false;
                return;
            }
            if (turn < 0 || turn >= Seats || turn == human) return;   // nobody's, or the player's own turn: wait
            if (_forced < 0) { _forced = turn; Write($"TEST: seat {turn}'s turn: writing 'fold' into its inbox every frame"); }
            var inbox = new ulong[] { 5, 0 };
            ScriptMemory.Write(Script, Inbox + 2 * turn, inbox);
            _forcedFrames++;
        }
        static bool _tailWarned;
        const int Chunk = 4096;   // the first loader caps one read at 4096 slots
        static readonly ulong[] _head = new ulong[Chunk], _tail = new ulong[Statics - Chunk];

        // All 4844 statics into _now: slots 0..4095, then 4096..4843 (the script's start-up arguments are 4810..4843).
        static int ReadAll()
        {
            int a = ScriptMemory.Read(Script, 0, _head);
            if (a <= 0) return a;
            int b = ScriptMemory.Read(Script, Chunk, _tail);
            Array.Copy(_head, _now, Chunk);
            if (b == a) Array.Copy(_tail, 0, _now, Chunk, _tail.Length);
            else if (!_tailWarned) { _tailWarned = true; Write($"poker_sp: slots {Chunk}..{Statics - 1} didn't read ({b}): recording 0..{Chunk - 1} only"); }
            return a;
        }

        public static void Tick(long now)
        {
            if (!On || now - _readMs < EveryMs) return;
            _readMs = now;
            if (SCRIPTS.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(Script) == 0)
            {
                if (_thread != 0) { Write($"poker_sp (thread {_thread}) ended"); _thread = 0; _last = null; }
                return;
            }
            if (_path == null) _path = Path.Combine(GameDir(), "scripts", "RDR2Coop.poker.log");
            if (!ScriptMemory.Check(GameDir())) return;
            int thread = ReadAll();
            if (thread <= 0) { if (thread < 0 && _failures++ < 5) Write($"reading poker_sp failed ({thread})"); return; }
            var sb = new StringBuilder();
            if (thread != _thread || _last == null)
            {
                _thread = thread;
                _changes.Clear(); _noisy.Clear();
                sb.AppendLine($"=== poker_sp thread {thread}: started (or first seen); its statics from slot 1404, then its start-up arguments 4810..4843:");
                for (int i = 1404; i < 2120; i += 8) sb.AppendLine($"  {i,4}: " + string.Join(" ", Enumerable.Range(i, 8).Select(k => V(_now[k]))));
                for (int i = 4810; i < Statics; i += 8) sb.AppendLine($"  {i,4}: " + string.Join(" ", Enumerable.Range(i, Math.Min(8, Statics - i)).Select(k => V(_now[k]))));
                _last = (ulong[])_now.Clone();
            }
            else
            {
                int lines = 0;
                for (int i = 0; i < Statics; i++)
                {
                    if (_now[i] == _last[i]) continue;
                    _changes[i] = _changes.TryGetValue(i, out int c) ? c + 1 : 1;
                    if (_noisy.Contains(i)) continue;
                    if (_changes[i] > NoisyAfter) { _noisy.Add(i); sb.AppendLine($"  {i,4} {Label(i)} changes all the time: not logged any more"); continue; }
                    if (lines++ < MaxLinesPerRead) sb.AppendLine($"  {i,4} {Label(i),-14} {V(_last[i])} -> {V(_now[i])}");
                }
                if (lines > MaxLinesPerRead) sb.AppendLine($"  ... {lines - MaxLinesPerRead} more changes not shown");
                Array.Copy(_now, _last, Statics);
            }
            string summary = Summary();
            if (summary != _lastSummary) { _lastSummary = summary; sb.AppendLine("  TABLE " + summary); }
            if (sb.Length > 0) Write(sb.ToString().TrimEnd());
        }

        static int I(int slot) => unchecked((int)(uint)_now[slot]);
        static string V(ulong v) { int i = unchecked((int)(uint)v); return (v >> 32) != 0 ? "0x" + v.ToString("X") : i.ToString(); }

        static string Card(int slot)
        {
            int rank = I(slot), suit = I(slot + 1);
            if (rank < 2 || rank > 14 || suit < 0 || suit > 3) return rank == 0 && suit == 0 ? "--" : $"?{rank}/{suit}";
            return "--23456789TJQKA"[rank] + "hdsc"[suit].ToString();
        }

        static string Summary()
        {
            var seats = Enumerable.Range(0, Seats).Select(s =>
            {
                int b = Seat0 + SeatStride * s;
                return $"s{s}[stack {I(b + 2)} bet {I(b + 4)} st {I(b + 6)} {Card(b + 8)} {Card(b + 10)} in {I(Inbox + 2 * s)}/{I(Inbox + 2 * s + 1)}]";
            });
            string board = string.Join(" ", Enumerable.Range(0, 5).Select(k => Card(Board + 2 * k)));
            return $"phase {I(Phase)} turn {I(Turn)} board {board} deck-next {I(DeckNext)} | " + string.Join(" ", seats);
        }

        static string Label(int i)
        {
            if (i >= Seat0 && i < Seat0 + SeatStride * Seats) return $"seat{(i - Seat0) / SeatStride}+{(i - Seat0) % SeatStride}";
            if (i >= Board && i < Board + 10) return $"board{(i - Board) / 2}{((i - Board) % 2 == 0 ? "rank" : "suit")}";
            if (i == Turn) return "turn";
            if (i == Phase) return "phase";
            if (i >= Inbox && i < Inbox + 2 * Seats) return $"inbox{(i - Inbox) / 2}{((i - Inbox) % 2 == 0 ? "type" : "amount")}";
            if (i >= Deck && i < DeckNext) return "deck" + (i - Deck);
            if (i == DeckNext) return "deck-next";
            return "";
        }

        static string GameDir() => ScriptMemory.GameDir();

        static void Write(string text)
        {
            try
            {
                if (_path == null) _path = Path.Combine(GameDir(), "scripts", "RDR2Coop.poker.log");
                var f = new FileInfo(_path);
                if (f.Exists && f.Length > 20_000_000) return;   // shortcut: stops at 20 MB, clear the file to record more
                File.AppendAllText(_path, DateTime.Now.ToString("HH:mm:ss.f ") + text + Environment.NewLine);
            }
            catch (Exception) { }
        }
#endif
    }
}
