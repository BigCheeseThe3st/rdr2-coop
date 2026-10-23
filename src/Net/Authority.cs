// Which game runs the area around a player. Every game runs its own player's
// surroundings unless another player is close: then one game runs the shared
// area (the host's if the host is there, so missions and enRage work; else the
// lowest player id) and the others show copies of its NPCs.
using System.Collections.Generic;

namespace RDR2Coop.Net
{
    public struct OtherPlayer
    {
        public Vec3 Position;
        public byte Following;   // whose area they show; 0 = their own
    }

    public static class Authority
    {
        public const float JoinRadius = 150f;
        public const float LeaveRadius = 250f;   // gap so the role doesn't flicker at the edge

        // Returns the id of the game that should run my area (my own id if mine).
        public static byte Pick(byte me, Vec3 myPos, byte host, IDictionary<byte, OtherPlayer> others, byte current)
        {
            byte best = me;
            foreach (var kv in others)
            {
                float radius = kv.Key == current ? LeaveRadius : JoinRadius;
                if (Vec3.Distance(kv.Value.Position, myPos) > radius) continue;
                if (Better(kv.Key, best, host)) best = kv.Key;
            }

            // Follow a chain to the game that really runs the area (A near B,
            // B near C: everyone ends up on the same owner), but never back to us.
            var seen = new HashSet<byte> { me };
            while (best != me && others.TryGetValue(best, out var o) && o.Following != 0 && o.Following != best && seen.Add(best))
            {
                if (o.Following == me) return me;
                best = o.Following;
            }
            return best;
        }

        static bool Better(byte a, byte b, byte host) => a == host || (b != host && a < b);
    }
}
