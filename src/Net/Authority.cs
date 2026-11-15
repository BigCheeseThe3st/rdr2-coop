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
        public bool Away;        // silent for AwayMs (pause menu, alt-tab, loading): runs nobody's area until it's back
    }

    public static class Authority
    {
        public const float JoinRadius = 200f;
        public const float LeaveRadius = 300f;   // gap so the role doesn't flicker at the edge
        public const int AwayMs = 20000;          // a shorter pause changes nothing anywhere
        public const int CutsceneAwayMs = 180000; // a game last seen in a cutscene or a fade keeps its area this long

        // Returns the id of the game that should run my area (my own id if mine).
        public static byte Pick(byte me, Vec3 myPos, byte host, IDictionary<byte, OtherPlayer> others, byte current)
        {
            byte best = me;
            foreach (var kv in others)
            {
                if (kv.Value.Away) continue;
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
            // A chain ending on a paused game: run my own area (everyone else reshuffles the same way).
            return others.TryGetValue(best, out var end) && end.Away ? me : best;
        }

        static bool Better(byte a, byte b, byte host) => a == host || (b != host && a < b);

        /// <summary>Why Pick left us running our own area although the host is on the server, for the log (bug #4: guests'
        /// games switching to their own world for seconds at a time, spawning their own people).</summary>
        public static string WhyOwn(Vec3 myPos, byte host, IDictionary<byte, OtherPlayer> others, byte current)
        {
            if (!others.TryGetValue(host, out var h)) return "no position from the host yet";
            if (h.Away) return "the host has been silent (pause menu, alt-tab or loading)";
            float d = Vec3.Distance(h.Position, myPos), radius = current == host ? LeaveRadius : JoinRadius;
            if (d > radius) return $"the host is {d:F0} m away (over {radius:F0} m)";
            if (h.Following != 0 && h.Following != host) return $"the host shows player {h.Following}'s area, whose game isn't usable";
            return "unknown";
        }
    }
}
