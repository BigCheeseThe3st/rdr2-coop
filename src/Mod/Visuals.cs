// What players see of each other's stand-ins (0.8.0), each part with its own switch in
// RDR2Coop.ini [Visuals], so a tester can turn off one that gives trouble and keep the rest.
using System;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    static class Visuals
    {
        public static bool Aiming = true;     // they raise their gun, and their shots show (no bullets)
        public static bool Horses = true;     // their horse stays where they leave it
        public static bool Stance = true;     // crouching, jumping, climbing, swimming, falling
        public static bool Carrying = true;   // bodies, hogtied people, pelts and carcasses they carry
        public static bool Names = true;      // their name over their head
        public static bool Wear = true;       // wet, dirty or bloody clothes
        public static bool Emotes = true;     // emotes, sitting and other actions

        public static void Load(Ini ini)
        {
            Aiming = On(ini, "Aiming"); Horses = On(ini, "Horses"); Stance = On(ini, "Stance");
            Carrying = On(ini, "Carrying"); Names = On(ini, "Names"); Wear = On(ini, "Wear"); Emotes = On(ini, "Emotes");
        }

        static bool On(Ini ini, string key) => !ini.Get("Visuals", key, "Yes").StartsWith("n", StringComparison.OrdinalIgnoreCase);
    }

    // Shared-world gameplay (0.8.0), each part with its own switch in RDR2Coop.ini [Gameplay].
    static class Sync
    {
        public static bool Markers = true;      // the host's mission markers on guests' maps
        public static bool MissionSteps = true; // a guest at one of those markers can call the host over (Y)
        public static bool OwnMissionsAsGuest = false;   // a guest's own story missions still start (normally blocked: MissionBlock)
        public static bool Explosions = true;   // dynamite and fire reach the game that owns the area
        public static bool Law = true;          // crimes count for the player who did them
        public static bool Doors = true;        // doors and broken things match
        public static bool Trains = true;       // trains players drive, and ones the mod didn't know
        public static bool MiniGames = true;    // co-op stands back while you play poker, blackjack, dominoes...

        public static void Load(Ini ini)
        {
            Markers = On(ini, "Markers"); MissionSteps = On(ini, "MissionSteps"); OwnMissionsAsGuest = ini.Get("Gameplay", "OwnMissionsAsGuest", "No").Trim().Equals("Yes", System.StringComparison.OrdinalIgnoreCase); Explosions = On(ini, "Explosions");
            Law = On(ini, "Law"); Doors = On(ini, "Doors"); Trains = On(ini, "Trains"); MiniGames = On(ini, "MiniGames");
        }

        static bool On(Ini ini, string key) => !ini.Get("Gameplay", key, "Yes").StartsWith("n", StringComparison.OrdinalIgnoreCase);
    }

    // Convenience (0.8.0), each part with its own switch in RDR2Coop.ini [Convenience].
    static class Extras
    {
        public static bool Chat = true;        // in-game text chat
        public static bool Waypoint = true;    // partners' map waypoints shown on your map
        public static bool PartyList = true;   // who's in the session, their health, who's down, how far
        public static bool RideDouble = true;  // sit behind a partner on their horse
        public static bool Ping = true;        // a 'look here' marker for your partners
        public static bool GiveMoney = true;   // hand a partner some of your cash
        public static bool Scoreboard = true;  // the session's kills, wins and winnings
        public static bool Spectate = true;    // watch a partner while you're down or dead
        public static bool EmoteMenu = true;   // the mod's emote menu (wave, point, laugh...), seen by partners

        public static void Load(Ini ini)
        {
            Chat = On(ini, "Chat"); Waypoint = On(ini, "Waypoint"); PartyList = On(ini, "PartyList"); RideDouble = On(ini, "RideDouble");
            Ping = On(ini, "Ping"); GiveMoney = On(ini, "GiveMoney"); Scoreboard = On(ini, "Scoreboard"); Spectate = On(ini, "Spectate"); EmoteMenu = On(ini, "EmoteMenu");
        }

        static bool On(Ini ini, string key) => !ini.Get("Convenience", key, "Yes").StartsWith("n", StringComparison.OrdinalIgnoreCase);
    }

    // Playing together (0.9.0): the mod's own table games and contests, each with its own switch in RDR2Coop.ini [Games].
    static class Games
    {
        public static bool Poker = true;      // Texas hold 'em against each other
        public static bool Blackjack = true;  // one dealer, everyone's hands
        public static bool Dominoes = true;   // all fives
        public static bool Dice = true;       // liar's dice
        public static bool Race = true;       // a horse race to the host's waypoint
        public static bool Duel = true;       // a quick-draw duel
        public static bool Shooting = true;   // a shooting contest: bottles, on the clock
        public static bool Hunting = true;    // a hunting contest: most points in the time

        public static void Load(Ini ini)
        {
            Poker = On(ini, "Poker"); Blackjack = On(ini, "Blackjack"); Dominoes = On(ini, "Dominoes"); Dice = On(ini, "Dice");
            Race = On(ini, "Race"); Duel = On(ini, "Duel"); Shooting = On(ini, "Shooting"); Hunting = On(ini, "Hunting");
        }

        static bool On(Ini ini, string key) => !ini.Get("Games", key, "Yes").StartsWith("n", StringComparison.OrdinalIgnoreCase);
    }
}
