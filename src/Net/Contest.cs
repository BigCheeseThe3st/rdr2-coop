// Contests (0.9.0): races, duels, shooting and hunting against each other, on one framework. This header is the
// whole contract a contest kind is written to; src/Net/Race.cs + src/Mod/Races.cs are the worked example.
//
// WHAT A CONTEST IS
//   An id "<host>-<seq>" (ContestMsg.HostOf), a kind, a params string the kind defines (its setup's answers), a host
//   (the player who started it: runs the rules, the standings and the result) and its players: the host, then
//   everyone whose game heard the open within 60 m of the host while not busy (Activities.Busy, a story mission or
//   cutscene) and answered "in" before GO (their game does it by itself). The host takes an in only from a player
//   it sees within 80 m of itself (our copies of each other lag). At GO, fewer than two players: called off.
//   One contest at a time in a game; the menu row turns into "Call off the <kind>" (host) / "Quit the <kind>".
//
// THE MESSAGES: NoticeKind.Contest, text "<verb> <id> [rest]". 5 a second per sender, the rest dropped; ids we
// don't know, verbs from the wrong sender (only the host sends open/start/standings/end and a cancel that ends
// it) and malformed text are dropped. Nothing else travels: kinds put what they need in params/report/standings.
//   open <id> <kind> <params>   host -> everyone, at once and again 5 s into the countdown (for a lost packet)
//   in <id>                     player -> host: I'm in; again every 2 s until GO
//   start <id>                  host -> everyone at GO, 10 s after the open (a subtitle each second until then)
//   report <id> <data>          player -> host: what Contest.Report said, at most twice a second
//   standings <id> <text>       host -> everyone: Contest.Standings() at GO and every 2 s while it runs (a player who
//                               missed start starts on it)
//   end <id> <results>          host -> everyone: Contest.Results() once Over, again every 2 s for the 15 s the
//                               results panel shows; then everyone drops it
//   cancel <id>                 host -> everyone: it's off.  player -> host: I'm out (Contest.Left; it goes on)
//
// WRITING A KIND: "duel", "shooting" or "hunting", those exact names: the name picks the [Games] switch (Duel,
// Shooting, Hunting; off = no menu row, opens ignored) and the win stat (duel.wins, shoot.wins, hunt.wins: WinStat).
//   1. src/Net/<Kind>.cs: public sealed class Duel : Contest. Pure (no game calls, no System.Drawing): tested.
//   2. src/Mod/<Kind>s.cs: static void Setup(Action<string> done): ask the host with PickList.Open / PickList.AskFor,
//      then done(params) (no newlines, <= 1000 bytes), or done(null) when they back out (nothing opens). And
//      static void View(Contest c, long now): called every tick before the framework's own, while a contest of
//      this kind exists (c is null once more after it's gone: remove your blips and markers then). Feed your
//      contest what the game sees from here (the race's Observe), natives at most every 200 ms; skip drawing in
//      a story mission (MissionProbe.StoryMission / InMissionScene). Exceptions are caught: the contest is off.
//   3. One line in CoopScript.Start under the race's: Contests.Register("duel", () => new Duel(), Duels.Setup, Duels.View);
//   4. tools/ContestBots.cs: one entry in Bots and a ContestBot class, for FakePlayer's `partner contest duel`.
//   5. tests/NetTests.cs: your rules, and a run through ContestRun.Open/Join, OnNotice and Tick like the race's.
//
// THE CONTEST CLASS: one per contest in each player's game; the framework sets Me, NameOf and PosOf first.
//   Me                         our player id.  NameOf(id): a player's name.  PosOf(id): where we see a player now
//                              (ourselves too; null: nowhere): the host checks reports against it
//   Host side, in the host's game only (the host plays too: its own reports go straight to its OnReport):
//   Begin(players, prms, now)  at GO: who's in (the host first) and the params
//   OnReport(from, data, now)  a player's report; from is one of the players, data is untrusted: parse, clamp, drop
//   Tick(now)                  every tick while it runs
//   Standings()                the standings text (<= 1000 bytes), asked at GO and every 2 s
//   Over                       true once it's decided: the framework asks Results() and ends it
//   Results()                  the final text (<= 1000 bytes)
//   Left(player, now)          optional: a player quit or left the session mid-contest
//   Client side, in every player's game, the host's included:
//   Start(prms, now)           at GO
//   Report(now)                what to tell the host now, or null; asked twice a second
//   OnStandings(text)          optional: the host's latest standings, as they come
//   Lines(text, now)           the panel rows (text, tone) from the standings, or from the results once ended: the
//                              title first (tone 3); 12 rows drawn at most. Tones: 0 white, 1 gold (mine), 2 grey,
//                              3 red (title), 4 green (the winner). Before GO the framework draws the countdown
//   End(results)               the results came: the panel shows them 15 s
//   Winner(results)            who won, from the results text (0: nobody, a draw): the framework adds the win stat
//                              in the winner's own game. Don't add it yourself
// The panel sits top right under the party list (src/Mod/Contests.cs). The host's contest is called off when it
// starts a story mission, leaves the session or stops the mod; a player who leaves is Left. ContestRun (below) is
// both ends' bookkeeping, fed notices and the clock, handing back what to send.
using System;
using System.Collections.Generic;
using System.Linq;

