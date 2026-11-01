// FakePlayer's tables (0.9.0): `partner table <game>` (e.g. highcard) stands still 6 m from you, joins the first table
// of that game it hears opened (from its open line: open one within 10 m of it from the co-op menu), brings $100 of
// pretend cash to each hand, and when the game offers it options, plays what its bot picks 1.5 s later. It prints the
// table's panel lines as they change, every move and every settlement, with its wallet. It runs the very same
// TableRun and TableGame view as a real partner's game, made by TableRegistry.
// Each game package adds its game and bot to TableBots.Bots: one line, plus its bot class. Compiled into
// FakePlayer.exe only (tools/*.cs); the game never sees this file.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2Coop.Net;

// What a fake partner plays: one of the options the game offers it now (their action text).
abstract class TableBot { public abstract string Pick(TableGame view, byte me, IList<(string label, string action)> options); }

// Takes the first option: right for high card's lone "Show my card".
sealed class FirstOptionBot : TableBot
{
    public override string Pick(TableGame view, byte me, IList<(string label, string action)> options) => options[0].action;
}

// Liar's dice: counts its own dice and a third of the rest (a sixth for ones), calls Liar! on a bid over that by more
// than 2, Spot on when it's that to a quarter, else bids the face with the most room under its count.
sealed class LiarsDiceBot : TableBot
{
    public override string Pick(TableGame view, byte me, IList<(string label, string action)> options)
    {
        var g = view as LiarsDice;
        if (g == null) return options[0].action;
        string mine = g.MyDice;
        double Expect(int face) => LiarsDice.Count(new[] { mine }, face) + (g.DiceInPlay - mine.Length) / (face == 1 ? 6.0 : 3.0);
        if (g.BidQuantity > 0 && g.BidQuantity > Expect(g.BidFace) + 2) return "liar";
        if (g.BidQuantity > 0 && Math.Abs(g.BidQuantity - Expect(g.BidFace)) < 0.25) return "spot";
        var bids = options.Where(o => o.action.StartsWith("bid ")).Select(o => o.action.Split(' ')).ToList();
        return bids.Count == 0 ? "liar" : string.Join(" ", bids.OrderByDescending(t => Expect(int.Parse(t[2])) - int.Parse(t[1])).First());
    }
}

// Dominoes: the play that scores the most, else the heaviest tile (rid of the pips), else draw or pass (then the only option).
sealed class DominoesBot : TableBot
{
    public override string Pick(TableGame view, byte me, IList<(string label, string action)> options) =>
        options.OrderByDescending(o => ((Dominoes)view).Gain(o.action)).ThenByDescending(o => Dominoes.TilePips(o.action)).First().action;
}

// Blackjack: bets the stakes, doubles on 10 or 11, hits under 17 against a dealer's 7 or better (an ace too),
// and against a 2 to 6 stands on 12 or more.
sealed class BlackjackBot : TableBot
{
    public override string Pick(TableGame view, byte me, IList<(string label, string action)> options)
    {
        var acts = options.Select(o => o.action).ToList();
        var bj = (Blackjack)view;
        int total = Blackjack.Total(bj.CardsOf(me)).total, up = bj.UpCard == null ? 10 : Blackjack.Total(new[] { bj.UpCard }).total;
        string want = acts.Contains("bet 1") ? "bet 1" : (total == 10 || total == 11) && acts.Contains("double") ? "double" : total < (up >= 7 ? 17 : 12) ? "hit" : "stand";
        return acts.Contains(want) ? want : acts[0];
    }
}

// Poker: checks when it can, calls up to 2 big blinds, folds to more unless it holds a pair or better (a pocket pair
// before the flop), and 1 time in 10 makes the smallest raise it's offered.
sealed class PokerBot : TableBot
{
    readonly Random _rng = new Random();

    public override string Pick(TableGame view, byte me, IList<(string label, string action)> options)
    {
        var p = view as Poker;
        var raise = options.FirstOrDefault(o => o.action.StartsWith("raise "));
        if (raise.action != null && _rng.Next(10) == 0) return raise.action;
        if (options.Any(o => o.action == "check")) return "check";
        bool call = p == null || p.ToCall(me) <= 2 * p.Stakes || p.MyRank() >= 1;
        return call && options.Any(o => o.action == "call") ? "call" : "fold";
    }
}

