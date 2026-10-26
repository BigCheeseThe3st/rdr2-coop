// Table games (0.9.0): what one game of cards or dice at a mod table is (poker, blackjack, dominoes, liar's dice,
// the hidden sample "highcard"). Pure, so the tests can run whole hands. src/Net/Table.cs runs the table around it
// (seats, the cash each player brings, the messages, the settlement); src/Mod/Tables.cs is the game side.
//
// One object per game instance. On the host of a table (the player who opened it) the framework makes TWO: one runs
// the rules (Begin/Act/Tick/Public/Private/Settlement), the other is the host's own view, fed with what the rules
// publish, exactly as every other seat's view is. So a game never mixes the rules' secrets with a view.
// Seats in the host-side calls are indexes 0..n-1 into the list Begin got (the players in this hand, in seat order);
// the client side gets player ids (byte) and finds its seat in the public state, which therefore must carry the ids.
using System;
using System.Collections.Generic;

namespace RDR2Coop.Net
{
    public abstract class TableGame
    {
        public const int TurnMs = 45_000;   // a seat that doesn't move in time gets DefaultAction

        public abstract string Name { get; }        // the registry's name: lowercase letters, e.g. "poker"
        public virtual string Title => char.ToUpperInvariant(Name[0]) + Name.Substring(1);   // for the menu and the panel
        public virtual int MinPlayers => 2;         // hands start when this many seated players bring the stakes
        public virtual bool HostIsHouse => false;   // seat 0 is the house: no hand is dealt while the host hasn't the stakes

        // ---- host side: the rules (only the table's host runs these, on its rules object) ----

        /// <summary>A new hand: the players (ids, seat order), the stakes (cents: the unit bet) and the cash each brought
        /// (cents, at least the stakes, at most 50 x stakes: the most that seat can lose in this hand).</summary>
        public abstract void Begin(IList<byte> seats, int stakes, IList<int> cash, Random rng);

        /// <summary>A move from a seat (the action text of one of its Options). False when it isn't legal now.</summary>
        public abstract bool Act(int seat, string action);

        /// <summary>The seat is out of this hand for good (left the table, walked away, disconnected, or its
        /// DefaultAction was refused): it can't win any more, what it has put in stays in, it's never its turn again.</summary>
        public abstract void Forfeit(int seat);

        /// <summary>The seat whose move it is (the turn timer runs for it), or -1 (nobody's, or the hand is over).</summary>
        public virtual int Turn => -1;

        /// <summary>The move the host makes for a seat whose 45 s ran out: check, fold, stand, pass...</summary>
        public abstract string DefaultAction(int seat);

        /// <summary>The hand as everyone may see it, at most 900 bytes, no newlines (sent whenever it changes).</summary>
        public abstract string Public();

        /// <summary>What only that seat may see (its cards), or null. No newlines.</summary>
        public virtual string Private(int seat) => null;

        /// <summary>The hand is finished: Settlement() says who won what.</summary>
        public abstract bool HandOver { get; }

        /// <summary>Net cents per seat for the finished hand: + won, - lost; they must add up to zero and no seat may
        /// lose more than the cash it brought, or the framework voids the hand (nobody pays).</summary>
        public abstract IList<int> Settlement();

        /// <summary>The table is finished for good (no next hand).</summary>
        public virtual bool Over => false;

        /// <summary>Every tick on the host. This one is the turn timer: whenever Turn or Public() changes the seat to move
        /// gets a fresh 45 s; when they run out it plays DefaultAction for it, and forfeits it if that's refused.
        /// A game with timing of its own overrides it (and may call base.Tick).</summary>
        public virtual void Tick(long now)
        {
            if (HandOver) return;
            int turn = Turn;
            string state = Public();
            if (turn != _timedSeat || state != _timedState) { _timedSeat = turn; _timedState = state; _timedMs = now; return; }
            if (turn < 0 || now - _timedMs < TurnMs) return;
            if (!Act(turn, DefaultAction(turn))) Forfeit(turn);
            _timedSeat = -2;   // whoever moves next, even the same seat, gets a fresh 45 s
        }

        int _timedSeat = -2;
        string _timedState;
        long _timedMs;

        // ---- client side: a view (every seat, the host too, runs these on its own view object) ----

        /// <summary>The host's latest Public(). Parse it defensively: it comes over the network.</summary>
        public abstract void OnPublic(string state);

        /// <summary>The host's latest Private(our seat). Parse it defensively.</summary>
        public virtual void OnPrivate(string secret) { }

        /// <summary>Our moves now: (what the pick-list shows, the action sent to the host). Empty: not our move.</summary>
        public abstract IList<(string label, string action)> Options(byte me);

        /// <summary>The panel, under the framework's title line: (text, tone) with tones 0 white, 1 gold (mine),
        /// 2 grey, 3 red (warning), 4 green (won). Eight lines or fewer.</summary>
        public abstract IList<(string text, int tone)> Lines(byte me, Func<byte, string> nameOf);
    }

    // Every table game by name. The game registers itself from CoopScript.Start (Tables.Register, which also checks
    // its [Games] switch); FakePlayer's tools/TableBots.cs registers the games it has bots for. Both then make their
    // instances here.
    public static class TableRegistry
    {
        static readonly Dictionary<string, Func<TableGame>> Makers = new Dictionary<string, Func<TableGame>>();

        public static void Register(string name, Func<TableGame> make) { if (TableMsg.IsGameName(name) && make != null) Makers[name] = make; }
        public static bool Has(string name) => name != null && Makers.ContainsKey(name);
        public static TableGame Make(string name) => Has(name) ? Makers[name]() : null;
        public static IEnumerable<string> Names => Makers.Keys;
    }
}