namespace RDR2Coop.Net
{
    public abstract class Contest
    {
        public abstract string Kind { get; }
        public Func<byte, string> NameOf = id => id.ToString();   // the framework sets these before Begin/Start
        public Func<byte, Vec3?> PosOf = id => null;   // where we last saw a player (null: nowhere), for the host to check reports against
        public byte Me;

        // Host side: the rules, the standings and the result.
        public abstract void Begin(IList<byte> players, string prms, long now);
        public abstract void OnReport(byte from, string data, long now);
        public abstract void Tick(long now);
        public abstract string Standings();
        public abstract bool Over { get; }
        public abstract string Results();
        public virtual void Left(byte player, long now) { }   // a player left the session mid-contest

        // Client side (every participant, the host included).
        public abstract void Start(string prms, long now);
        public abstract string Report(long now);   // what to tell the host now, or null (asked at most twice a second)
        public virtual void OnStandings(string text) { }
        public abstract IList<(string text, int tone)> Lines(string standings, long now);
        public abstract void End(string results);
        public abstract byte Winner(string results);   // 0: nobody

        /// <summary>The Stats key a kind's wins count under (the scoreboard's columns).</summary>
        public static string WinStat(string kind) => (kind == "shooting" ? "shoot" : kind == "hunting" ? "hunt" : kind) + ".wins";
    }

    public static class ContestMsg
    {
        /// <summary>"verb id rest" (rest may be empty): false for anything else.</summary>
        public static bool Parse(string text, out string verb, out string id, out string rest)
        {
            verb = id = rest = null;
            if (text == null || text.Length > NoticeMsg.MaxBytes) return false;
            var p = text.Split(new[] { ' ' }, 3);
            if (p.Length < 2 || p[0].Length == 0 || HostOf(p[1]) == 0) return false;
            verb = p[0]; id = p[1]; rest = p.Length > 2 ? p[2] : "";
            return true;
        }

        /// <summary>The host in an id "<host>-<seq>", or 0 when it isn't one.</summary>
        public static byte HostOf(string id)
        {
            int dash = id == null ? -1 : id.IndexOf('-');
            if (dash <= 0 || dash == id.Length - 1 || id.Length > 8) return 0;
            return byte.TryParse(id.Substring(0, dash), out byte h) && h != 0 && h != Protocol.Broadcast && uint.TryParse(id.Substring(dash + 1), out _) ? h : (byte)0;
        }
    }

    // One contest as this game sees it, host or player: the countdown, who's in, GO, the reports, the standings,
    // the end and the 15 s the results stay. Out is what to send (to, text); the caller drains it after every call.
    public sealed class ContestRun
    {
        public const int CountdownMs = 10_000, StandingsMs = 2000, ReportMs = 500, ResultsMs = 15_000, GiveUpMs = 10_000;
        public const float JoinRange = 60f, HostSlack = 20f;   // the host's copy of a player lags: one at the edge isn't left out

