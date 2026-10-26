// Smooths the partner's movement: snapshots arrive ~20 times a second with
// jitter, so we draw them a little in the past and blend between the two
// snapshots either side of that moment.
using System;
using System.Collections.Generic;

namespace RDR2Coop.Net
{
    public sealed class Interpolator
    {
        struct Entry
        {
            public long ArrivedMs;
            public PlayerState State;
        }

        readonly List<Entry> _buffer = new List<Entry>();
        readonly int _delayMs;
        readonly int _maxExtrapolateMs;
        uint _lastSeq;
        bool _any;

        public Interpolator(int delayMs = 70, int maxExtrapolateMs = 250)
        {
            _delayMs = delayMs;
            _maxExtrapolateMs = maxExtrapolateMs;
        }

        public bool HasData => _any;

        public void Clear()
        {
            _buffer.Clear();
            _any = false;
            _lastSeq = 0;
        }

        // Returns false for duplicates and stale (out-of-order) snapshots.
        public bool Push(PlayerState s, long arrivedMs)
        {
            if (_any && s.Seq <= _lastSeq) return false;
            _lastSeq = s.Seq;
            _any = true;
            _buffer.Add(new Entry { ArrivedMs = arrivedMs, State = s });
            if (_buffer.Count > 32) _buffer.RemoveRange(0, _buffer.Count - 32);
            return true;
        }

        public PlayerState Sample(long nowMs)
        {
            if (_buffer.Count == 0) throw new InvalidOperationException("no snapshots yet");
            long t = nowMs - _delayMs;

            if (t <= _buffer[0].ArrivedMs) return _buffer[0].State;

            for (int i = _buffer.Count - 1; i > 0; i--)
            {
                var a = _buffer[i - 1];
                var b = _buffer[i];
                if (t >= a.ArrivedMs && t <= b.ArrivedMs)
                {
                    float span = Math.Max(1, b.ArrivedMs - a.ArrivedMs);
                    return Blend(a.State, b.State, (t - a.ArrivedMs) / span);
                }
            }

            // Newer than anything we have: carry on along the last velocity,
            // but not for long, so a lost connection doesn't send them flying.
            var last = _buffer[_buffer.Count - 1];
            float ahead = Math.Min(t - last.ArrivedMs, _maxExtrapolateMs) / 1000f;
            var s = last.State;
            s.Position = s.Position + s.Velocity * ahead;
            if (s.Mounted) s.MountPosition = s.MountPosition + s.Velocity * ahead;
            return s;
        }

        static PlayerState Blend(PlayerState a, PlayerState b, float t)
        {
            // Mounting, dismounting or a model change can't be blended; snap
            // to whichever side we're closer to.
            if (a.Flags != b.Flags || a.PedModel != b.PedModel || a.MountModel != b.MountModel)
                return t < 0.5f ? a : b;

            var s = b;
            s.Position = Vec3.Lerp(a.Position, b.Position, t);
            s.Velocity = Vec3.Lerp(a.Velocity, b.Velocity, t);
            s.Heading = LerpAngle(a.Heading, b.Heading, t);
            s.MountPosition = Vec3.Lerp(a.MountPosition, b.MountPosition, t);
            s.MountHeading = LerpAngle(a.MountHeading, b.MountHeading, t);
            return s;
        }

        // Headings are degrees in [0, 360); go the short way round.
        public static float LerpAngle(float a, float b, float t)
        {
            float d = ((b - a) % 360f + 540f) % 360f - 180f;
            float r = a + d * t;
            return (r % 360f + 360f) % 360f;
        }
    }
}
