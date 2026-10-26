// Mod tables (0.9.0): the game side of src/Net/Table.cs. Co-op menu (F10 twice) > "Play cards or dice here" lists the
// registered games whose [Games] switch is on, then the stakes ($0.50 to $25): the table opens where you stand and you
// sit at it. Partners within 10 m get "Join <name>'s <game> table ($x)"; anyone seated gets "Leave the table" (or "Your
// move at the table" when it's theirs). On your move a pick-list opens by itself with the game's options (Esc hides
// it; the menu row brings it back; after 45 s the table moves for you). Seated players sit on the ground (the first
// sitting scenario that loads; none: they stand) and count as busy (Activities.Busy): the mission party, F9, contests
// and enRage's ambushes leave them be. You get up when you pick Leave, go over 15 m from the table, go down, start a
// mission, or the session ends; mid-hand that forfeits the hand. Money moves only by the host's settlement, each
// line checked in Table.cs, through Money (never more than the cash you brought to that hand).
// Where the panel sits, on the 1280x720 layout Ui.Panel uses: top right, its box's right edge at x 1264, first line
// at y 210, under the party list (the scoreboard and contest panels hide while you're seated, being busy); 12 lines at
// most, only while seated, never in a story mission or cutscene, the pause menu or a black screen.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2.Native;
using RDR2Coop.Net;
using LinkState = RDR2Coop.Net.LinkState;
using Screen = RDR2.UI.Screen;

namespace RDR2Coop.Mod
{
    sealed class Tables
    {
        const int Top = 210, CheckMs = 200, ForgetAdMs = 12_000, SitTryMs = 2000;
        // Sitting on the ground, as the story's camps do; the first that loads is used (shortcut: the generic male sit,
        // whoever the player is; check how it looks in game).
        static readonly string[] Sits = { "WORLD_CAMP_FIRE_SIT_GROUND_MALE_A", "WORLD_HUMAN_SIT_GROUND_MALE_A", "WORLD_HUMAN_SIT_GROUND_TIRED_MALE_A" };

        /// <summary>We sit at one of the mod's tables (Activities.Busy counts it).</summary>
        public static bool Seated { get; private set; }

        /// <summary>A game for the tables, from CoopScript.Start: one line per game.</summary>
        public static void Register(string game, Func<TableGame> make) => TableRegistry.Register(game, make);

        static bool On(string game)
        {
            switch (game) { case "poker": return Games.Poker; case "blackjack": return Games.Blackjack; case "dominoes": return Games.Dominoes; case "dice": return Games.Dice; default: return true; }
        }

        static IEnumerable<string> Playable => TableRegistry.Names.Where(On).OrderBy(n => n);

        // A table someone opened, from its open line (seats from its seats line), forgotten 12 s after the last open.
        sealed class Ad { public string Id, Game; public byte Host; public int Stakes, Seats = 1; public Vec3 At; public long HeardMs; }

        TableRun _run;   // the table we sit at (or asked to)
        readonly List<TableRun> _settling = new List<TableRun>();   // tables we got up from mid-hand: their pay may still come
        readonly Dictionary<string, Ad> _ads = new Dictionary<string, Ad>();
        readonly Dictionary<byte, BlastGate> _gates = new Dictionary<byte, BlastGate>();   // 5 notices a second per sender
        ServerLink _link;
        uint _seq = (uint)new Random().Next(1, 9999);   // a reload doesn't reuse the last ids (an id has 4 digits at most)
        Vec3 _myPos;
        long _now, _checkMs = -CheckMs;
        int _cash;
        bool _down;
        PickList _moves;
        string _movesFor, _shownMoves;   // the state and the moves (labels and actions) our list was made for
        bool _menuMove;                  // the menu row said "Your move at the table" when the menu opened
        uint _sit;
        int _sitTry = -1;
        long _sitTryMs;

        static bool Connected(ServerLink link) => link != null && link.State == LinkState.Connected;

        // ---- the co-op menu ----

        public string JoinLabel(ServerLink link)
        {
            var ad = _run == null && Connected(link) ? Near() : null;
            return ad == null ? null : $"Join {link.NameOf(ad.Host)}'s {Title(ad.Game)} table ({Money.Dollars(ad.Stakes)})";
        }

        public void JoinAct(ServerLink link)
        {
            var ad = Near();
            if (ad == null || _run != null || !Connected(link)) return;
            if (!CanSit()) return;
            _cash = Money.Cents;
            var back = _settling.Find(r => r.Id == ad.Id);   // got up from it a moment ago: the same run, or a hand could be paid twice
            if (back != null && back.Rejoin(_now)) _settling.Remove(back);
            else back = null;
            _run = back ?? TableRun.Join(link.MyId, ad.Host, ad.Id, TableRegistry.Make(ad.Game), ad.Stakes, ad.At, _now);
            CoopScript.Log($"table {ad.Id}: asking {link.NameOf(ad.Host)} for a seat ({ad.Game}, {Money.Dollars(ad.Stakes)})");
            Flush(_run, link);
        }