        public readonly string Id;
        public readonly byte Host, Me;
        public readonly Contest Game;
        public readonly string Params;
        public readonly List<byte> Players = new List<byte>();   // the host's: who's in, itself first
        public readonly List<(byte to, string text)> Out = new List<(byte, string)>();
        public string Standings { get; private set; } = "";
        public string Results { get; private set; }
        public string Why { get; private set; }   // once Done: how it ended
        public bool Started => _startedMs >= 0;
        public bool Ended => Results != null;
        public bool Done { get; private set; }
        public bool IAmHost => Host == Me;
        public string Title => char.ToUpperInvariant(Game.Kind[0]) + Game.Kind.Substring(1);

        readonly long _openedMs;
        long _startedMs = -1, _endedMs, _sentMs, _reportMs = -1, _inMs;
        int _said = int.MaxValue;
        bool _reopened;
        readonly HashSet<byte> _quit = new HashSet<byte>();   // the host's: who said they're out (a late in doesn't put them back)

        ContestRun(string id, byte host, byte me, Contest game, string prms, long now)
        {
            Id = id; Host = host; Me = me; Game = game; Params = prms ?? ""; _openedMs = now;
            Game.Me = me;
        }

        /// <summary>The host opens one: the open goes out and the countdown starts.</summary>
        public static ContestRun Open(byte me, uint seq, Contest game, string prms, long now)
        {
            var r = new ContestRun(me + "-" + seq, me, me, game, prms, now);
            r.Players.Add(me);
            r.Out.Add((Protocol.Broadcast, r.OpenText));
            return r;
        }

        /// <summary>A player heard one opened near them: in (again every 2 s until GO).</summary>
        public static ContestRun Join(byte me, byte host, string id, Contest game, string prms, long now)
        {
            var r = new ContestRun(id, host, me, game, prms, now);
            r.SayIn(now);
            return r;
        }

        string OpenText => $"open {Id} {Game.Kind} {Params}";
        void SayIn(long now) { _inMs = now; Out.Add((Host, "in " + Id)); }
        void Close(string why) { Done = true; Why = why; }

        /// <summary>Every tick, both ends. Returns a subtitle to show (the countdown, GO, why it's off) or null.</summary>
        public string Tick(long now)
        {
            if (Done) return null;
            if (!Started)
            {
                int left = (int)((CountdownMs - (now - _openedMs) + 999) / 1000);
                if (left <= 0)
                {
                    if (IAmHost) return Go(now);
                    if (now - _openedMs < CountdownMs + GiveUpMs) return null;   // the host's GO is on its way
                    Close("never started");
                    return $"Co-op: {Title} off: {Game.NameOf(Host)} never started it";
                }
                if (!IAmHost && now - _inMs >= StandingsMs) SayIn(now);
                if (IAmHost && !_reopened && now - _openedMs >= CountdownMs / 2) { _reopened = true; Out.Add((Protocol.Broadcast, OpenText)); }
                if (left >= _said) return null;
                _said = left;
                return $"Co-op: {Title} in {left}";
            }
            if (Ended)
            {
                if (IAmHost && now - _sentMs >= StandingsMs) { _sentMs = now; Out.Add((Protocol.Broadcast, $"end {Id} {Results}")); }
                if (now - _endedMs >= ResultsMs) Close("over");
                return null;
            }
            if (IAmHost)
            {
                Game.Tick(now);
                if (Game.Over) { Finish(Game.Results(), now); return null; }
                if (now - _sentMs >= StandingsMs) SendStandings(now);
            }
            if (now - _reportMs >= ReportMs)
            {
                _reportMs = now;
                string r = Game.Report(now);
                if (r != null) { if (IAmHost) Game.OnReport(Me, r, now); else Out.Add((Host, $"report {Id} {r}")); }
            }
            return null;
        }

