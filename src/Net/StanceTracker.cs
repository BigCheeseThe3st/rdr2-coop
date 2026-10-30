// When a partner's stand-in copies a move of theirs (0.8.0, [Visuals] Stance): a jump or a climb as it
// starts, going limp when they're knocked over or fall a long way, and a look a second after a move
// ends at whether it got left on another level (below the wall they climbed, up where they jumped
// from). A move starts only when the stand-in is near them and no other move is playing, so wrong or
// hostile flags can't make it twitch; while one plays, steering waits (the follow code still snaps it
// if it falls far behind, so it is never stuck). Pure logic, so it can be tested.
using System;

namespace RDR2Coop.Net
{
    [Flags]
    public enum StanceStep : byte
    {
        None = 0,
        Jump = 1,         // jump once
        Climb = 2,        // climb or vault what's in front of it
        Ragdoll = 4,      // go limp for a moment
        CheckLevel = 8,   // a move ended a second ago: put it beside them if it's on another level
        Hold = 16,        // a move plays: no walking orders
    }

    public sealed class StanceTracker
    {
        public const int JumpMs = 800, ClimbMs = 1200, RagdollMs = 1500;   // how long each move plays before steering is back
        public const int ClimbMaxMs = 8000;   // steering waits while they climb (a ladder), but no longer
        public const int FallMs = 800;        // falling this long (about 3 m) is a real fall, not a step down
        public const int SettleMs = 1000;     // after a move: then see whether it made their level
        public const int BreakMs = 1000;      // after a longer break (on a horse, in a vehicle, down) a pose only sets the start
        const PoseFlags Moves = PoseFlags.Jumping | PoseFlags.Climbing | PoseFlags.Falling | PoseFlags.Ragdoll;

        PoseFlags _last;
        long _lastMs = -BreakMs - 1, _holdUntil, _climbSince = -1, _fallSince = -1, _checkAt = -1;
        bool _limp;   // this knock-over or fall has had its ragdoll

        // pose: theirs now; near: the stand-in is within reach of where they are (moves play only then).
        public StanceStep Update(PoseFlags pose, bool near, long now)
        {
            if (now - _lastMs > BreakMs)
            {
                _last = pose;
                _limp = (pose & (PoseFlags.Ragdoll | PoseFlags.Falling)) != 0;
                _holdUntil = 0;
                _climbSince = _fallSince = _checkAt = -1;
            }
            var rose = pose & ~_last;
            var fell = _last & ~pose;
            _last = pose;
            _lastMs = now;
            var step = StanceStep.None;

            if ((rose & PoseFlags.Jumping) != 0 && near && now >= _holdUntil) { step |= StanceStep.Jump; _holdUntil = now + JumpMs; }

            if ((pose & PoseFlags.Climbing) == 0) _climbSince = -1;
            else if ((rose & PoseFlags.Climbing) != 0)
            {
                _climbSince = now;
                if (near && now >= _holdUntil) { step |= StanceStep.Climb; _holdUntil = now + ClimbMs; }
            }

            // Limp once per knock-over or fall; out of reach it skips that one (it would collapse somewhere else).
            if ((pose & PoseFlags.Falling) == 0) _fallSince = -1;
            else if (_fallSince < 0) _fallSince = now;
            if ((pose & (PoseFlags.Ragdoll | PoseFlags.Falling)) == 0) _limp = false;
            else if (!_limp && now >= _holdUntil && ((pose & PoseFlags.Ragdoll) != 0 || now - _fallSince >= FallMs))
            {
                _limp = true;
                if (near) { step |= StanceStep.Ragdoll; _holdUntil = now + RagdollMs; }
            }

            if ((fell & Moves) != 0) _checkAt = now + SettleMs;
            if ((pose & Moves) == 0 && _checkAt >= 0 && now >= _checkAt) { step |= StanceStep.CheckLevel; _checkAt = -1; }

            if (now < _holdUntil || (_climbSince >= 0 && now - _climbSince < ClimbMaxMs)) step |= StanceStep.Hold;
            return step;
        }

        // Left on another level: more than 1.5 m above or below them while nearly under or over them
        // (further off, the follow code catches it up).
        public static bool OffLevel(Vec3 standIn, Vec3 them)
        {
            float dx = them.X - standIn.X, dy = them.Y - standIn.Y;
            return Math.Abs(them.Z - standIn.Z) > 1.5f && dx * dx + dy * dy < 36f;
        }
    }
}
