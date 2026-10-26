// Dynamite, fire bottles and explosive ammo (0.8.0): the Explosion notice a player sends when they set
// one off ("kind x y z"; later versions may add fields after these), the kinds that are passed on, and
// how many a player may set off in a second. The game side is src/Mod/Explosions.cs.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RDR2Coop.Net
{
    public static class Blast
    {
        // The game's explosion tags (EXP_TAG_*) a player's weapons make; nothing else (wagons, trains,
        // barrels, lightning) is passed on, or made from a notice.
        public static readonly Dictionary<int, string> Kinds = new Dictionary<int, string>
        {
            { 2, "fire bottle" }, { 3, "volatile fire bottle" }, { 12, "explosive round" }, { 24, "lantern" },
            { 25, "dynamite" }, { 26, "dynamite bundle" }, { 27, "volatile dynamite" }, { 29, "placed dynamite" },
            { 30, "fire arrow" }, { 31, "dynamite arrow" }, { 32, "phosphorus round" }, { 35, "poison bottle" },
        };

        public const float MaxFromThrower = 250f;   // a scoped rifle's explosive round can land this far off
        const float WorldLimit = 20000f;            // as Protocol's: nothing on the map is further out

        public static string Name(int kind) => Kinds.TryGetValue(kind, out var n) ? n : "explosion " + kind;

        public static string Write(int kind, Vec3 at) =>
            string.Format(CultureInfo.InvariantCulture, "{0} {1:0.00} {2:0.00} {3:0.00}", kind, at.X, at.Y, at.Z);

        // False for anything that isn't one of our kinds at a real map position.
        public static bool Read(string text, out int kind, out Vec3 at)
        {
            at = default(Vec3);
            var f = (text ?? "").Split(' ');
            kind = f.Length >= 4 && int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) ? k : -1;
            if (!Kinds.ContainsKey(kind) || !Coord(f[1], out float x) || !Coord(f[2], out float y) || !Coord(f[3], out float z)) return false;
            at = new Vec3(x, y, z);
            return true;
        }

        static bool Coord(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && Math.Abs(v) <= WorldLimit;
    }

    // At most Max explosions in any one second (a burst of explosive rounds); the rest are dropped.
    public sealed class BlastGate
    {
        public const int Max = 5;
        readonly Queue<long> _times = new Queue<long>();

        public bool Allow(long now)
        {
            while (_times.Count > 0 && now - _times.Peek() >= 1000) _times.Dequeue();
            if (_times.Count >= Max) return false;
            _times.Enqueue(now);
            return true;
        }
    }
}