        string Go(long now)
        {
            if (Players.Count < 2) { Out.Add((Protocol.Broadcast, "cancel " + Id)); Close("nobody joined"); return $"Co-op: nobody joined the {Game.Kind}"; }
            _startedMs = now;
            Out.Add((Protocol.Broadcast, "start " + Id));
            Game.Begin(Players.ToList(), Params, now);   // a copy: Left says who goes later
            Game.Start(Params, now);
            SendStandings(now);
            return "GO!";
        }

        void SendStandings(long now)
        {
            _sentMs = now;
            Standings = Game.Standings();
            Game.OnStandings(Standings);
            Out.Add((Protocol.Broadcast, $"standings {Id} {Standings}"));
        }

        void Finish(string results, long now)
        {
            Results = results ?? "";
            _endedMs = _sentMs = now;
            if (IAmHost) Out.Add((Protocol.Broadcast, $"end {Id} {Results}"));
            Game.End(Results);
            if (Game.Winner(Results) == Me) Stats.Add(Contest.WinStat(Game.Kind));
        }

        /// <summary>A Contest notice for this id from someone in the roster. Returns a subtitle or null.</summary>
        public string OnNotice(byte from, string verb, string rest, long now)
        {
            if (Done) return null;
            if (IAmHost)
            {
                if (from == Me) return null;
                if (verb == "in" && !Started && !Players.Contains(from) && !_quit.Contains(from) && Near(from)) { Players.Add(from); return $"Co-op: {Game.NameOf(from)} is in"; }
                if (verb == "report" && Started && !Ended && Players.Contains(from)) Game.OnReport(from, rest, now);
                if (verb == "cancel" && !Ended && _quit.Add(from) && Remove(from, now)) return $"Co-op: {Game.NameOf(from)} is out of the {Game.Kind}";
                return null;
            }
            if (from != Host) return null;   // only the contest's host runs it
            if (verb == "cancel") { Close("cancelled"); return $"Co-op: {Game.NameOf(Host)} called the {Game.Kind} off"; }
            if (verb != "start" && verb != "standings" && verb != "end") return null;
            string say = null;
            if (!Started) { _startedMs = now; Game.Start(Params, now); say = "GO!"; }
            if (verb == "standings" && !Ended) { Standings = rest; Game.OnStandings(rest); }
            if (verb == "end" && !Ended) Finish(rest, now);
            return say;
        }

        bool Near(byte id)
        {
            Vec3? them = Game.PosOf(id), us = Game.PosOf(Me);
            return them != null && us != null && Vec3.Distance(them.Value, us.Value) <= JoinRange + HostSlack;
        }

        bool Remove(byte id, long now)
        {
            if (!Players.Remove(id)) return false;
            if (Started && !Ended) Game.Left(id, now);
            return true;
        }

        /// <summary>Someone left the session: the host leaving ends it for everyone; a player leaving is out of it.</summary>
        public string PlayerLeft(byte id, long now)
        {
            if (Done) return null;
            if (id == Host) { Close("host left"); return $"Co-op: {Title} off: {Game.NameOf(Host)} left"; }
            if (IAmHost) Remove(id, now);
            return null;
        }

        /// <summary>Called off by us: the host tells everyone it's off; a player tells the host it's out.</summary>
        public void Cancel()
        {
            if (Done) return;
            if (!Ended) Out.Add((IAmHost ? Protocol.Broadcast : Host, "cancel " + Id));
            Close(IAmHost ? "cancelled" : "quit");
        }

        /// <summary>The panel: the countdown and who's in, then the kind's own lines.</summary>
        public IList<(string text, int tone)> Lines(long now)
        {
            if (Started) return Game.Lines(Ended ? Results : Standings, now);
            int left = Math.Max(0, (int)((CountdownMs - (now - _openedMs) + 999) / 1000));
            return new List<(string, int)> { ($"{Title}: starting in {left}", 3),
                (IAmHost ? "In: " + string.Join(", ", Players.Select(id => Game.NameOf(id))) : "You're in: wait for GO", 0) };
        }
    }
}
