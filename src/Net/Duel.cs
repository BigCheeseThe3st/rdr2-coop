// A quick-draw duel (0.9.0, [Games] Duel), a Contest of kind "duel": the host picks one partner whose stand-in is
// within 20 m. params: "<challenger> <opponent> <pauseMs>" (the host, the one it picked, and the pause before DRAW,
// 1-3 s, picked at random by the host). Anyone else near is in too, but only watches.
// At GO both duelists hear "Holster and face <name>": 2 s, then the pause, then DRAW! (each game counts from its
// own GO). Until our DRAW our game keeps attack and aim off; a shot of ours that leaves aimed at the other's
// stand-in before our DRAW anyway is "early" (we're out, they win). After it, our game times our first shot aimed
// at their stand-in from our own DRAW and tells the host "shot <ms>", again every second until the end. Both are
// timed against their own DRAW, so the network's delay favours nobody: the host waits up to 1.5 s after the first
// shot arrives for the other's, and the lower time wins; nobody hit within 30 s of DRAW: nobody wins.
// Why the shooter's game says it and not the one hit: a partner's shot at a stand-in never makes a Hit packet (the
// area's owner drops its own player's damage on stand-ins as friendly fire, and a guest's game catches no hits).
// Standings: "<a> <b> on". Results: "<a> <b> won <id> <ms>", "<a> <b> early <id>" (id drew too soon, the other
// wins), "<a> <b> left <id>", "<a> <b> time" (nobody hit) or "<a> <b> none" (the opponent wasn't in at GO).
// The game side (the controls, the shot, the cues, the fall) is src/Mod/Duels.cs.
using System;
using System.Collections.Generic;
using System.Linq;

namespace RDR2Coop.Net
{
    public sealed class Duel : Contest
    {
        public const float RangeM = 20f;
        public const int FaceMs = 2000, MinPauseMs = 1000, MaxPauseMs = 3000, WaitMs = 1500, ShootMs = 30_000, EveryMs = 1000, SlackMs = 100;
        public const string DrawCue = "DRAW!";

        public override string Kind => "duel";

        byte _a, _b;            // the challenger (the host) and the opponent
        bool _valid;
        long _startMs = -1, _drawAt;   // our GO and our DRAW (the host's own are its yardstick for reports)

        // The host's.
        string _result;
        readonly Dictionary<byte, (int ms, long at)> _shots = new Dictionary<byte, (int, long)>();

        // Our own duel.
        int _shotMs = -1, _cue;
        long _sentMs = -EveryMs;
        bool _early, _ended, _fall;
        string _verdict = "";

        public static string Params(byte challenger, byte opponent, int pauseMs) => $"{challenger} {opponent} {pauseMs}";

        public static bool TryParse(string prms, out byte a, out byte b, out int pauseMs)
        {
            a = b = 0; pauseMs = 0;
            var p = (prms ?? "").Split(' ');
            return p.Length == 3 && byte.TryParse(p[0], out a) && byte.TryParse(p[1], out b) && int.TryParse(p[2], out pauseMs)
                && a != 0 && b != 0 && a != b && a != Protocol.Broadcast && b != Protocol.Broadcast && pauseMs >= MinPauseMs && pauseMs <= MaxPauseMs;
        }

        void Read(string prms, long now)
        {
            _valid = TryParse(prms, out _a, out _b, out int pause);
            _drawAt = now + FaceMs + pause;
        }

        // ---- the host ----
        public override void Begin(IList<byte> players, string prms, long now)
        {
            Read(prms, now);
            if (!_valid || _a != Me || !players.Contains(_b)) _result = $"{_a} {_b} none";
        }

        public override void OnReport(byte from, string data, long now)
        {
            if (_result != null || (from != _a && from != _b)) return;
            if (data == "early") { _result = $"{_a} {_b} early {from}"; return; }
            var p = (data ?? "").Split(' ');
            if (p.Length != 2 || p[0] != "shot" || !int.TryParse(p[1], out int ms) || ms < 0 || _shots.ContainsKey(from)) return;
            if (ms > now - _drawAt + SlackMs) return;   // timed from a DRAW that can't have come yet (no game's comes before ours)
            _shots[from] = (ms, now);
            if (_shots.ContainsKey(from == _a ? _b : _a)) Decide();
        }

        public override void Tick(long now)
        {
            if (_result != null || !_valid) return;
            if (_shots.Count > 0) { if (now - _shots.Values.Min(s => s.at) >= WaitMs) Decide(); }
            else if (now - _drawAt >= ShootMs) _result = $"{_a} {_b} time";
        }

