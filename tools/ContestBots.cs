// FakePlayer's contests (0.9.0): `partner contest [kind]` (default race) answers the first contest of that kind it
// hears opened with "in" (again every 2 s until GO), then reports what the kind's bot says every 500 ms, and
// prints the standings and the result. A host that goes quiet for 20 s (it left) is forgotten, so the next
// contest is answered. Each contest package adds its kind to ContestBots.Bots: one line and its bot class.
// Compiled into FakePlayer.exe only (tools/*.cs); the game never sees this file.
using System;
using System.Collections.Generic;
using RDR2Coop.Net;

// What a fake partner reports, given the contest's params and the ms since GO; null = nothing to say now.
// Standings hands it each new standings text from the host (a duel's "draw!", a target's score...).
abstract class ContestBot
{
    public abstract string Report(string prms, long sinceStartMs);
    public virtual void Standings(string text) { }
}

static class ContestBots
{
    static readonly Dictionary<string, Func<ContestBot>> Bots = new Dictionary<string, Func<ContestBot>> { { "race", () => new RaceBot() },
        { "hunting", () => new HuntBot() }, { "shooting", () => new ShootingBot() }, { "duel", () => new DuelBot() } };

    public static ContestBot For(string kind) => Bots.TryGetValue(kind ?? "", out var make) ? make() : null;
}

// race: 300 m out and closing, done after 20 + up to 10 s, so a real player can beat it or lose to it. The host
// takes a done only from a racer it last saw within 45 m of the finish, and this fake partner circles YOU: so
// its done counts once you're near the finish (it keeps saying it every 2 s until then).
sealed class RaceBot : ContestBot
{
    readonly int _doneMs = 20000 + new Random().Next(10000);
    long _saidMs = -2000;

    public override string Report(string prms, long t)
    {
        if (t - _saidMs < 2000) return null;
        _saidMs = t;
        return t >= _doneMs ? "done " + _doneMs : "dist " + Math.Max(0, 300 - 300 * t / _doneMs);
    }
}

// hunting: a rabbit 15 s in, then a deer 40 s in (4 points in all), the whole list every 2 s as a hunter's game
// says it (the host keeps the longest), so one deer and a fox, or a bigger animal, beats it.
sealed class HuntBot : ContestBot
{
    long _saidMs = -2000;

    public override string Report(string prms, long t)
    {
        if (t < 15000 || t - _saidMs < 2000) return null;
        _saidMs = t;
        return "kill " + Hunting.Hash("a_c_rabbit_01") + (t >= 40000 ? " " + Hunting.Hash("a_c_deer_01") : "");
    }
}

// shooting: breaks a bottle every few seconds, about 70% of them by the time, so a real player can beat it or lose
// to it; says its score when it rises and again every 2 s (in case one went missing).
sealed class ShootingBot : ContestBot
{
    readonly Random _rng = new Random();
    int _score;
    long _nextMs = -1, _saidMs;

    public override string Report(string prms, long t)
    {
        if (!Shooting.TryParse(prms, out _, out _, out _, out int count, out int seconds)) return null;
        int gap = (int)(seconds * 1000 / (count * 0.7));
        if (_nextMs < 0) _nextMs = gap / 2 + _rng.Next(gap);
        bool rose = false;
        while (t >= _nextMs && _score < count) { _score++; rose = true; _nextMs += gap / 2 + _rng.Next(gap); }
        if (_score == 0 || (!rose && t - _saidMs < 2000)) return null;
        _saidMs = t;
        return "score " + _score;
    }
}

// duel: start a duel against this fake partner (it circles you 6 m away); it "fires" 2 s after its DRAW, so put a
// shot into its stand-in within 2 s of yours and you win (in your game's panel and the scoreboard); wait and it
// wins (you're knocked over). Draw before DRAW and you can't: aim and fire stay off until then. It says its shot
// every second until the end, as a real partner's game does; as an onlooker (you duel someone else) it's ignored.
sealed class DuelBot : ContestBot
{
    const int ReactMs = 2000;
    long _saidMs = -Duel.EveryMs;

    public override string Report(string prms, long t)
    {
        if (!Duel.TryParse(prms, out _, out _, out int pause) || t < Duel.FaceMs + pause + ReactMs || t - _saidMs < Duel.EveryMs) return null;
        _saidMs = t;
        return "shot " + ReactMs;
    }
}

static class ContestMode
{
    public static string Kind = "race";
    static string _id, _prms, _standings;
    static byte _host;
    static ContestBot _bot;
    static long _inMs, _startMs = -1, _askedMs, _heardMs;
    const int QuietMs = 20_000;

    public static void Heard(ServerLink link, byte from, string text, long now)
    {
        if (!ContestMsg.Parse(text, out string verb, out string id, out string rest)) return;
        string stamp = DateTime.Now.ToString("HH:mm:ss");
        if (_id == null)
        {
            if (verb != "open" || ContestMsg.HostOf(id) != from) return;
            int sp = rest.IndexOf(' ');
            string kind = sp < 0 ? rest : rest.Substring(0, sp);
            if (kind != Kind) { Console.WriteLine($"{stamp} player {from} opened a {kind}; we only do {Kind}"); return; }
            _id = id; _host = from; _prms = sp < 0 ? "" : rest.Substring(sp + 1); _bot = ContestBots.For(kind); _startMs = -1; _standings = null; _heardMs = now;
            Console.WriteLine($"{stamp} player {from} opened {kind} {id} ({_prms}): we're in" + (_bot == null ? " (no bot: we'll only watch)" : ""));
            SayIn(link, now);
            return;
        }
        if (id != _id || from != _host) return;
        _heardMs = now;
        switch (verb)
        {
            case "start": if (_startMs < 0) { _startMs = now; Console.WriteLine($"{stamp} GO"); } break;
            case "standings": if (_startMs < 0) _startMs = now; if (rest != _standings) { _standings = rest; Console.WriteLine($"{stamp} standings: {rest}"); _bot?.Standings(rest); } break;
            case "end": Console.WriteLine($"{stamp} over: {rest}"); _id = null; break;
            case "cancel": Console.WriteLine($"{stamp} called off"); _id = null; break;
        }
    }

    static void SayIn(ServerLink link, long now) { _inMs = now; link.SendNotice(_host, NoticeKind.Contest, "in " + _id); }

    public static void Tick(ServerLink link, long now)
    {
        if (_id == null) return;
        if (now - _heardMs >= QuietMs) { Console.WriteLine($"{DateTime.Now:HH:mm:ss} player {_host} went quiet: contest forgotten"); _id = null; return; }
        if (_startMs < 0) { if (now - _inMs >= 2000) SayIn(link, now); return; }
        if (_bot == null || now - _askedMs < 500) return;
        _askedMs = now;
        string r = _bot.Report(_prms, now - _startMs);
        if (r == null) return;
        link.SendNotice(_host, NoticeKind.Contest, $"report {_id} {r}");
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} report: {r}");
    }
}
