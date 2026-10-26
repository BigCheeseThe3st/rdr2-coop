// Card and table games (0.8.0, [Gameplay] MiniGames): poker, blackjack, dominoes and five
// finger fillet. Each is a game script of its own (poker_sp, bjack_sp, dominoes_sp,
// fillet_sp) that runs from the moment you sit down until you get up, in town and in camp
// alike: the game's short_update, or the camp's act_gen_* activity, starts it for the seat
// you take, while the *_launch_sp scripts only keep the table and its players there while
// you're near. While one runs here co-op stands back: nothing of ours fades, freezes, moves
// or hurts us (the mission party, going down, the area owner's NPCs' hits: the games make
// us invincible at the table anyway, F9, Y/N), the host's enRage sends no ambush, the host's
// winnings aren't shared as mission money, the games' own fades aren't sent as ours, and
// our subtitles wait until we're up (answers to our keys still show). The table's
// players are the launchers' own peds: MirrorWorld never clears them, and the area's owner
// doesn't send its own table players to the others (their copies would sit on top of the
// others' own; every game seats its own). Partners hear which game we play (TableRules):
// their stand-in of us sits where we sit, and F10 says so.
using System.Collections.Generic;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class TableGames
    {
        const int CheckMs = 200;    // soon after we sit down, before the mission party could fade or fetch us
        const int AfterMs = 3000;   // still standing back a moment after: the cash-out and the fade back in come last

        public static bool Busy { get; private set; }   // we play at a table, or just got up
        public static bool Answering;                     // one of our keys was just pressed: its answer shows at once
        static string _held;                              // the last subtitle that waited
        static uint _cardPlayers, _chair;

        // In TableRules.Games' order.
        readonly uint[] _scripts = { MISC.GET_HASH_KEY("poker_sp"), MISC.GET_HASH_KEY("bjack_sp"), MISC.GET_HASH_KEY("dominoes_sp"), MISC.GET_HASH_KEY("fillet_sp") };
        readonly TablePlayers _players = new TablePlayers();
        readonly Dictionary<byte, int> _sitting = new Dictionary<byte, int>();   // player -> their stand-in we sat down
        string _game, _sent;
        long _checkMs = -CheckMs, _sentMs, _busyUntil;

        // Every tick: which game runs here (checked five times a second), and the word to the others.
        public void Tick(ServerLink link, long now)
        {
            Answering = false;
            if (!Sync.MiniGames) return;
            if (now - _checkMs >= CheckMs)
            {
                _checkMs = now;
                string game = null;
                for (int i = 0; i < _scripts.Length && game == null; i++)
                    if (SCRIPTS.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(_scripts[i]) > 0) game = TableRules.Games[i];
                if (game != _game) CoopScript.Log(game != null ? $"at the {game} table: co-op stands back" : $"up from the {_game} table");
                _game = game;
            }
            if (_game != null) _busyUntil = now + AfterMs;
            bool busy = now < _busyUntil;
            if (Busy && !busy && _held != null) { RDR2.UI.Screen.DisplaySubtitle(_held); _held = null; }
            Busy = busy;
            string send = TableRules.Due(_game, ref _sent, ref _sentMs, now);
            if (send != null) link.SendNotice(Protocol.Broadcast, NoticeKind.Activity, send);
        }

        // Our subtitles: true = it waits while we play (the last one shows when we're up).
        public static bool Hold(string text)
        {
            if (!Busy || Answering) return false;
            _held = text;
            return true;
        }

        // Every partner's Activity notice comes here first. False: it only repeats their table game.
        public bool OnNotice(byte from, string text, long now) => Sync.MiniGames && _players.Heard(from, text, now);

        // For F10: " (playing poker)" for us or a partner at a table, else "".
        public string Doing(byte id, byte me, long now) => TableRules.Label(id == me ? _game : _players.Of(id, now));

        // Their stand-in sits where they sit while they play at a table (the same chair is there in
        // our game): true while it does, so it isn't steered meanwhile.
        public bool Sit(byte id, Puppet p, PlayerState s, long now)
        {
            if (!Sync.MiniGames) return false;
            bool atTable = _players.Of(id, now) != null;
            bool was = _sitting.TryGetValue(id, out int ped) && ped == p.Handle;
            if ((!atTable && !was) || !p.Exists) return false;   // the usual case costs no natives
            var at = CoopScript.ToGame(s.Position);
            bool free = !s.Mounted && s.VehicleId == 0 && (s.Flags & (StateFlags.Downed | StateFlags.Dead)) == 0;
            bool sit = TableRules.Sits(atTable, free, s.Velocity.Length, p.Position.DistanceTo(at), was);
            if (sit == was) return sit;
            if (sit)
            {
                if (_chair == 0) _chair = MISC.GET_HASH_KEY("PROP_HUMAN_SEAT_CHAIR");
                // Placed exactly where they are, then seated there (as MirrorWorld seats copies), without the sitting-down
                // move: they sat down a moment ago. shortcut: a plain chair pose, not the card games' own; check it in game.
                ENTITY._SET_ENTITY_COORDS_AND_HEADING(p.Handle, at, s.Heading, false, false, false);
                TASK.TASK_START_SCENARIO_IN_PLACE_HASH(p.Handle, _chair, -1, false, 0, s.Heading, false);
                _sitting[id] = p.Handle;
            }
            else
            {
                TASK.CLEAR_PED_TASKS(p.Handle, false, false);   // stand up
                _sitting.Remove(id);
            }
            return sit;
        }

        // One of a card table's own players (the table launchers put them in this group).
        public static bool CardPlayer(int ped)
        {
            if (!Sync.MiniGames) return false;
            if (_cardPlayers == 0) _cardPlayers = MISC.GET_HASH_KEY("rel_minigame_player");
            return PED.GET_PED_RELATIONSHIP_GROUP_HASH(ped) == _cardPlayers;
        }

        // Partners who left are forgotten; someone new hears our game at once.
        public void OnRoster(ICollection<byte> ids)
        {
            _players.KeepOnly(ids);
            foreach (var id in new List<byte>(_sitting.Keys)) if (!ids.Contains(id)) _sitting.Remove(id);
            _sent = null;
        }

        public void Clear()
        {
            _players.Clear();
            _sitting.Clear();
            _sent = null;
        }
    }
}
