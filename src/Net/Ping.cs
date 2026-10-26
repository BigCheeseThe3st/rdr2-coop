// "Look here" pings (0.9.0, [Convenience] Ping): a player marks the spot they look at and the others see it
// on their map and in the world for 12 s (the game side is src/Mod/Pings.cs). The Ping notice, broadcast:
//   at <x> <y> <z>     metres, the game's map coordinates (fields after these are left for newer versions)
// Pure, so the format, its checks and the one-every-2-seconds rule are tested without the game.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RDR2Coop.Net
{
    public static class PingMsg
    {
        public const int ShowMs = 12000, GapMs = 2000;   // how long a ping shows; one per player this often
        public const float Reach = 200f;                 // how far along the view a ping can land
        const float Limit = 20000f;                      // as Protocol's: nothing on the map is further out (or from us)

        public static string Write(Vec3 at) =>
            string.Format(CultureInfo.InvariantCulture, "at {0:0.0} {1:0.0} {2:0.0}", at.X, at.Y, at.Z);

        /// <summary>False for anything but a real map position within 20 km of me.</summary>
        public static bool Read(string text, Vec3 me, out Vec3 at)
        {
            at = default(Vec3);
            var f = (text ?? "").Split(' ');
            if (f.Length < 4 || f[0] != "at" || !Num(f[1], out float x) || !Num(f[2], out float y) || !Num(f[3], out float z)) return false;
            at = new Vec3(x, y, z);
            return Vec3.Distance(at, me) <= Limit;
        }

        /// <summary>The subtitle: "Arthur: look here (120 m)", the name without the game's ~ &lt; &gt; markup.</summary>
        public static string Line(string name, float metres) =>
            new string((name ?? "?").Where(c => c != '~' && c != '<' && c != '>').ToArray()) + ": look here (" +
            Math.Round(metres).ToString("0", CultureInfo.InvariantCulture) + " m)";

        // NaN and infinity fail the range check too.
        static bool Num(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && Math.Abs(v) <= Limit;
    }

    // One ping per player every GapMs (ours too); the rest are dropped.
    public sealed class PingGate
    {
        readonly Dictionary<byte, long> _last = new Dictionary<byte, long>();

        public bool Allow(byte from, long now)
        {
            if (_last.TryGetValue(from, out long t) && now - t < PingMsg.GapMs) return false;
            _last[from] = now;
            return true;
        }

        public void Clear() => _last.Clear();
    }
}
