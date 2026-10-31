// Guests at the game's own poker (0.9.0-beta.8.4): the game side of src/Net/GamePoker.cs. The HOST is whoever sits at
// one of the story's poker tables and plays it the usual way (hold E): its game announces the table, gives a guest
// within 10 m one of the card players' seats, holds that seat's action inbox in poker_sp empty on its turns (the card
// player's AI choice is wiped; the dealer waits) and writes the guest's move there; it sends the guest its seat's view
// (its own two cards, everyone's stacks and bets, the board; no one else's cards until they're shown). The GUEST picks
// "Take a seat at <host>'s poker table" in the co-op menu, sees its cards and a panel, and its moves in a pick-list on
// its turn. It plays the seat's table chips, not its own money (first version). 40 s silent, or getting up on its turn:
// it folds. Statics from the 1491.50 map of poker_sp (confirmed in game 26 Oct): seats at 1444 + 56 per seat (stack +2,
// bet +4, status +6, cards +8..+11), board 1420, turn 1410, the inbox 1917 + 2 per seat, the player's seat 137,
// deck-next 2115.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2.Native;
using RDR2Coop.Net;
using LinkState = RDR2Coop.Net.LinkState;
using Screen = RDR2.UI.Screen;

namespace RDR2Coop.Mod
{
    sealed class GamePoker
    {
        const uint Script = 0x012A452C;   // joaat("poker_sp")
        const int Seat0 = 1444, Stride = 56, Board = 1420, Turn = 1410, Inbox = 1917, HumanSeat = 137, DeckNext = 2115;
        const int AdEveryMs = 3000, StateMinMs = 300, StateEveryMs = 3000, ForgetAdMs = 9000, Top = 210;
        const float JoinRange = 10f, LeaveRange = 15f;

        ServerLink _link;
        long _now;
        Vec3 _myPos;

        // ---- host ----
        sealed class Guest { public byte Id; public SeatGate Gate; public string LastSent; public long SentMs; public bool Leaving; }
        readonly Dictionary<byte, Guest> _guests = new Dictionary<byte, Guest>();
        long _adMs = -AdEveryMs;
        int _bigBlind, _lastDeckNext = -1;
        readonly ulong[] _seats = new ulong[Stride * GamePokerMsg.Seats], _board = new ulong[10], _one = new ulong[1];

        // ---- guest ----
        sealed class Ad { public byte Host; public Vec3 At; public int Free; public long HeardMs; }
        readonly Dictionary<byte, Ad> _ads = new Dictionary<byte, Ad>();
        byte _host;            // the host whose table we sit at (0: none)
        Vec3 _at;
        bool _asked;
        long _askedMs;
        int _seat = -1;
        readonly GamePokerView _view = new GamePokerView();
        int _shownVersion = -1;
        PickList _moves;

        static bool Connected(ServerLink link) => link != null && link.State == LinkState.Connected;
        static bool Running => SCRIPTS.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(Script) > 0;
        public bool Seated => _host != 0 && _seat >= 0;

        // ---- the co-op menu (guest) ----

        public string MenuLabel(ServerLink link)
        {
            if (!Connected(link)) return null;
            if (_host != 0) return _view.MyTurn ? "Your move at the poker table" : $"Leave {link.NameOf(_host)}'s poker table";
            var ad = Near();
            return ad == null ? null : $"Take a seat at {link.NameOf(ad.Host)}'s poker table";
        }

        public void MenuAct(ServerLink link)
        {
            if (!Connected(link)) return;
            if (_host != 0 && _view.MyTurn) { ShowMoves(); return; }
            if (_host != 0) { Leave("you got up"); return; }
            var ad = Near();
            if (ad == null) return;
            _host = ad.Host; _at = ad.At; _seat = -1; _asked = true; _askedMs = _now;
            link.SendNotice(_host, NoticeKind.GamePoker, "join");
            CoopScript.Log($"game poker: asking {link.NameOf(_host)} for a seat at their table");
        }

        Ad Near() => _ads.Values.Where(a => a.Free > 0 && _now - a.HeardMs < ForgetAdMs && Vec3.Distance(a.At, _myPos) <= JoinRange)
                               .OrderBy(a => Vec3.Distance(a.At, _myPos)).FirstOrDefault();

        // ---- every tick (every frame) ----

        public void Tick(ServerLink link, Vec3 myPos, Func<byte, Vec3?> posOf, long now)
        {
            _link = link; _myPos = myPos; _now = now;
            if (!Connected(link)) return;
            HostTick(posOf);
            GuestTick();
        }

