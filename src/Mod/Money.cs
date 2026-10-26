// The player's cash (0.9.0), for the table games, giving money and the contests. The game
// keeps it in cents (the poker script's stakes and winnings are ints the HUD shows as
// dollars and cents). Every feature uses this, never the natives: it caps a single move
// at $10,000 and refuses to take what the player doesn't have, so a bad or hostile
// message can't make a fortune or a debt.
using System;
using RDR2.Native;

namespace RDR2Coop.Mod
{
    static class Money
    {
        public const int MaxCents = 10_000_00;   // one move, at most
        // The game's reason for a cash change: the hash the game's own poker (poker_sp) passes when it pays winnings.
        const uint Reason = 0x2CD419DC;

        public static int Cents => MONEY._MONEY_GET_CASH_BALANCE();

        /// <summary>Cents Give added that the mission party hasn't counted yet: money the mod moved is never mission money.</summary>
        public static int Given;

        /// <summary>Takes cents from the player's cash: false when they don't have it (nothing taken).</summary>
        public static bool Take(int cents)
        {
            if (cents <= 0 || cents > MaxCents || cents > Cents) return false;
            return MONEY._MONEY_DECREMENT_CASH_BALANCE(cents);
        }

        /// <summary>Adds cents to the player's cash.</summary>
        public static bool Give(int cents)
        {
            if (cents <= 0 || cents > MaxCents) return false;
            bool ok = MONEY._MONEY_INCREMENT_CASH_BALANCE(cents, Reason);
            if (ok) Given += cents;
            return ok;
        }

        public static string Dollars(int cents) => (cents < 0 ? "-$" : "$") + Math.Abs(cents) / 100 + "." + (Math.Abs(cents) % 100).ToString("00");
    }
}
