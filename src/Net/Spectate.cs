// Spectate (0.9.0, [Convenience] Spectate): which partner the camera goes to next. The game side is src/Mod/Spectate.cs.
using System;
using System.Collections.Generic;
using System.Linq;

namespace RDR2Coop.Net
{
    public static class SpectateOrder
    {
        // After current (0: nobody yet), the next partner id up that watchable accepts, round to the lowest;
        // current itself only when nobody else is watchable; 0 when nobody is.
        public static byte Next(IEnumerable<byte> partners, byte current, Func<byte, bool> watchable)
        {
            var ids = partners.Where(id => id != 0).Distinct().Where(watchable).OrderBy(id => id).ToList();
            if (ids.Count == 0) return 0;
            foreach (var id in ids) if (id > current) return id;
            return ids[0];
        }
    }
}
