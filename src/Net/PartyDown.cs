// Party down (the author, 26 Oct: a death in co-op should wait "until all party members are dead"): a downed
// player's game lets its own death happen only once every other player in the session is down or dead too.
using System.Collections.Generic;
using System.Linq;

namespace RDR2Coop.Net
{
    public static class PartyDown
    {
        /// <summary>Whether every other player is down (true for each who is); false with nobody else in the session.</summary>
        public static bool AllDown(IEnumerable<bool> othersDown)
        {
            var l = othersDown.ToList();
            return l.Count > 0 && l.All(d => d);
        }
    }
}