        public string MenuLabel(ServerLink link)
        {
            _menuMove = Connected(link) && _run != null && _run.Options.Count > 0;
            if (!Connected(link)) return null;
            if (_run == null) return Playable.Any() ? "Play cards or dice here" : null;
            return _menuMove ? "Your move at the table" : "Leave the table";
        }

        public void MenuAct(ServerLink link)
        {
            if (!Connected(link)) return;
            if (_run != null && _menuMove) { if (_run.Options.Count > 0) ShowMoves(); else Say("Co-op: the table moved on"); return; }   // never Leave for a stale row
            if (_run != null) { Leave(link, "you left the table", "closed"); return; }
            if (!CanSit()) return;
            var rows = Playable.Select(g => (Title(g), (Action)(() => PickStakes(g, link)))).ToList();
            PickList.Open("Play cards or dice here", rows, null, "Partners within 10 m join from their co-op menu");
        }

        void PickStakes(string game, ServerLink link)
        {
            var rows = TableMsg.Stakes.Select(c => (Money.Dollars(c), (Action)(() => OpenTable(game, c, link)))).ToList();
            PickList.Open($"{Title(game)}: the stakes", rows, null, "The unit bet: everyone brings up to 50 times it to each hand");
        }

        void OpenTable(string game, int stakes, ServerLink link)
        {
            if (_run != null || !Connected(link) || !TableRegistry.Has(game) || !CanSit()) return;
            _cash = Money.Cents;
            _run = TableRun.Open(link.MyId, _seq = _seq % 9999 + 1, () => TableRegistry.Make(game), stakes, _myPos, _now);
            CoopScript.Log($"table {_run.Id}: {game} at {Money.Dollars(stakes)} opened by us");
            Say($"Co-op: your {Title(game)} table is open: partners within 10 m can join from the co-op menu");
            Flush(_run, link);
        }

        // Natives only here, on a menu pick.
        bool CanSit()
        {
            int me = PLAYER.PLAYER_PED_ID();
            string why = Activities.Busy ? "finish what you're doing first" : _down ? "you're down" : MissionProbe.StoryMission || MissionProbe.InMissionScene ? "not during a mission"
                : PED.IS_PED_ON_MOUNT(me) || PED.IS_PED_IN_ANY_VEHICLE(me, false) ? "get down first" : null;
            if (why != null) Say("Co-op: " + why);
            return why == null;
        }

        Ad Near() => _ads.Values.Where(a => a.Seats < TableMsg.MaxSeats && Vec3.Distance(a.At, _myPos) <= TableRun.JoinRange)
                                 .OrderBy(a => Vec3.Distance(a.At, _myPos)).FirstOrDefault();

        // ---- every tick while connected ----

        public void Tick(ServerLink link, Vec3 myPos, Func<byte, Vec3?> posOf, bool down, bool menuOpen, long now)
        {
            _link = link; _myPos = myPos; _now = now; _down = down;
            foreach (var id in _ads.Where(kv => now - kv.Value.HeardMs > ForgetAdMs).Select(kv => kv.Key).ToList()) _ads.Remove(id);
            foreach (var s in _settling.ToList()) { s.MyCash = _cash; s.Tick(now); Settle(s); Flush(s, link); if (s.Done) { CoopScript.Log($"table {s.Id}: {s.Why}"); _settling.Remove(s); } }
            if (_run == null) return;
            if (now - _checkMs >= CheckMs)
            {
                _checkMs = now;
                _cash = Money.Cents;
                string why = Vec3.Distance(myPos, _run.At) > TableRun.LeaveRange ? "you walked away from the table" : down ? "you went down"
                    : TableGames.Busy ? "you sat at another table" : MissionProbe.StoryMission || MissionProbe.InMissionScene ? "a mission started" : null;
                if (why != null) { Leave(link, why, "host left"); return; }
                if (_run.Seated) Sit(now);
            }
            _run.MyCash = _cash;
            _run.PosOf = posOf;
            _run.Tick(now);
            Settle(_run);
            Flush(_run, link);
            if (Over()) return;
            Seated = _run.Seated;
            Moves(menuOpen);
            if (!Seated || MissionProbe.InMissionScene || HUD.IS_PAUSE_MENU_ACTIVE() || CAM.IS_SCREEN_FADED_OUT() || CAM.IS_SCREEN_FADING_OUT()) return;
            var lines = _run.Lines(link.NameOf).Take(12).Select(l => (l.text, Tone.Of(l.tone))).ToList();
            float width = Math.Min(640f, 32f + 10f * lines.Max(l => l.Item1.Length));   // ~10 px a character, as the party list reckons
            Ui.Panel(lines, width, 1264f - width, Top);
        }

