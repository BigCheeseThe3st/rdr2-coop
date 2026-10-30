// Poker at one of the story's own poker tables (0.9.0-beta.8.4). The hand is the mod's (src/Net/Poker.cs, run by the
// table's host like any mod table); this is only its look, made by each player's game for itself: the table's seats
// (the game's MINIGAME_POKER_PLAYER scenario points, put in the same order in every game, so seat k is the same chair
// for all), the co-op players sitting in them (the table's own card player in a chair one of us takes is hidden while
// we play), the board as the game's card models on the table, face-down cards in front of everyone still in, the
// cards turned up at a showdown, and our own two on screen with the game's card faces. The game's own poker script
// isn't started or touched. Every game finds the table from the mod table's spot, so nothing new goes over the wire.
// Natives only; everything made here goes in Clear.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class RealPoker
    {
        static readonly uint SeatType = MISC.GET_HASH_KEY("MINIGAME_POKER_PLAYER"), CampSeatType = MISC.GET_HASH_KEY("MINIGAME_POKER_CAMP_PLAYER");
        // How we sit in a chair: the table's own poker pose first, then plain chairs (the first that loads).
        static readonly string[] Sits = { "MINIGAME_POKER_PLAYER", "PROP_HUMAN_SEAT_CHAIR_GENERIC", "PROP_HUMAN_SEAT_CHAIR" };
        const float TableAcross = 3.2f;   // seats further apart than this aren't one table
        const float CardGap = 0.085f, InFront = 0.42f;

        sealed class Seat { public int Point; public Vector3 At; public float Heading; public int Ped; }   // Point 0: a chair found by its card player (Ped)

        readonly List<Seat> _seats;
        readonly string _deck;
        public readonly Vector3 Centre;
        readonly float _top;   // the table top's height
        readonly Dictionary<string, int> _props = new Dictionary<string, int>();   // what lies on the table: key -> object
        readonly HashSet<int> _hidden = new HashSet<int>();
        readonly Dictionary<int, (int chair, long since)> _away = new Dictionary<int, (int, long)>();   // card players we sent off
        readonly HashSet<int> _hiddenProps = new HashSet<int>();
        readonly HashSet<uint> _seenModels = new HashSet<uint>();
        HashSet<uint> _theirModels;
        long _propsCheckMs;
        readonly HashSet<uint> _models = new HashSet<uint>();
        int _shownVer = -1;
        uint _sit;
        int _sitTry = -1;
        long _sitTryMs;
        bool _loggedFaces;

        RealPoker(List<Seat> seats, Vector3 centre, float top)
        {
            _seats = seats; Centre = centre; _top = top;
            _deck = PokerCards.DeckFor(CoopScript.ToNet(centre));
        }

        public int SeatCount => _seats.Count;

        /// <summary>The story poker table with a seat within radius of near, or null. log: say what was found (from the menu).</summary>
        public static unsafe RealPoker Find(Vector3 near, float radius, bool log = false)
        {
            var points = new HashSet<int>();
            // 1. The scenario points the game lists here. It fills a script array: slot 0 is its header, the points follow
            // (as RedM's scripts read it), 8 bytes each. A buffer of exactly `max` slots made the game write past it (26 Oct).
            const int Max = 256;   // a saloon has well over 64 points in a few metres (26 Oct: the seats were cut off)
            var buffer = new ulong[Max + 1];
            int n;
            fixed (ulong* found = buffer) n = TASK.GET_SCENARIO_POINTS_IN_AREA(near, radius, found, Max);
            n = Math.Max(0, Math.Min(n, Max));
            var listed = new List<string>();
            for (int i = 1; i <= n; i++)
            {
                int point = (int)buffer[i];
                uint type = TASK._GET_SCENARIO_POINT_TYPE(point);
                if (log) listed.Add($"{type:X8}@{TASK._GET_SCENARIO_POINT_COORDS(point, true).DistanceTo(near):0.0}m");
                if (type == SeatType || type == CampSeatType) points.Add(point);
            }
            // 2. The seats the table's card players sit in (the launcher's own points may not be listed above).
            int fromPeds = 0;
            foreach (int ped in EntityScan.Peds(new[] { near }, radius + TableAcross))
            {
                if (ped == PLAYER.PLAYER_PED_ID() || Peds.Position(ped).DistanceTo(near) > radius + TableAcross) continue;
                if (!PED.IS_PED_USING_SCENARIO_HASH(ped, SeatType) && !PED.IS_PED_USING_SCENARIO_HASH(ped, CampSeatType)) continue;
                int point = TASK._GET_SCENARIO_POINT_PED_IS_USING(ped, true);
                if (point > 0 && points.Add(point)) fromPeds++;
            }
            // 3. The game's nearest poker seat (the free one the table keeps for the player).
            int nearest = TASK.FIND_SCENARIO_OF_TYPE_HASH(near, SeatType, radius + TableAcross, 0, false);
            if (nearest <= 0) nearest = TASK.FIND_SCENARIO_OF_TYPE_HASH(near, CampSeatType, radius + TableAcross, 0, false);
            bool nearestNew = nearest > 0 && points.Add(nearest);
            var seats = new List<Seat>();
            foreach (int point in points)
            {
                var at = TASK._GET_SCENARIO_POINT_COORDS(point, true);
                if (seats.Any(x => x.At.DistanceTo(at) < 0.3f)) continue;   // the same chair twice
                seats.Add(new Seat { Point = point, At = at, Heading = TASK._GET_SCENARIO_POINT_HEADING(point, true) });
            }
            // 4. The card players the table seats with its own animations (they use no scenario point): their chairs
            // ring the table. Its middle: just in front of the free seat the game keeps for the player.
            int fromRing = 0;
            var free = nearest > 0 ? seats.FirstOrDefault(x => x.Point == nearest) : null;
            if (free != null)
            {
                double h = free.Heading * Math.PI / 180.0;
                var middle = free.At + new Vector3((float)-Math.Sin(h), (float)Math.Cos(h), 0f) * 0.95f;
                foreach (int ped in EntityScan.Peds(new[] { middle }, 2.2f))
                {
                    if (ped == PLAYER.PLAYER_PED_ID() || !PED.IS_PED_HUMAN(ped) || Peds.Ours.Contains(ped)) continue;
                    var at = Peds.Position(ped);
                    var flat = new Vector3(at.X - middle.X, at.Y - middle.Y, 0f);
                    float d = flat.Length();
                    if (d < 0.6f || d > 1.6f || Math.Abs(at.Z - free.At.Z) > 0.8f || seats.Any(x => x.At.DistanceTo(at) < 0.5f)) continue;
                    float heading = (float)(Math.Atan2(flat.X, -flat.Y) * 180.0 / Math.PI);   // facing the middle
                    seats.Add(new Seat { Point = 0, At = at, Heading = heading, Ped = ped });
                    fromRing++;
                }
            }
            if (log) CoopScript.Log($"real poker: {n} scenario points here [{string.Join(" ", listed.Take(24))}], {fromPeds} more from seated card players, {fromRing} card players round the table, nearest poker seat {(nearest > 0 ? (nearestNew ? "new" : "known") : "none")}: {seats.Count} chairs, nearest {(seats.Count > 0 ? seats.Min(x => x.At.DistanceTo(near)).ToString("0.0") + " m" : "-")} (types {SeatType:X8} / {CampSeatType:X8})");
            var first = seats.OrderBy(s => s.At.DistanceTo(near)).FirstOrDefault();
            if (first == null || first.At.DistanceTo(near) > radius) return null;
            seats = seats.Where(s => s.At.DistanceTo(first.At) <= TableAcross).ToList();
            if (seats.Count < 2) return null;
            var centre = new Vector3(seats.Average(s => s.At.X), seats.Average(s => s.At.Y), seats.Average(s => s.At.Z));
            // The same order in every game: round the table from north.
            seats = seats.OrderBy(s => Math.Atan2(s.At.X - centre.X, s.At.Y - centre.Y)).ToList();
            float top = centre.Z + 0.8f, z = 0f;
            if (MISC.GET_GROUND_Z_FOR_3D_COORD(centre.X, centre.Y, centre.Z + 2f, &z, false) && z > centre.Z - 1f && z < centre.Z + 2f) top = z;   // from above: the table's top
            return new RealPoker(seats, centre, top);
        }

        // ---- sitting ----

        /// <summary>Every 200 ms while seated: sit in chair k (players beyond the chairs stand there), the card player there out of the way.</summary>
        public void Sit(int k, long now)
        {
            if (_sit != 0 || _sitTry >= Sits.Length || k < 0 || k >= _seats.Count) return;
            if (_sitTry < 0) { _sitTry = 0; _sitTryMs = now; }
            uint scenario = MISC.GET_HASH_KEY(Sits[_sitTry]);
            STREAMING._REQUEST_SCENARIO_TYPE(scenario, 1, 0, 0);
            if (STREAMING._HAS_SCENARIO_TYPE_LOADED(scenario, false))
            {
                var seat = _seats[k];
                int me = PLAYER.PLAYER_PED_ID();
                TASK.TASK_START_SCENARIO_AT_POSITION(me, scenario, seat.At, seat.Heading, -1, true, true, null, -1f, false);
                _sit = scenario;
                CoopScript.Log($"real poker: sitting in chair {k + 1} of {_seats.Count} ({Sits[_sitTry]}, {_deck} deck)");
                return;
            }
            if (now - _sitTryMs < 2000) return;
            STREAMING._REMOVE_SCENARIO_ASSET(scenario);
            _sitTry++;
            _sitTryMs = now;
            if (_sitTry >= Sits.Length) CoopScript.Log("real poker: no sitting scenario loaded: standing at the table");
        }

        public void Stand()
        {
            if (_sit != 0)
            {
                int me = PLAYER.PLAYER_PED_ID();
                if (PED.IS_PED_USING_SCENARIO_HASH(me, _sit)) TASK.CLEAR_PED_TASKS(me, false, false);
                STREAMING._REMOVE_SCENARIO_ASSET(_sit);
            }
            _sit = 0;
            _sitTry = -1;
        }

        // The table's own card player in chair k now (never a player or one of our stand-ins), or 0.
        int OccupantOf(int k, Func<int, bool> ours)
        {
            var seat = _seats[k];
            int ped = seat.Point > 0 ? TASK._GET_PED_USING_SCENARIO_POINT(seat.Point) : 0;
            bool Npc(int p) => p != 0 && p != PLAYER.PLAYER_PED_ID() && Peds.Exists(p) && PED.IS_PED_HUMAN(p) && !PED.IS_PED_A_PLAYER(p) && !ours(p) && !_away.ContainsKey(p);
            if (Npc(ped)) return ped;
            if (Npc(seat.Ped) && Peds.Position(seat.Ped).DistanceTo(seat.At) < 0.8f) return seat.Ped;
            return EntityScan.Peds(new[] { seat.At }, 0.8f).FirstOrDefault(p => Npc(p) && Peds.Position(p).DistanceTo(seat.At) < 0.7f);
        }

        /// <summary>The chairs a card player sits in now: they play as the house's players.</summary>
        public List<int> CardPlayerChairs(Func<int, bool> ours) => Enumerable.Range(0, _seats.Count).Where(k => OccupantOf(k, ours) != 0).ToList();

        // The card players in the chairs we take (0..players-1) stand up and walk off (George, 26 Oct: "make the NPC
        // that was sat in a seat the players take stand up and leave"). One the table's own script sits back down is
        // hidden instead, while we play.
        public void ClearChairs(int players, Func<int, bool> ours, long now)
        {
            for (int k = 0; k < Math.Min(players, _seats.Count); k++)
            {
                int ped = OccupantOf(k, ours);
                if (ped == 0) continue;
                ENTITY.SET_ENTITY_AS_MISSION_ENTITY(ped, true, true);   // ours now: the table's script lets go of it
                TASK.CLEAR_PED_TASKS_IMMEDIATELY(ped, false, true);
                TASK.TASK_WANDER_STANDARD(ped, 10f, 10);
                PED.SET_PED_KEEP_TASK(ped, true);
                _away[ped] = (k, now);
                CoopScript.Log($"real poker: the card player in chair {k + 1} gets up for a partner and leaves");
            }
            foreach (var kv in _away.Where(kv => !_hidden.Contains(kv.Key) && now - kv.Value.since > 6000 && Peds.Exists(kv.Key)
                                              && Peds.Position(kv.Key).DistanceTo(_seats[kv.Value.chair].At) < 0.8f).ToList())
            {
                _hidden.Add(kv.Key);   // sat back down: out of sight
                ENTITY.SET_ENTITY_VISIBLE(kv.Key, false);
                ENTITY.SET_ENTITY_COLLISION(kv.Key, false, false);
                ENTITY.FREEZE_ENTITY_POSITION(kv.Key, true);
                CoopScript.Log($"real poker: the card player from chair {kv.Value.chair + 1} sat back down: hidden while we play");
            }
        }

        // The table's own cards and chips (the card players' game) are out of sight while ours is dealt, once a second.
        public void HideTheirTable(long now)
        {
            if (now - _propsCheckMs < 1000) return;
            _propsCheckMs = now;
            if (_theirModels == null)
            {
                _theirModels = new HashSet<uint>();
                for (int c = 0; c < 52; c++) _theirModels.Add(MISC.GET_HASH_KEY(PokerCards.Model(c, _deck)));
                foreach (var n in new[] { PokerCards.Back(_deck), "p_cards01x", "p_cardssplit01x_" + _deck, "p_chips_val01x", "p_chips05x10_rho", "p_coin01x" })
                    _theirModels.Add(MISC.GET_HASH_KEY(n));
                for (int i = 1; i <= 10; i++) _theirModels.Add(MISC.GET_HASH_KEY("prop_chip_green_x" + i));
            }
            var mine = new HashSet<int>(_props.Values);
            var others = new HashSet<uint>();
            foreach (var o in RDR2.World.GetAllObjects())
            {
                int h = o.Handle;
                if (mine.Contains(h) || _hiddenProps.Contains(h)) continue;
                var at = ENTITY.GET_ENTITY_COORDS(h, true, true);
                float dx = at.X - Centre.X, dy = at.Y - Centre.Y;
                if (dx * dx + dy * dy > 1.3f * 1.3f || Math.Abs(at.Z - _top) > 0.4f) continue;
                uint m = ENTITY.GET_ENTITY_MODEL(h);
                if (!_theirModels.Contains(m)) { if (_seenModels.Add(m)) others.Add(m); continue; }
                ENTITY.SET_ENTITY_VISIBLE(h, false);
                _hiddenProps.Add(h);
            }
            if (others.Count > 0) CoopScript.Log("real poker: other things on the table (left as they are): " + string.Join(" ", others.Select(m => m.ToString("X8"))));
        }

        // ---- the cards ----

        /// <summary>Every tick while seated: the table's cards for this view (chairOf: a seat id's chair), our two on screen.</summary>
        public void Show(Poker view, Func<byte, int> chairOf, byte me)
        {
            if (view.ViewVersion != _shownVer)
            {
                _shownVer = view.ViewVersion;
                var want = new Dictionary<string, (string model, Vector3 at, float heading)>();
                var board = view.Board;
                for (int i = 0; i < board.Count; i++)
                    want["b" + i + ":" + board[i]] = (PokerCards.Model(board[i], _deck), new Vector3(Centre.X + (i - 2) * CardGap, Centre.Y, _top + 0.003f), 0f);
                foreach (var (id, state, cards) in view.SeatView)
                {
                    int k = chairOf(id);
                    if (k < 0 || k >= _seats.Count || state == 'f' || state == 'l') continue;
                    var seat = _seats[k];
                    var toward = Centre - seat.At; toward.Z = 0f;
                    float len = toward.Length();
                    if (len < 0.05f) continue;
                    toward /= len;
                    var side = new Vector3(-toward.Y, toward.X, 0f);
                    float heading = (float)(Math.Atan2(-toward.X, toward.Y) * 180.0 / Math.PI);
                    for (int c = 0; c < 2; c++)
                    {
                        var at = seat.At + toward * InFront + side * ((c - 0.5f) * CardGap);
                        at.Z = _top + 0.003f;
                        bool up = cards != null && cards.Count == 2;
                        want[$"s{id}:{c}:" + (up ? cards[c].ToString() : "back")] = (up ? PokerCards.Model(cards[c], _deck) : PokerCards.Back(_deck), at, heading);
                    }
                }
                foreach (var gone in _props.Keys.Where(k => !want.ContainsKey(k)).ToList()) { Delete(_props[gone]); _props.Remove(gone); }
                foreach (var kv in want.Where(kv => !_props.ContainsKey(kv.Key)))
                {
                    int obj = Place(kv.Value.model, kv.Value.at, kv.Value.heading);
                    if (obj != 0) _props[kv.Key] = obj;
                    else _shownVer = -1;   // its model is still loading: try again next tick
                }
            }
            var mine = view.MyCards;
            if (mine == null || mine.Count != 2) return;
            TXD.REQUEST_STREAMED_TEXTURE_DICT(PokerCards.FaceDict, false);
            if (!TXD.HAS_STREAMED_TEXTURE_DICT_LOADED(PokerCards.FaceDict))
            {
                if (!_loggedFaces) { _loggedFaces = true; CoopScript.Log($"real poker: card faces ({PokerCards.FaceDict}) still loading"); }
                return;
            }
            const float h = 0.13f, w = h * 0.7f * 9f / 16f;   // a card's shape on a 16:9 screen
            for (int c = 0; c < 2; c++)
                GRAPHICS.DRAW_SPRITE(PokerCards.FaceDict, PokerCards.Face(mine[c]), 0.5f + (c - 0.5f) * (w + 0.008f), 0.86f, w, h, 0f, 255, 255, 255, 255, false);
        }

        int Place(string model, Vector3 at, float heading)
        {
            if (model == null) return 0;
            uint m = MISC.GET_HASH_KEY(model);
            if (!STREAMING.IS_MODEL_VALID(m)) { CoopScript.Log($"real poker: no model {model} in this game"); return -1; }
            _models.Add(m);
            STREAMING.REQUEST_MODEL(m, false);
            if (!STREAMING.HAS_MODEL_LOADED(m)) return 0;
            int obj = OBJECT.CREATE_OBJECT(m, at, false, false, false, false, true);
            if (obj == 0) return 0;
            ENTITY.SET_ENTITY_HEADING(obj, heading);
            ENTITY.FREEZE_ENTITY_POSITION(obj, true);
            ENTITY.SET_ENTITY_COLLISION(obj, false, false);
            return obj;
        }

        static unsafe void Delete(int obj)
        {
            if (obj <= 0 || !ENTITY.DOES_ENTITY_EXIST(obj)) return;
            ENTITY.SET_ENTITY_AS_MISSION_ENTITY(obj, true, true);
            ENTITY.DELETE_ENTITY(&obj);
        }

        /// <summary>We got up: our cards off the table, the card players back, nothing of ours left.</summary>
        public void Clear()
        {
            Stand();
            foreach (int obj in _props.Values) Delete(obj);
            _props.Clear();
            foreach (int ped in _hidden.Where(p => ENTITY.DOES_ENTITY_EXIST(p)))
            {
                ENTITY.FREEZE_ENTITY_POSITION(ped, false);
                ENTITY.SET_ENTITY_COLLISION(ped, true, true);
                ENTITY.SET_ENTITY_VISIBLE(ped, true);
            }
            _hidden.Clear();
            foreach (int ped in _away.Keys.ToList())   // the ones that walked off: the game's again, to tidy up when it likes
            {
                int h = ped;
                if (!ENTITY.DOES_ENTITY_EXIST(h)) continue;
                unsafe { ENTITY.SET_PED_AS_NO_LONGER_NEEDED(&h); }
            }
            _away.Clear();
            foreach (int obj in _hiddenProps.Where(o => ENTITY.DOES_ENTITY_EXIST(o))) ENTITY.SET_ENTITY_VISIBLE(obj, true);
            _hiddenProps.Clear();
            foreach (uint m in _models) STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED(m);
            _models.Clear();
            TXD.SET_STREAMED_TEXTURE_DICT_AS_NO_LONGER_NEEDED(PokerCards.FaceDict);
            _shownVer = -1;
        }
    }
}