        void Decide()
        {
            var w = _shots.OrderBy(kv => kv.Value.ms).ThenBy(kv => kv.Value.at).First();
            _result = $"{_a} {_b} won {w.Key} {w.Value.ms}";
        }

        public override bool Over => _result != null;
        public override string Standings() => $"{_a} {_b} on";
        public override string Results() => _result;
        public override void Left(byte player, long now) { if (_result == null && (player == _a || player == _b)) _result = $"{_a} {_b} left {player}"; }

        // ---- every player ----
        public override void Start(string prms, long now) { _startMs = now; Read(prms, now); }

        public bool Started => _startMs >= 0;
        bool Mine => _valid && (Me == _a || Me == _b);
        /// <summary>We're one of the two and it isn't over: the game side holds our gun until DRAW and watches our shot.</summary>
        public bool Dueling => Started && !_ended && Mine;
        public byte Foe => Me == _a ? _b : _a;
        public bool Drawn(long now) => now >= _drawAt;

        /// <summary>The game side: a shot of ours just left aimed at the other's stand-in.</summary>
        public void Shot(long now)
        {
            if (!Dueling || _early || _shotMs >= 0) return;
            if (now < _drawAt) _early = true; else _shotMs = (int)Math.Min(now - _drawAt, ShootMs);
            _sentMs = -EveryMs;
        }

        public override string Report(long now)
        {
            if (!Dueling || (!_early && _shotMs < 0) || now - _sentMs < EveryMs) return null;
            _sentMs = now;
            return _early ? "early" : "shot " + _shotMs;
        }

        /// <summary>A subtitle when something happens (face them, DRAW!, the verdict), else null.</summary>
        public string Cue(long now)
        {
            if (!Started || !_valid) return null;
            int step = _ended ? 3 : now >= _drawAt ? 2 : 1;
            if (step <= _cue) return null;
            _cue = step;
            if (step == 3) return "Co-op: duel over: " + _verdict;
            if (step == 2) return Mine ? DrawCue : null;
            return Mine ? $"Holster and face {NameOf(Foe)}" : $"Co-op: {NameOf(_a)} and {NameOf(_b)} face off";
        }

        public override IList<(string text, int tone)> Lines(string text, long now)
        {
            string title = Mine ? $"DUEL vs {NameOf(Foe)}" : $"DUEL: {NameOf(_a)} vs {NameOf(_b)}";
            int tone = 3;
            string state;
            if (_ended) { byte w = Winner(text); state = _verdict; tone = w != 0 && (w == Me || !Mine) ? 4 : 2; }
            else if (now < _startMs + FaceMs) state = "holster and face them";
            else if (now < _drawAt) state = "steady...";
            else state = !Mine ? "DRAW!" : _early ? "you drew too soon" : _shotMs >= 0 ? $"you fired in {Sec(_shotMs)}" : "DRAW!";
            return new List<(string, int)> { ($"{title}: {state}", tone) };
        }

        public override void End(string results)
        {
            _ended = true;
            var p = Tokens(results);
            byte.TryParse(p.Length > 3 ? p[3] : "", out byte id);
            string verb = p.Length > 2 ? p[2] : "", who = id == Me ? "you" : NameOf(id);
            byte win = Winner(results);
            string time = p.Length > 4 && int.TryParse(p[4], out int ms) && ms >= 0 && ms <= ShootMs ? " in " + Sec(ms) : "";
            _verdict = verb == "won" && win != 0 ? (win == Me ? "you won" : Mine ? who + " beat you" : who + " won") + time
                     : verb == "early" && win != 0 ? (id == Me ? $"you drew too soon: {NameOf(win)} wins" : $"{who} drew too soon: {(win == Me ? "you win" : NameOf(win) + " wins")}")
                     : verb == "left" && id != 0 ? $"{who} walked away"
                     : verb == "time" ? "nobody hit: a draw"
                     : verb == "none" ? $"{NameOf(_b)} didn't accept"
                     : "it's off";
            _fall = Mine && verb == "won" && win == Foe;   // shot down: the game side knocks us over
        }

        /// <summary>True once, when we lost to a shot.</summary>
        public bool TakeFall() { bool f = _fall; _fall = false; return f; }

        public override byte Winner(string results)
        {
            var p = Tokens(results);
            if (p.Length < 4 || !byte.TryParse(p[0], out byte a) || !byte.TryParse(p[1], out byte b) || !byte.TryParse(p[3], out byte id) || a == b || (id != a && id != b)) return 0;
            return p[2] == "won" ? id : p[2] == "early" ? (id == a ? b : a) : (byte)0;
        }

        static string[] Tokens(string text) => (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        static string Sec(int ms) => (ms / 1000f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s";
    }
}
