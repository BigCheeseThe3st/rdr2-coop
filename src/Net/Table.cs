// Mod tables (0.9.0): cards and dice against each other at a spot of your choosing. A table has an id
// "<host>-<seq>" (the same shape as a contest's), a game (TableRegistry name), stakes (cents: the unit bet), the spot
// it was opened at, a host (the player who opened it: runs the rules, src/Net/TableGame.cs) and seats (up to 4 player
// ids in join order, the host first). Pure: TableRun is both ends' bookkeeping, fed notices and the clock, handing
// back what to send (Out, merged by Notices()) and the money to move (Paid); src/Mod/Tables.cs does the game calls.
//
// THE MESSAGES (NoticeKind.Table). A notice holds one or more lines separated by \n, each "<verb> <id> <fields>",
// space-separated; every line of one notice is about one table. Only the table's host (the id's first part) may send
// open, seats, pub, priv, pay and end; only a player join, leave, cash and act, to the host alone.
//   open <id> <game> <stakes> <x> <y> <z>   host -> all, on opening and every 5 s while open. stakes: one of
//                                            50 100 250 500 1000 2500; x y z: where it is (metres, the game's map)
//   join <id>                                player -> host: a seat please (again every 2 s until seated, 6 s at most;
//                                            the host seats players it last saw within 15 m, 4 at most)
//   leave <id>                               player -> host: I'm up. Mid-hand that forfeits the hand
//   seats <id> <id,id,...> <next>            host -> all, on every change, at each hand's start and every 5 s: who sits
//                                            there, the host first; next = the number of the next hand to be dealt
//   cash <id> <cents> <hand>                 player -> host, answering every seats line that lists it: the cash it
//                                            brings to hand <hand>, at most 50 x stakes: the most it can lose in that hand.
//                                            A hand is dealt to the seats that brought at least the stakes, once everyone
//                                            answered or after 5 s
//   act <id> <action>                        player -> host: a move, the action text of one of the game's Options
//                                            (64 characters at most; the game decides what's legal)
//   pub <id> <state>                         host -> all: the game's Public(), on change (at most every 0.6 s) and
//                                            every 3 s while a hand runs or its result shows (900 bytes at most)
//   priv <id> <secret>                       host -> one seat: the game's Private(seat), with every pub. Never broadcast
//   pay <id> <playerId> <cents> <hand>       host -> all, when a hand is over: one line per seat in the hand, all in ONE
//                                            notice (they add up to zero, or a game drops them all), again 2 s and 4 s
//                                            later for a lost packet. Each game applies only its own line, once per
//                                            hand, and only for a hand it brought cash to: never a loss over that cash,
//                                            never a win after it left the table. The result shows 5 s, then next hand
//   end <id> <reason>                        host -> all: the table is closed: closed, too few (nobody joined for 5
//                                            minutes), host left, over (the game says it's finished)
// A player who leaves, walks away (over 15 m: the host unseats anyone it sees 20 m off) or disconnects mid-hand
// forfeits it (TableGame.Forfeit); one who left still pays a hand it lost (its game waits for that pay while the
// table's host is still heard, 30 minutes at most). When the host gets up, it forfeits too: a hand that's then over
// (heads-up, or the house's hand at blackjack) is paid with the end; otherwise the table ends and the hand in play is
// void (nobody pays); a host that disconnects voids it. A game takes one settlement every 4 s at most (a real hand
// takes longer), so a hostile host can't invent hands to drain a seat. Ids not known, lines from the wrong sender,
// odd numbers, oversize text: dropped without a word.
//
// HOW TO ADD A GAME (the game packages): a `public sealed class X : TableGame` in src/Net/X.cs (pure; see
// TableGame.cs and the sample HighCard.cs), one line in CoopScript.Start next to highcard's:
// `Tables.Register("x", () => new X());` (Tables.On maps poker, blackjack, dominoes and dice to their [Games]
// switch), one line in tools/TableBots.cs with its bot, and its tests. Nothing else: the menu, seats, cash, turns,
// messages, panel and money are the framework's.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RDR2Coop.Net
{
    public static class TableMsg
    {
        public const int MaxSeats = 4, CashTimesStakes = 50, MaxCents = 1_000_000, MaxState = 900, MaxAction = 64;
        public static readonly int[] Stakes = { 50, 100, 250, 500, 1000, 2500 };
        public static readonly string[] EndReasons = { "closed", "too few", "host left", "over" };

        public static bool IsGameName(string s) => s != null && s.Length >= 1 && s.Length <= 16 && s.All(c => c >= 'a' && c <= 'z');

        /// <summary>The most a seat brings (and can lose) at these stakes.</summary>
        public static int CashCap(int stakes) => Math.Min(MaxCents, CashTimesStakes * stakes);

        /// <summary>A notice's well-formed lines ("verb id rest", the id checked as a contest's); the rest are dropped.</summary>
        public static List<(string verb, string id, string rest)> Lines(string text)
        {
            var list = new List<(string, string, string)>();
            if (text == null || text.Length > NoticeMsg.MaxBytes) return list;
            foreach (var line in text.Split(new[] { '\n' }))
                if (list.Count < 12 && ContestMsg.Parse(line, out string verb, out string id, out string rest)) list.Add((verb, id, rest));
            return list;
        }

        public static string Open(string id, string game, int stakes, Vec3 at) =>
            $"open {id} {game} {stakes} {F(at.X)} {F(at.Y)} {F(at.Z)}";

        public static bool ParseOpen(string rest, out string game, out int stakes, out Vec3 at)
        {
            game = null; stakes = 0; at = default;
            var t = (rest ?? "").Split(new[] { ' ' });
            if (t.Length != 5 || !IsGameName(t[0]) || !Int(t[1], out stakes) || Array.IndexOf(Stakes, stakes) < 0) return false;
            if (!Coord(t[2], out float x) || !Coord(t[3], out float y) || !Coord(t[4], out float z)) return false;
            game = t[0]; at = new Vec3(x, y, z);
            return true;
        }

        /// <summary>"1,2,3 4": 1 to 4 distinct player ids and the next hand's number (1 or more).</summary>
        public static bool ParseSeats(string rest, out List<byte> ids, out int next)
        {
            ids = new List<byte>(); next = 0;
            var t = (rest ?? "").Split(new[] { ' ' });
            if (t.Length != 2 || !Int(t[1], out next) || next < 1) return false;
            foreach (var s in t[0].Split(new[] { ',' }))
            {
                if (!byte.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out byte id) || id == 0 || id == Protocol.Broadcast || ids.Contains(id) || ids.Count == MaxSeats) return false;
                ids.Add(id);
            }
            return true;
        }

        /// <summary>"<playerId> <cents> <hand>", cents signed and at most MaxCents either way.</summary>
        public static bool ParsePay(string rest, out byte player, out int cents, out int hand)
        {
            player = 0; cents = 0; hand = 0;
            var t = (rest ?? "").Split(new[] { ' ' });
            return t.Length == 3 && byte.TryParse(t[0], NumberStyles.None, CultureInfo.InvariantCulture, out player) && player != 0 && player != Protocol.Broadcast
                && int.TryParse(t[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out cents) && Math.Abs((long)cents) <= MaxCents
                && Int(t[2], out hand) && hand >= 1;
        }

        /// <summary>Null when a hand's settlement is good: one entry per seat, adding up to zero, nobody losing more than
        /// they brought, nothing over MaxCents. Otherwise why not.</summary>
        public static string CheckSettlement(IList<int> net, IList<int> cash)
        {
            if (net == null || cash == null || net.Count != cash.Count) return "not one entry per seat";
            if (net.Sum(n => (long)n) != 0) return "doesn't add up to zero";
            for (int i = 0; i < net.Count; i++)
                if (Math.Abs((long)net[i]) > MaxCents || -net[i] > cash[i]) return $"seat {i} would lose more than it brought";
            return null;
        }

        public static string Dollars(int cents) => (cents < 0 ? "-$" : "$") + Math.Abs((long)cents) / 100 + "." + (Math.Abs((long)cents) % 100).ToString("00");

        static bool Int(string s, out int v) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v);

        static bool Coord(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v) && Math.Abs(v) <= 100_000f;

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    // One table as this game sees it, host or player. Out is what to send (to, line): the caller sends Notices() after
    // every call; Paid is the money the caller moves (checked here: + won, - lost), then clears.
    public sealed class TableRun
    {
        public const int OpenEveryMs = 5000, PubMinMs = 600, PubEveryMs = 3000, GatherMs = 5000, ResultsMs = 5000, PayAgainMs = 2000,
                         LonelyMs = 300_000, JoinAgainMs = 2000, SeatWaitMs = 6000, SettleWaitMs = 1_800_000, HostQuietMs = 20_000, WalkCheckMs = 1000,
                         PaidMinMs = 4000;
        public const float JoinRange = 10f, LeaveRange = 15f, Slack = 5f;

        enum Phase { Waiting, Gathering, Playing, Results }

        public readonly string Id;
        public readonly byte Host, Me;
        public readonly int Stakes;
        public readonly Vec3 At;
        public readonly TableGame Game;   // our view of it (the host too)
        readonly TableGame _rules;        // the host's rules; null on a player
        public readonly List<byte> Seats = new List<byte>();
        public readonly List<(byte to, string text)> Out = new List<(byte, string)>();
        public readonly List<int> Paid = new List<int>();
        public Func<byte, Vec3?> PosOf = id => null;   // host: where we last saw a player (null: nowhere)
        public Random Rng = new Random();
        public int MyCash;                              // the caller keeps it current: our cash in cents
        public string Note { get; private set; }        // the last thing worth a line in the log (TakeNote clears it)
        public string TakeNote() { var n = Note; Note = null; return n; }
        public string LastPublic { get; private set; }  // the hand as we last saw it (null: none yet)
        public bool Done { get; private set; }
        public string Why { get; private set; }         // once Done: closed, too few, host left, over, left, no seat, host gone
        public bool IAmHost => Host == Me;
        public bool Seated => !_left && Seats.Contains(Me);
        public bool Settling => _left && !Done;         // we got up mid-hand: waiting for that hand's pay
        public int Bring => Math.Max(0, Math.Min(MyCash, TableMsg.CashCap(Stakes)));

        // host
        Phase _phase;
        int _next = 1, _dealt;
        List<byte> _hand = new List<byte>();
        List<int> _handCash = new List<int>();
        readonly Dictionary<byte, int> _cash = new Dictionary<byte, int>();
        string _pubSeen, _pubSent, _payText;
        long _phaseMs, _openMs, _pubMs, _lonelyMs, _walkMs, _payMs;
        int _payAgain;
        // player
        readonly Dictionary<int, int> _declared = new Dictionary<int, int>();   // hand -> the most cash we said we bring to it
        readonly HashSet<int> _settled = new HashSet<int>();
        bool _left, _wasSeated, _leaveAgain;
        long _joinMs, _joinSaidMs, _leftMs, _heardMs, _paidMs = -PaidMinMs;

        TableRun(string id, byte host, byte me, TableGame view, TableGame rules, int stakes, Vec3 at, long now)
        {
            Id = id; Host = host; Me = me; Game = view; _rules = rules; Stakes = stakes; At = at;
            _openMs = _lonelyMs = _joinMs = _joinSaidMs = _heardMs = now;
        }

        /// <summary>We open a table: we're seat 0; the open and the seats go out.</summary>
        public static TableRun Open(byte me, uint seq, Func<TableGame> make, int stakes, Vec3 at, long now)
        {
            var r = new TableRun(me + "-" + seq, me, me, make(), make(), stakes, at, now);
            r.Seats.Add(me);
            r.Out.Add((Protocol.Broadcast, TableMsg.Open(r.Id, r.Game.Name, stakes, at)));
            r.SendSeats();
            return r;
        }

        /// <summary>We ask for a seat at a table someone opened (from its open line).</summary>
        public static TableRun Join(byte me, byte host, string id, TableGame view, int stakes, Vec3 at, long now)
        {
            var r = new TableRun(id, host, me, view, null, stakes, at, now);
            r.Out.Add((host, "join " + id));
            return r;
        }

        void SendSeats() => Out.Add((Protocol.Broadcast, $"seats {Id} {string.Join(",", Seats)} {_next}"));
        void Close(string why) { Done = true; Why = why; }
        void End(string why) { Out.Add((Protocol.Broadcast, $"end {Id} {why}")); Close(why); }
        bool Unsettled => _declared.Keys.Any(h => !_settled.Contains(h));

        /// <summary>Every tick, both ends.</summary>
        public void Tick(long now)
        {
            if (Done) return;
            if (!IAmHost) { PlayerTick(now); return; }
            if (now - _openMs >= OpenEveryMs) { _openMs = now; Out.Add((Protocol.Broadcast, TableMsg.Open(Id, Game.Name, Stakes, At))); SendSeats(); }
            if (now - _walkMs >= WalkCheckMs)
            {
                _walkMs = now;
                foreach (var id in Seats.Where(s => s != Me).ToList())
                {
                    var p = PosOf(id);
                    if (p != null && Vec3.Distance(p.Value, At) > LeaveRange + Slack) { Unseat(id); Note = $"{id} walked away from the table"; }
                }
            }
            switch (_phase)
            {
                case Phase.Waiting:
                    if (Seats.Count >= _rules.MinPlayers) Gather(now);
                    else if (now - _lonelyMs >= LonelyMs) End("too few");
                    break;
                case Phase.Gathering:
                    _cash[Me] = Bring;
                    if (!Seats.All(_cash.ContainsKey) && now - _phaseMs < GatherMs) break;
                    var players = Seats.Where(s => _cash.TryGetValue(s, out int c) && c >= Stakes).ToList();
                    if (_rules.HostIsHouse && (players.Count == 0 || players[0] != Me)) players.Clear();   // never a guest as the house
                    if (players.Count >= _rules.MinPlayers) Deal(players, now);
                    else if (now - _phaseMs >= GatherMs) { if (Seats.Count >= _rules.MinPlayers) Gather(now); else Idle(now); }   // ask again
                    break;
                case Phase.Playing:
                    _rules.Tick(now);
                    if (_rules.HandOver) Settle(now);
                    break;
                case Phase.Results:
                    if (_payText != null && _payAgain < 2 && now - _payMs >= PayAgainMs) { _payAgain++; _payMs = now; Out.Add((Protocol.Broadcast, _payText)); }
                    if (now - _phaseMs < ResultsMs) break;
                    if (_rules.Over) { End("over"); return; }
                    if (Seats.Count >= _rules.MinPlayers) Gather(now); else Idle(now);
                    break;
            }
            if (_phase == Phase.Playing || _phase == Phase.Results) Publish(now);
        }

        void Idle(long now) { _phase = Phase.Waiting; _lonelyMs = now; }

        void Gather(long now)
        {
            _phase = Phase.Gathering;
            _phaseMs = now;
            _cash.Clear();
            _cash[Me] = Bring;
            SendSeats();
        }

        void Deal(List<byte> players, long now)
        {
            _hand = players;
            _handCash = players.Select(id => _cash[id]).ToList();
            _dealt = _next++;
            _rules.Begin(_hand, Stakes, _handCash, Rng);
            _phase = Phase.Playing;
            _phaseMs = now;
            _pubSent = null;
            _pubMs = now - PubEveryMs;   // the first state goes at once
            Note = $"hand {_dealt} dealt to {string.Join(",", _hand)}";
        }

        void Settle(long now)
        {
            var net = _rules.Settlement();
            string bad = TableMsg.CheckSettlement(net, _handCash);
            _payText = null;
            if (bad != null) Note = $"hand {_dealt} void: the settlement {bad}";
            else
            {
                _payText = string.Join("\n", _hand.Select((id, i) => $"pay {Id} {id} {net[i]} {_dealt}"));   // one block: never split
                Out.Add((Protocol.Broadcast, _payText));
                int mine = _hand.IndexOf(Me);
                if (mine >= 0 && net[mine] != 0) Paid.Add(net[mine]);
                Note = $"hand {_dealt} settled: " + string.Join(" ", _hand.Select((id, i) => $"{id}:{net[i]}"));
            }
            _phase = Phase.Results;
            _phaseMs = _payMs = now;
            _payAgain = 0;
        }

        void Publish(long now)
        {
            string pub = _rules.Public() ?? "";
            if (pub.Length > TableMsg.MaxState || pub.IndexOf('\n') >= 0) { Note = "the game's state is too long to send"; return; }
            if (pub != _pubSeen)
            {
                _pubSeen = pub;
                See(pub);
                int mine = _hand.IndexOf(Me);
                string secret = mine >= 0 ? _rules.Private(mine) : null;
                if (secret != null) Safe(() => Game.OnPrivate(secret));
            }
            if (!(pub != _pubSent && now - _pubMs >= PubMinMs) && now - _pubMs < PubEveryMs) return;
            _pubSent = pub;
            _pubMs = now;
            Out.Add((Protocol.Broadcast, $"pub {Id} {pub}"));
            for (int i = 0; i < _hand.Count; i++)
            {
                if (_hand[i] == Me || !Seats.Contains(_hand[i])) continue;
                string secret = _rules.Private(i);
                if (secret != null && secret.Length <= TableMsg.MaxState && secret.IndexOf('\n') < 0) Out.Add((_hand[i], $"priv {Id} {secret}"));
            }
        }

        void See(string pub) { if (pub == LastPublic) return; LastPublic = pub; Safe(() => Game.OnPublic(pub)); }

        // The host takes a seat away: mid-hand that seat forfeits it.
        void Unseat(byte id)
        {
            if (!Seats.Remove(id)) return;
            int i = _hand.IndexOf(id);
            if (_phase == Phase.Playing && i >= 0) _rules.Forfeit(i);
            _cash.Remove(id);
            SendSeats();
        }

        void PlayerTick(long now)
        {
            if (_left)
            {
                if (!_leaveAgain && now - _leftMs >= 1000) { _leaveAgain = true; Out.Add((Host, "leave " + Id)); }   // once more, for a lost packet
                if (!Unsettled || now - _heardMs >= HostQuietMs || now - _leftMs >= SettleWaitMs) Close("left");   // a long game's pay comes at its end
                return;
            }
            if (now - _heardMs >= HostQuietMs) { Close("host gone"); return; }
            if (_wasSeated) return;
            if (now - _joinMs >= SeatWaitMs) { Close("no seat"); return; }
            if (now - _joinSaidMs >= JoinAgainMs) { _joinSaidMs = now; Out.Add((Host, "join " + Id)); }
        }

        /// <summary>A Table notice from someone in the roster (only its lines for this table count).</summary>
        public void OnNotice(byte from, string text, long now)
        {
            if (Done) return;
            var lines = TableMsg.Lines(text).Where(l => l.id == Id).ToList();
            if (lines.Count == 0 || from == Me) return;
            if (IAmHost) { foreach (var l in lines) FromPlayer(from, l.verb, l.rest, now); return; }
            if (from != Host) return;   // only the table's host runs it
            _heardMs = now;
            var pays = new List<(byte player, int cents, int hand)>();
            bool badPay = false;
            string end = null;
            foreach (var l in lines)
            {
                switch (l.verb)
                {
                    case "seats": OnSeats(l.rest, now); break;
                    case "pub": if (!_left && l.rest.Length <= TableMsg.MaxState) See(l.rest); break;
                    case "priv": if (!_left && l.rest.Length <= TableMsg.MaxState) Safe(() => Game.OnPrivate(l.rest)); break;
                    case "pay": if (TableMsg.ParsePay(l.rest, out byte p, out int c, out int h)) pays.Add((p, c, h)); else badPay = true; break;
                    case "end": end = Array.IndexOf(TableMsg.EndReasons, l.rest) >= 0 ? l.rest : "closed"; break;
                }
            }
            if (pays.Count > 0) OnPays(pays, badPay, now);   // before the end: a host that got up sends the hand's pay with it
            if (end != null) Close(end);
        }

        void FromPlayer(byte from, string verb, string rest, long now)
        {
            switch (verb)
            {
                case "join":
                    if (Seats.Contains(from)) { SendSeats(); return; }   // asking again: our seats line got lost
                    var p = PosOf(from);
                    if (Seats.Count >= TableMsg.MaxSeats || p == null || Vec3.Distance(p.Value, At) > LeaveRange) return;
                    Seats.Add(from);
                    _lonelyMs = now;
                    Note = $"{from} sat down";
                    SendSeats();
                    break;
                case "leave":
                    Unseat(from);
                    break;
                case "cash":
                    var t = rest.Split(new[] { ' ' });
                    if (_phase != Phase.Gathering || !Seats.Contains(from) || t.Length != 2) return;
                    if (!int.TryParse(t[0], NumberStyles.None, CultureInfo.InvariantCulture, out int cents)
                        || !int.TryParse(t[1], NumberStyles.None, CultureInfo.InvariantCulture, out int hand) || hand != _next) return;
                    _cash[from] = Math.Min(cents, TableMsg.CashCap(Stakes));
                    break;
                case "act":
                    int seat = _hand.IndexOf(from);
                    if (_phase == Phase.Playing && seat >= 0 && Seats.Contains(from) && rest.Length > 0 && rest.Length <= TableMsg.MaxAction) _rules.Act(seat, rest);
                    break;
            }
        }

        void OnSeats(string rest, long now)
        {
            if (!TableMsg.ParseSeats(rest, out var ids, out int next)) return;
            Seats.Clear();
            Seats.AddRange(ids);
            if (_left) return;
            if (!ids.Contains(Me))
            {
                if (_wasSeated) { _left = true; _leftMs = now; Note = "the host took our seat (too far away?)"; }
                return;
            }
            _wasSeated = true;
            int bring = Bring;
            _declared[next] = Math.Max(bring, _declared.TryGetValue(next, out int d) ? d : 0);
            foreach (int old in _declared.Keys.Where(h => h < next - 3).ToList()) _declared.Remove(old);
            Out.Add((Host, $"cash {Id} {bring} {next}"));
        }

        void OnPays(List<(byte player, int cents, int hand)> pays, bool badPay, long now)
        {
            int hand = pays[0].hand;
            if (badPay || pays.Count > TableMsg.MaxSeats || pays.Any(x => x.hand != hand) || pays.Select(x => x.player).Distinct().Count() != pays.Count
                || pays.Sum(x => (long)x.cents) != 0) { Note = "a settlement that doesn't add up: dropped"; return; }
            if (!_declared.TryGetValue(hand, out int risked) || _settled.Contains(hand)) return;   // a hand we brought nothing to, or paid already
            if (now - _paidMs < PaidMinMs) { Note = $"hand {hand}: paid {now - _paidMs} ms after the last: too soon, waiting for a repeat"; return; }
            _settled.Add(hand);
            _paidMs = now;
            foreach (var x in pays.Where(x => x.player == Me))
            {
                if (x.cents < 0 && -x.cents > risked) Note = $"hand {hand}: asked for {TableMsg.Dollars(-x.cents)}, more than the {TableMsg.Dollars(risked)} we brought: refused";
                else if (x.cents > 0 && (_left || x.cents > (TableMsg.MaxSeats - 1) * TableMsg.CashCap(Stakes))) Note = $"hand {hand}: a win we can't have: refused";
                else if (x.cents != 0) Paid.Add(x.cents);
            }
            if (_left && !Unsettled) Close("left");
        }

        /// <summary>Our move: the action of one of Options.</summary>
        public void Act(string action, long now)
        {
            if (Done || !Seated || string.IsNullOrEmpty(action)) return;
            if (!IAmHost) { Out.Add((Host, $"act {Id} {action}")); return; }
            int seat = _hand.IndexOf(Me);
            if (_phase == Phase.Playing && seat >= 0) _rules.Act(seat, action);
        }

        /// <summary>We get up, forfeiting a hand we're in. The host's table ends with it (paying that hand when the forfeit
        /// ends it, else it's void); a player stays Settling until that hand's pay comes (see PlayerTick).</summary>
        public void Leave(long now, string why = "closed")
        {
            if (Done) return;
            if (IAmHost)
            {
                int mine = _hand.IndexOf(Me);
                if (_phase == Phase.Playing && mine >= 0) { _rules.Forfeit(mine); if (_rules.HandOver) Settle(now); }   // walking off never voids a hand we're losing
                End(Array.IndexOf(TableMsg.EndReasons, why) >= 0 ? why : "closed");
                return;
            }
            Out.Add((Host, "leave " + Id));
            _left = true;
            _leftMs = now;
            if (!Unsettled) Close("left");
        }

        /// <summary>Back to a table we got up from while its pay may still come: the same run, so no hand is paid twice
        /// and the one we forfeited is still paid. False when it's over.</summary>
        public bool Rejoin(long now)
        {
            if (Done || !_left || IAmHost) return false;
            _left = _wasSeated = _leaveAgain = false;
            _joinMs = _joinSaidMs = _heardMs = now;
            Out.Add((Host, "join " + Id));
            return true;
        }

        /// <summary>Someone left the session: the host leaving ends it; a seated player leaving forfeits.</summary>
        public void PlayerLeft(byte id, long now)
        {
            if (Done) return;
            if (id == Host) Close("host left");
            else if (IAmHost) Unseat(id);
        }

        public IList<(string label, string action)> Options =>
            Done || !Seated || LastPublic == null ? new List<(string, string)>() : Safe(() => Game.Options(Me), new List<(string, string)>());

        /// <summary>The panel: the table's title, waiting/cash lines, then the game's own.</summary>
        public IList<(string text, int tone)> Lines(Func<byte, string> nameOf)
        {
            var l = new List<(string, int)> { ($"{nameOf(Host)}'s {Game.Title} table, {TableMsg.Dollars(Stakes)} stakes", 3) };
            if (Seats.Count < Game.MinPlayers) l.Add(($"Waiting for players ({Seats.Count} of {Game.MinPlayers}): join from the co-op menu", 2));
            else if (LastPublic == null) l.Add(("Dealing...", 2));
            if (Seated && Bring < Stakes) l.Add(($"You need {TableMsg.Dollars(Stakes)} in cash to play a hand", 3));
            if (LastPublic != null) l.AddRange(Safe(() => Game.Lines(Me, nameOf), new List<(string, int)>()).Take(8));
            return l;
        }

        /// <summary>Out merged into notices: lines for the same player joined by \n up to the notice size; then cleared.</summary>
        public List<(byte to, string text)> Notices()
        {
            var res = new List<(byte, string)>();
            foreach (var g in Out.GroupBy(m => m.to))
            {
                var sb = new StringBuilder();
                foreach (var m in g)
                {
                    if (sb.Length > 0 && Encoding.UTF8.GetByteCount(sb.ToString()) + 1 + Encoding.UTF8.GetByteCount(m.text) > NoticeMsg.MaxBytes) { res.Add((g.Key, sb.ToString())); sb.Clear(); }
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(m.text);
                }
                if (sb.Length > 0) res.Add((g.Key, sb.ToString()));
            }
            Out.Clear();
            return res;
        }

        // A game's view parses strangers' text: whatever it throws is a dropped line, never the script's tick.
        static void Safe(Action a) { try { a(); } catch (Exception) { } }
        static T Safe<T>(Func<T> f, T fallback) where T : class { try { return f() ?? fallback; } catch (Exception) { return fallback; } }
    }
}