        void HostTick(Func<byte, Vec3?> posOf)
        {
            if (_host != 0) return;   // we sit at someone else's table: never a host as well
            if (!Running || !ScriptMemory.Check(ScriptMemory.GameDir()))
            {
                if (_guests.Count > 0) { foreach (var g in _guests.Keys) _link.SendNotice(g, NoticeKind.GamePoker, "leave"); _guests.Clear(); CoopScript.Log("game poker: our poker ended: guests told"); }
                return;
            }
            if (ScriptMemory.Read(Script, Seat0, _seats) <= 0) return;
            ScriptMemory.Read(Script, Turn, _one);
            int turn = I(_one[0]);
            // The gates: every frame, before anything else, so the card player's AI choice never reaches the dealer.
            foreach (var g in _guests.Values.ToList())
            {
                var w = g.Gate.Frame(turn, _now);
                if (w != null) ScriptMemory.Write(Script, Inbox + 2 * g.Gate.Seat, new ulong[] { (ulong)(uint)w.Value.type, (ulong)(uint)w.Value.amount });
                if (g.Leaving && turn != g.Gate.Seat) { _guests.Remove(g.Id); CoopScript.Log($"game poker: {_link.NameOf(g.Id)}'s seat {g.Gate.Seat} is the card player's again"); }
            }
            // The big blind: the highest bet just after a new deal (deck-next back from 0).
            ScriptMemory.Read(Script, DeckNext, _one);
            int deckNext = I(_one[0]);
            if (_lastDeckNext == 0 && deckNext > 0) _bigBlind = Enumerable.Range(0, GamePokerMsg.Seats).Max(s => I(_seats[Stride * s + 4]));
            _lastDeckNext = deckNext;
            if (_now - _adMs >= AdEveryMs)
            {
                _adMs = _now;
                var me = CoopScript.ToNet(Peds.Position(PLAYER.PLAYER_PED_ID()));
                _link.SendNotice(Protocol.Broadcast, NoticeKind.GamePoker, string.Format(System.Globalization.CultureInfo.InvariantCulture, "table {0:0.0} {1:0.0} {2:0.0} {3}", me.X, me.Y, me.Z, FreeSeats().Count));
            }
            foreach (var g in _guests.Values.Where(x => !x.Leaving))
            {
                var p = posOf(g.Id);
                var me = CoopScript.ToNet(Peds.Position(PLAYER.PLAYER_PED_ID()));
                if (p == null || Vec3.Distance(p.Value, me) > LeaveRange + 5f) { Release(g, "walked away"); continue; }
                int b0 = Stride * g.Gate.Seat;
                if (I(_seats[b0 + 2]) <= 0 && I(_seats[b0 + 6]) == -1 && turn != g.Gate.Seat)   // its card player got up (out of chips, or the table's turnover)
                {
                    _link.SendNotice(g.Id, NoticeKind.GamePoker, "leave");
                    Release(g, "the card player in that seat left the table");
                    continue;
                }
                string state = StateFor(g.Gate.Seat, turn);
                if (state != g.LastSent && _now - g.SentMs >= StateMinMs || _now - g.SentMs >= StateEveryMs)
                {
                    g.LastSent = state; g.SentMs = _now;
                    _link.SendNotice(g.Id, NoticeKind.GamePoker, state);
                }
            }
        }

        static int I(ulong v) => unchecked((int)(uint)v);
        int Human() { ScriptMemory.Read(Script, HumanSeat, _one); return I(_one[0]); }

        // Card players' seats a guest can take: someone sits there (chips), not our own seat, not a guest's.
        List<int> FreeSeats()
        {
            int human = Human();
            return Enumerable.Range(0, GamePokerMsg.Seats).Where(s => s != human && I(_seats[Stride * s + 2]) > 0 && !_guests.Values.Any(g => g.Gate.Seat == s)).ToList();
        }

        string StateFor(int seat, int turn)
        {
            ScriptMemory.Read(Script, Board, _board);
            var board = Enumerable.Range(0, 5).Select(k => GamePokerMsg.Card(I(_board[2 * k]), I(_board[2 * k + 1]))).ToList();
            var seats = Enumerable.Range(0, GamePokerMsg.Seats).Select(s =>
            {
                int b = Stride * s, status = I(_seats[b + 6]);
                string c1 = GamePokerMsg.Card(I(_seats[b + 8]), I(_seats[b + 9])), c2 = GamePokerMsg.Card(I(_seats[b + 10]), I(_seats[b + 11]));
                bool show = (s == seat || status == 2) && c1 != "-" && c2 != "-";   // its own cards, or a hand shown at the showdown
                return new GamePokerMsg.Seat { Stack = Math.Max(0, I(_seats[b + 2])), Bet = Math.Max(0, I(_seats[b + 4])), Status = Math.Max(-1, Math.Min(3, status)), Cards = show ? c1 + c2 : "-" };
            }).ToList();
            return GamePokerMsg.State(turn < 0 || turn >= GamePokerMsg.Seats ? -1 : turn, seat, _bigBlind, board, seats);
        }

