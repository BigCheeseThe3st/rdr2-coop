// Bubbles (0.9.0-beta.9, [Gameplay] Bubbles; George 28 Oct: "each player has the 200m radius and they can overlap/sync
// up"): every game always runs the world round its own player, and games whose players are close enough to see each
// other's people are linked: each sends the other the people, animals and wagons it owns and shows copies of the
// other's. Nothing is deleted or swapped because someone walked in (the old single area owner wiped a guest's fight).
// In an overlap only the higher-priority game (the host, then the lowest id) spawns new ambient life; the other stops
// spawning while it's near that player. The host's story missions keep the old strict follow (a party member near the
// host shows the host's world only), as missions are tuned for it. Pure: links, priority, when to strict-follow.
using System.Collections.Generic;

namespace RDR2Coop.Net
{
    public static class Bubbles
    {
        public const float Radius = 200f;          // each player's bubble
        public const float LinkAt = 320f;          // a bubble plus how far NPCs are sent (EntitySelector.Radius 120)
        public const float UnlinkAt = 390f;        // a gap so a link doesn't flicker at the edge (inside CoopScript.PuppetRange 400: a linked player has a stand-in)
        public const float SpawnBlock = 200f;      // a lower-priority game stops spawning within this of a higher one's player

        /// <summary>Linked to this player now? Away players never link; past LinkAt only a link we already have holds,
        /// up to UnlinkAt. No chains: in a line H - A - B, B isn't linked to H.</summary>
        public static bool Linked(Vec3 me, OtherPlayer other, bool wasLinked)
        {
            if (other.Away) return false;
            float d = Vec3.Distance(me, other.Position);
            return d <= LinkAt || (wasLinked && d <= UnlinkAt);
        }

        /// <summary>Who spawns ambient life in an overlap: the host, then the lowest id (Authority's own order).</summary>
        public static bool Outranks(byte other, byte me, byte host) => Authority.Better(other, me, host);

        /// <summary>Should our game stop spawning ambient life: a linked, higher-priority player within SpawnBlock.</summary>
        public static bool BlockSpawning(byte me, Vec3 myPos, byte host, IDictionary<byte, OtherPlayer> linked)
        {
            foreach (var kv in linked)
                if (!kv.Value.Away && Outranks(kv.Key, me, host) && Vec3.Distance(myPos, kv.Value.Position) <= SpawnBlock) return true;
            return false;
        }

        /// <summary>The host's story missions keep the old strict follow: a guest in the mission party, near the host
        /// while the host is in a story mission, shows the host's world only.</summary>
        public static bool StrictFollow(bool iAmHost, bool partyMember, bool hostInMission, bool nearHost) =>
            !iAmHost && partyMember && hostInMission && nearHost;
    }
}