        // Our move: the game's options in a pick-list, once per state (Esc hides it; the menu row brings it back).
        void Moves(bool menuOpen)
        {
            var opts = _run.Options;
            if (opts.Count == 0)
            {
                if (_moves != null && PickList.Current == _moves) PickList.Close();   // the turn passed (the table moved for us)
                _moves = null; _movesFor = _shownMoves = null;
                return;
            }
            if (_movesFor == _run.LastPublic || menuOpen || Chat.Typing) return;
            if (PickList.IsOpen && (PickList.Current == null || PickList.Current != _moves)) return;   // another list or the keyboard: ours waits
            if (Shown(opts) == _shownMoves) { _movesFor = _run.LastPublic; return; }   // the same moves: the highlight stays (or the list Esc hid stays hidden)
            ShowMoves();   // or replaces ours: the options changed
        }

        static string Shown(IList<(string label, string action)> opts) => string.Join("\n", opts.Select(o => o.label + "\t" + o.action));

        void ShowMoves()
        {
            var run = _run;
            var opts = run.Options;
            var rows = opts.Select(o => (o.label, (Action)(() => { _shownMoves = null; run.Act(o.action, _now); }))).ToList();   // picked: the next state shows the list again
            rows.Add(("Leave the table", () => { if (_run == run) Leave(_link, "you left the table", "closed"); }));
            PickList.Open($"{run.Game.Title}: your move", rows, null, "45 s to choose, or the table moves for you");
            _moves = PickList.Current;
            _movesFor = run.LastPublic;
            _shownMoves = Shown(opts);
        }

        // ---- notices ----

        public void OnNotice(byte from, string text, ServerLink link, Func<byte, Vec3?> posOf, long now)
        {
            if (!link.Roster.Any(e => e.Id == from)) return;
            if (!_gates.TryGetValue(from, out var gate)) _gates[from] = gate = new BlastGate();
            if (!gate.Allow(now)) return;
            var lines = TableMsg.Lines(text);
            if (lines.Count == 0) return;
            foreach (var run in new[] { _run }.Concat(_settling).Where(r => r != null && lines.Any(l => l.id == r.Id)).ToList())
            {
                run.PosOf = posOf;
                run.MyCash = _cash;
                run.OnNotice(from, text, now);
                Settle(run);
                Flush(run, link);
                if (run == _run) Over();
            }
            foreach (var (verb, id, rest) in lines)
            {
                if (ContestMsg.HostOf(id) != from || from == link.MyId) continue;   // ads come from the table's host only
                if (verb == "end") { _ads.Remove(id); continue; }
                if (verb == "seats" && _ads.TryGetValue(id, out var ad) && TableMsg.ParseSeats(rest, out var ids, out _)) { ad.Seats = ids.Count; continue; }
                if (verb != "open" || !TableMsg.ParseOpen(rest, out string game, out int stakes, out Vec3 at) || !TableRegistry.Has(game) || !On(game)) continue;
                if (Vec3.Distance(at, _myPos) > 20_000f) continue;   // nowhere near any of us
                if (!_ads.TryGetValue(id, out var a)) { _ads[id] = a = new Ad { Id = id, Host = from }; CoopScript.Log($"table {id}: {link.NameOf(from)} opened a {game} table at {Money.Dollars(stakes)}"); }
                a.Game = game; a.Stakes = stakes; a.At = at; a.HeardMs = now;
            }
        }

        public void OnRoster(HashSet<byte> ids, ServerLink link, long now)
        {
            foreach (var id in _ads.Where(kv => !ids.Contains(kv.Value.Host)).Select(kv => kv.Key).ToList()) _ads.Remove(id);
            foreach (byte gone in _gates.Keys.Where(k => !ids.Contains(k)).ToList()) _gates.Remove(gone);
            foreach (var run in new[] { _run }.Concat(_settling).Where(r => r != null).ToList())
            {
                foreach (var id in run.Seats.Concat(new[] { run.Host }).Where(id => !ids.Contains(id)).Distinct().ToList()) run.PlayerLeft(id, now);
                Settle(run);
                Flush(run, link);
            }
            _settling.RemoveAll(r => r.Done);
            if (_run != null) Over();
        }

        /// <summary>The session's over or the mod stops: we get up (our own table closes), nothing of ours is left.</summary>
        public void Clear(ServerLink link)
        {
            if (_run != null)
            {
                _run.Leave(_now, "host left");
                if (Connected(link)) { Flush(_run, link); Settle(_run); }   // a host's forfeit that ended the hand is paid with the end
                Drop("the session ended");
            }
            _settling.Clear();
            _ads.Clear();
            _gates.Clear();
            Seated = false;
        }