        void Release(Guest g, string why)
        {
            if (g.Gate.Waiting) g.Gate.Move(GamePokerMsg.Fold, 0);   // its turn: it folds, or the table would wait for ever
            g.Leaving = true;
            CoopScript.Log($"game poker: {_link.NameOf(g.Id)} left seat {g.Gate.Seat} ({why})");
        }

        // ---- the guest's side, every tick ----

        void GuestTick()
        {
            foreach (var k in _ads.Where(kv => _now - kv.Value.HeardMs > ForgetAdMs * 3).Select(kv => kv.Key).ToList()) _ads.Remove(k);
            if (_host == 0) return;
            if (_asked && _seat < 0 && _now - _askedMs > 6000) { Say("Co-op: no seat at the poker table (the host didn't answer)"); Drop(); return; }
            if (_seat < 0) return;
            if (Vec3.Distance(_myPos, _at) > LeaveRange) { Leave("you walked away from the poker table"); return; }
            if (_view.MyTurn && _shownVersion != _view.Version && !PickList.IsOpen) ShowMoves();
            if (!_view.MyTurn && _moves != null && PickList.Current == _moves) { PickList.Close(); _moves = null; }
            if (MissionProbe.InMissionScene || HUD.IS_PAUSE_MENU_ACTIVE() || CAM.IS_SCREEN_FADED_OUT()) return;
            var lines = new List<(string, System.Drawing.Color)> { ($"{_link.NameOf(_host)}'s poker table (the table's chips)", Tone.Of(3)) };
            lines.AddRange(_view.Lines(i => $"Card player {i + 1}").Select(l => (l.text, Tone.Of(l.tone))));
            float width = Math.Min(640f, 32f + 10f * lines.Max(l => l.Item1.Length));
            Ui.Panel(lines, width, 1264f - width, Top);
            DrawMyCards();
        }

        void DrawMyCards()
        {
            if (_view.You < 0 || _view.Seats.Count != GamePokerMsg.Seats) return;
            string cards = _view.Seats[_view.You].Cards;
            if (cards == null || cards.Length != 4) return;
            TXD.REQUEST_STREAMED_TEXTURE_DICT(PokerCards.FaceDict, false);
            if (!TXD.HAS_STREAMED_TEXTURE_DICT_LOADED(PokerCards.FaceDict)) return;
            const float h = 0.13f, w = h * 0.7f * 9f / 16f;
            for (int c = 0; c < 2; c++)
                GRAPHICS.DRAW_SPRITE(PokerCards.FaceDict, GamePokerMsg.Face(cards.Substring(2 * c, 2)), 0.5f + (c - 0.5f) * (w + 0.008f), 0.86f, w, h, 0f, 255, 255, 255, 255, false);
        }

        void ShowMoves()
        {
            _shownVersion = _view.Version;
            var opts = _view.Options();
            if (opts.Count == 0) return;
            var rows = opts.Select(o => (o.label, (Action)(() => Send(o.action)))).ToList();
            rows.Add(("Leave the table", () => Leave("you got up")));
            PickList.Open("Poker: your move", rows, null, "40 s to choose, or you fold");
            _moves = PickList.Current;
        }

        void Send(string action)
        {
            if (_host == 0 || !_view.MyTurn) return;
            _link.SendNotice(_host, NoticeKind.GamePoker, "act " + action);
            CoopScript.Log($"game poker: our move {action}");
        }

        void Leave(string why)
        {
            if (_host != 0 && Connected(_link)) _link.SendNotice(_host, NoticeKind.GamePoker, "leave");
            Say("Co-op: " + why);
            Drop();
        }

        void Drop()
        {
            if (_moves != null && PickList.Current == _moves) PickList.Close();
            _moves = null; _host = 0; _seat = -1; _asked = false; _shownVersion = -1;
            TXD.SET_STREAMED_TEXTURE_DICT_AS_NO_LONGER_NEEDED(PokerCards.FaceDict);
        }

        // ---- notices (both sides) ----