static class TableBots
{
    static readonly Dictionary<string, (Func<TableGame> game, Func<TableBot> bot)> Bots = new Dictionary<string, (Func<TableGame>, Func<TableBot>)>
    {
        { "highcard", (() => new HighCard(), () => new FirstOptionBot()) },
        { "dice", (() => new LiarsDice(), () => new LiarsDiceBot()) },
        { "dominoes", (() => new Dominoes(), () => new DominoesBot()) },
        { "blackjack", (() => new Blackjack(), () => new BlackjackBot()) },
        { "poker", (() => new Poker(), () => new PokerBot()) },
    };

    static TableBots() { foreach (var kv in Bots) TableRegistry.Register(kv.Key, kv.Value.game); }

    public static TableBot For(string game) => Bots.TryGetValue(game ?? "", out var b) ? b.bot() : null;
}

static class TableMode
{
    public static string Game = "highcard";
    const int ThinkMs = 1500;
    static TableRun _run;
    static TableBot _bot;
    static int _wallet = 100_00;
    static string _panel, _decided, _waitingOn;
    static long _offeredMs = -1;

    static string Stamp => DateTime.Now.ToString("HH:mm:ss");

    public static void Heard(ServerLink link, byte from, string text, long now)
    {
        if (_run == null)
        {
            foreach (var (verb, id, rest) in TableMsg.Lines(text))
            {
                if (verb != "open" || ContestMsg.HostOf(id) != from || !TableMsg.ParseOpen(rest, out string game, out int stakes, out Vec3 at)) continue;
                if (game != Game) { if (_waitingOn != id) Console.WriteLine($"{Stamp} player {from} opened a {game} table; we only play {Game}"); _waitingOn = id; continue; }
                _bot = TableBots.For(game);
                _run = TableRun.Join(link.MyId, from, id, TableRegistry.Make(game), stakes, at, now);
                Console.WriteLine($"{Stamp} player {from} opened a {game} table at {TableMsg.Dollars(stakes)} ({id}): asking for a seat");
                break;
            }
            Flush(link);
            return;
        }
        _run.MyCash = _wallet;
        _run.OnNotice(from, text, now);
        Flush(link);
    }

    public static void Tick(ServerLink link, long now)
    {
        if (_run == null) return;
        if (!link.Roster.Any(e => e.Id == _run.Host)) _run.PlayerLeft(_run.Host, now);
        _run.MyCash = _wallet;
        _run.Tick(now);
        var opts = _run.Options;
        string turn = _run.LastPublic + "|" + string.Join("|", opts.Select(o => o.action));
        if (opts.Count == 0) _offeredMs = -1;
        else if (turn != _decided)
        {
            if (_offeredMs < 0) _offeredMs = now;
            if (now - _offeredMs >= ThinkMs)
            {
                string act = _bot?.Pick(_run.Game, link.MyId, opts) ?? opts[0].action;
                Console.WriteLine($"{Stamp} our move: {act} (offered: {string.Join(", ", opts.Select(o => o.label))})");
                _run.Act(act, now);
                _decided = turn;
                _offeredMs = -1;
            }
        }
        string panel = string.Join("\n  ", _run.Lines(id => id == link.MyId ? "us" : link.NameOf(id)).Select(l => l.text));
        if (_run.Seated && panel != _panel) { _panel = panel; Console.WriteLine($"{Stamp} table:\n  {panel}"); }
        Flush(link);
        if (_run.Done) { Console.WriteLine($"{Stamp} the table is over for us: {_run.Why}"); _run = null; _panel = null; }
    }

    static void Flush(ServerLink link)
    {
        if (_run == null) return;
        foreach (int c in _run.Paid) { _wallet += c; Console.WriteLine($"{Stamp} {(c > 0 ? "won" : "lost")} {TableMsg.Dollars(Math.Abs(c))}: wallet {TableMsg.Dollars(_wallet)}"); }
        _run.Paid.Clear();
        string note = _run.TakeNote();
        if (note != null) Console.WriteLine($"{Stamp} {note}");
        foreach (var (to, text) in _run.Notices()) link.SendNotice(to, NoticeKind.Table, text);
    }
}
