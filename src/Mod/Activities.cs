// Mini-games and activities (0.8.0, [Gameplay] MiniGames). While you fish, play five finger
// fillet, poker, blackjack or dominoes, do an epilogue chore, take a hotel bath, sit in the
// barber's chair or watch a show, co-op stands back: your own game runs your area (your fish
// and opponents stay, nothing is cleared away, no partner's NPCs are copied in), the mission
// party doesn't fade or move you, the host's clock waits, and your activity's own fades don't
// black out your party. Partners are told what you're doing (NoticeKind.Activity).
// Each one is the game's own script, found running by name (the 1491.50 story scripts);
// arm wrestling and horseshoes have none in story mode. The card and table games are found and
// announced by TableGames (by name, every 5 s); Busy counts them too, and partners' notices of
// both come to OnNotice (through TableGames, which seats them).
using System.Collections.Generic;
using System.Linq;
using RDR2.Native;
using RDR2Coop.Net;
using Screen = RDR2.UI.Screen;

namespace RDR2Coop.Mod
{
    static class Activities
    {
        const int CheckEveryMs = 500;

        // The minigames the game itself lists (bjack_sp ... es_activity_building) but the table games
        // (TableGames has those), fishing_core (the game starts it whenever the rod is in hand) and the hotel bath.
        static readonly (string script, string doing)[] Known =
        {
            ("fishing_core", "is fishing"),
            ("milking_cow", "is milking a cow"),
            ("es_clean_stalls", "is mucking out the stalls"),
            ("es_activity_building", "is working on the house"),
            ("bathing", "is taking a bath"),
            ("show_movie_magic_lantern", "is at the picture show"),
        };

        // The theatre's shows: each starts once you have a ticket and are near the stage.
        static readonly string[] Shows =
        {
            "generic_show_mc", "show_nbx_bigband", "show_nbx_bigband_02", "show_nbx_bulletcatch", "show_nbx_cancan",
            "show_nbx_cancan_02", "show_nbx_escapeartist", "show_nbx_escapenoose", "show_nbx_firebreath_01",
            "show_nbx_firedance_01", "show_nbx_firedance_02", "show_nbx_flexfight", "show_nbx_oddfellows",
            "show_nbx_snakedance_01", "show_nbx_snakedance_02", "show_nbx_strongwoman", "show_nbx_sworddance_01",
        };

        static (uint hash, string doing)[] _scripts;
        static uint _barber, _barberSeat, _rod;
        static readonly ActivityState State = new ActivityState();
        static readonly RodShots Rod = new RodShots();
        static long _lastCheckMs = -CheckEveryMs;
        static readonly Dictionary<byte, BlastGate> Gates = new Dictionary<byte, BlastGate>();   // partners' notices, 5 a second at most

        // We're in one of them, or at a card table (the game's own or one of the mod's): co-op leaves our game alone.
        public static bool Busy => State.Doing.Length > 0 || TableGames.Busy || Tables.Seated;

        public static void Tick(ServerLink link, long now)
        {
            if (!Sync.MiniGames || now - _lastCheckMs < CheckEveryMs) return;
            _lastCheckMs = now;
            if (_scripts == null)
            {
                _scripts = Known.Concat(Shows.Select(n => (script: n, doing: "is watching a show")))
                                .Select(k => (MISC.GET_HASH_KEY(k.script), k.doing)).ToArray();
                _barber = MISC.GET_HASH_KEY("shop_barber");
                _barberSeat = MISC.GET_HASH_KEY("PROP_PLAYER_BARBER_SEAT");
                _rod = MISC.GET_HASH_KEY("WEAPON_FISHINGROD");
            }
            string seen = "";
            foreach (var (hash, doing) in _scripts)
                if (Running(hash)) { seen = doing; break; }
            // The barber's script runs all the while you're near the shop: in its chair a script camera shows you.
            if (seen.Length == 0 && Running(_barber) && !CAM.IS_GAMEPLAY_CAM_RENDERING()
                && TASK.DOES_SCENARIO_OF_TYPE_EXIST_IN_AREA_HASH(Peds.Position(PLAYER.PLAYER_PED_ID()), _barberSeat, 2f, false))
                seen = "is at the barber's";
            if (!State.Update(seen, now)) return;
            CoopScript.Log(State.Doing.Length > 0 ? $"activity started ({State.Doing}): co-op stands back" : "activity over");
            // Over while we sit at a table: the table's own notices carry on ("" would stand our stand-in up there).
            if (State.Doing.Length > 0 || !TableGames.Busy) link.SendNotice(Protocol.Broadcast, NoticeKind.Activity, State.Doing);
        }

        static bool Running(uint script) => SCRIPTS.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(script) > 0;

        // A partner's notice: what they're doing now ("" once it's over; a table game by its name).
        public static void OnNotice(byte from, string name, string text, long now)
        {
            if (!Sync.MiniGames) return;
            // Games send one when it changes: a flood (a modified game) never reaches the log or the screen.
            if (!Gates.TryGetValue(from, out var gate)) Gates[from] = gate = new BlastGate();
            if (!gate.Allow(now)) return;
            string doing = ActivityText.Of(text);
            CoopScript.Log(doing.Length > 0 ? $"{name} {doing}" : $"{name}'s activity is over");
            string line = $"Co-op: {ActivityText.Clean(name)} {doing}";
            if (doing.Length > 0 && !TableGames.Hold(line)) Screen.DisplaySubtitle(line);   // waits while we play at a card table
        }

        // The last word on our state before it goes out.
        public static void Outgoing(ref PlayerState s)
        {
            if (!Sync.MiniGames) return;
            Rod.Apply(ref s, _rod != 0 && s.Weapon == _rod);
            // Into the bath, into a seat: an activity's fades aren't a mission's, so the party isn't blacked out.
            if (Busy) s.Flags &= ~StateFlags.ScreenFaded;
        }
    }
}