        public void OnNotice(byte from, string text, Func<byte, Vec3?> posOf)
        {
            if (_link == null || !_link.Roster.Any(e => e.Id == from) || string.IsNullOrEmpty(text) || text.Length > NoticeMsg.MaxBytes) return;
            int sp = text.IndexOf(' ');
            string verb = sp < 0 ? text : text.Substring(0, sp), rest = sp < 0 ? "" : text.Substring(sp + 1);
            switch (verb)
            {
                case "table":   // a host's ad
                    var t = rest.Split(' ');
                    if (t.Length == 4 && float.TryParse(t[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                        && float.TryParse(t[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y)
                        && float.TryParse(t[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z)
                        && int.TryParse(t[3], out int free) && free >= 0 && free <= GamePokerMsg.Seats)
                        _ads[from] = new Ad { Host = from, At = new Vec3(x, y, z), Free = free, HeardMs = _now };
                    break;
                case "join":    // host: a guest wants a seat
                    if (!Running || !ScriptMemory.Ready) { _link.SendNotice(from, NoticeKind.GamePoker, "seat -1"); return; }
                    if (_guests.TryGetValue(from, out var already) && !already.Leaving) { _link.SendNotice(from, NoticeKind.GamePoker, "seat " + already.Gate.Seat); return; }
                    var p = posOf(from);
                    var me = CoopScript.ToNet(Peds.Position(PLAYER.PLAYER_PED_ID()));
                    var seats = FreeSeats();
                    if (p == null || Vec3.Distance(p.Value, me) > JoinRange + 5f || seats.Count == 0) { _link.SendNotice(from, NoticeKind.GamePoker, "seat -1"); return; }
                    int seat = seats[0];
                    _guests[from] = new Guest { Id = from, Gate = new SeatGate(seat), SentMs = -StateEveryMs };
                    _link.SendNotice(from, NoticeKind.GamePoker, "seat " + seat);
                    CoopScript.Log($"game poker: {_link.NameOf(from)} takes card player seat {seat}");
                    Say($"Co-op: {_link.NameOf(from)} takes a card player's seat at your table");
                    break;
                case "act":     // host: a guest's move
                    if (!_guests.TryGetValue(from, out var g) || g.Leaving || !GamePokerMsg.ParseAct(rest, out int type, out int amount)) return;
                    ScriptMemory.Read(Script, Turn, _one);
                    if (I(_one[0]) != g.Gate.Seat) return;   // not its turn (late or hostile)
                    if (type == GamePokerMsg.Raise)   // never more than it has
                    {
                        int b = Stride * g.Gate.Seat, stack = I(_seats[b + 2]), bet = I(_seats[b + 4]);
                        int maxBet = Enumerable.Range(0, GamePokerMsg.Seats).Max(s => I(_seats[Stride * s + 4]));
                        amount = Math.Min(amount, Math.Max(0, stack - (maxBet - bet)));
                        if (amount <= 0) { type = GamePokerMsg.Call; amount = 0; }
                    }
                    g.Gate.Move(type, amount);
                    CoopScript.Log($"game poker: {_link.NameOf(from)} (seat {g.Gate.Seat}) moves {type} {amount}");
                    break;
                case "leave":
                    if (_guests.TryGetValue(from, out var gone) && !gone.Leaving) Release(gone, "got up");   // host side
                    if (from == _host) { Say($"Co-op: {_link.NameOf(_host)}'s poker game is over"); Drop(); }   // guest side
                    break;
                case "seat":    // guest: the host's answer
                    if (from != _host || !int.TryParse(rest, out int n)) return;
                    _asked = false;
                    if (n < 0 || n >= GamePokerMsg.Seats) { Say("Co-op: no seat for you at that poker table"); Drop(); return; }
                    _seat = n;
                    Say($"Co-op: you play a card player's seat at {_link.NameOf(_host)}'s table (the table's chips)");
                    CoopScript.Log($"game poker: we play seat {n} at {_link.NameOf(_host)}'s table");
                    break;
                case "state":   // guest: our seat's view
                    if (from == _host && _seat >= 0) _view.OnState(rest);
                    break;
            }
        }

        public void OnRoster(HashSet<byte> ids)
        {
            foreach (var g in _guests.Values.Where(g => !ids.Contains(g.Id) && !g.Leaving).ToList()) Release(g, "left the session");
            foreach (var k in _ads.Keys.Where(k => !ids.Contains(k)).ToList()) _ads.Remove(k);
            if (_host != 0 && !ids.Contains(_host)) { Say("Co-op: the poker host left"); Drop(); }
        }

        public void Clear()
        {
            if (_host != 0) Drop();
            foreach (var g in _guests.Values) if (g.Gate.Waiting) ScriptMemory.Write(Script, Inbox + 2 * g.Gate.Seat, new ulong[] { GamePokerMsg.Fold, 0 });
            _guests.Clear();
            _ads.Clear();
        }

        static void Say(string text)
        {
            text = new string(text.Where(c => c != '~' && c != '<' && c != '>').ToArray());
            if (!TableGames.Hold(text)) Screen.DisplaySubtitle(text);
        }
    }
}