        // ---- leaving, ending ----

        void Leave(ServerLink link, string why, string reason)
        {
            _run.Leave(_now, reason);
            Flush(_run, link);
            Settle(_run);   // a host whose forfeit ended the hand pays it now
            if (_run.Settling) _settling.Add(_run);   // its hand's pay may still come
            Say("Co-op: " + why);
            Drop(why);
        }

        // True when our table is gone without us leaving it: it ended, or the host took our seat (we walked off).
        bool Over()
        {
            if (_run.Settling) { _settling.Add(_run); Say("Co-op: you lost your seat at the table"); Drop("unseated"); return true; }
            if (!_run.Done) return false;
            Ended();
            return true;
        }

        void Ended()
        {
            string host = _link?.NameOf(_run.Host) ?? "the host";
            string why = _run.Why == "too few" ? "nobody joined" : _run.Why == "host left" || _run.Why == "host gone" ? $"{host} left" : _run.Why == "no seat" ? "no seat for you" : $"{host} closed it";
            Say(!_run.IAmHost ? $"Co-op: the {_run.Game.Title} table is over: {why}" : _run.Why == "too few" ? "Co-op: nobody joined your table: it's closed" : "Co-op: your table is over");
            Drop(_run.Why);
        }

        void Drop(string why)
        {
            CoopScript.Log($"table {_run.Id}: {why}");
            if (_moves != null && PickList.Current == _moves) PickList.Close();
            _moves = null; _movesFor = _shownMoves = null;
            Stand();
            _run = null;
            Seated = false;
        }

        // ---- money and sitting ----

        // The host's settlement for us, already checked: the money moves (Money caps and refuses what we haven't got).
        void Settle(TableRun run)
        {
            foreach (int cents in run.Paid)
            {
                if (cents > 0 && Money.Give(cents)) { Stats.Add("money.won", cents); Say($"Co-op: you won {Money.Dollars(cents)}"); }
                else if (cents < 0 && Money.Take(-cents)) { Stats.Add("money.lost", -cents); Say($"Co-op: you lost {Money.Dollars(-cents)}"); }
                else CoopScript.Log($"table {run.Id}: couldn't move {Money.Dollars(cents)} (cash {Money.Dollars(Money.Cents)})");
            }
            run.Paid.Clear();
        }

        // Every 200 ms while seated, until we sit or no sitting scenario loads.
        void Sit(long now)
        {
            if (_sit != 0 || _sitTry >= Sits.Length) return;
            if (_sitTry < 0) { _sitTry = 0; _sitTryMs = now; }
            uint scenario = MISC.GET_HASH_KEY(Sits[_sitTry]);
            STREAMING._REQUEST_SCENARIO_TYPE(scenario, 1, 0, 0);
            if (STREAMING._HAS_SCENARIO_TYPE_LOADED(scenario, false))
            {
                int me = PLAYER.PLAYER_PED_ID();
                TASK.TASK_START_SCENARIO_IN_PLACE_HASH(me, scenario, -1, true, 0, ENTITY.GET_ENTITY_HEADING(me), false);
                _sit = scenario;
                CoopScript.Log($"tables: sitting down ({Sits[_sitTry]})");
                return;
            }
            if (now - _sitTryMs < SitTryMs) return;
            STREAMING._REMOVE_SCENARIO_ASSET(scenario);
            _sitTry++;
            _sitTryMs = now;
            if (_sitTry >= Sits.Length) CoopScript.Log("tables: no sitting scenario loaded: standing at the table");
        }

        void Stand()
        {
            if (_sit != 0)
            {
                int me = PLAYER.PLAYER_PED_ID();
                if (PED.IS_PED_USING_SCENARIO_HASH(me, _sit)) TASK.CLEAR_PED_TASKS(me, false, false);   // only our sitting: never another task
                STREAMING._REMOVE_SCENARIO_ASSET(_sit);
            }
            _sit = 0;
            _sitTry = -1;
        }

        static void Flush(TableRun run, ServerLink link)
        {
            foreach (var (to, text) in run.Notices()) link.SendNotice(to, NoticeKind.Table, text);
            string note = run.TakeNote();
            if (note != null) CoopScript.Log($"table {run.Id}: {note}");
        }

        static string Title(string game) => TableRegistry.Make(game)?.Title ?? game;
        static void Say(string text)   // waits while we play at one of the game's own tables; names come from strangers: no ~ < >
        {
            text = new string(text.Where(c => c != '~' && c != '<' && c != '>').ToArray());
            if (!TableGames.Hold(text)) Screen.DisplaySubtitle(text);
        }
    }
}
