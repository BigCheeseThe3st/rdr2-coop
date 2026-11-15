// One of each person where bubbles overlap (0.9.0-beta.9; with the host's world, src/Net/WorldShare.cs, both games
// run the same camp, so every gang member exists in both). The higher-priority game's people win: the other game hides
// its own one (invisible, no collision, still; never deleted: its scripts expect it) while it shows the higher game's
// copy of the same person, and shows it again GraceMs after that copy goes. A hidden person is never sent on either.
// Same person: the game's companion registry (Global_1360165, 27 entries, the ped handle first: the gang, by index),
// which each game sends as "keys" notices for the people it sends; else, for our other script-made people
// (shopkeepers, strangers at their spot), a copy of the same model within SameSpot metres. Pure.
//
// The EntityKeys notice, every 2 s to linked players:  keys <companion>=<entity id> ...   (ids as in the sender's world)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RDR2Coop.Net
{
    public static class EntityKeys
    {
        public const int Companions = 27, SendEveryMs = 2000;

        public static string Write(IEnumerable<KeyValuePair<int, ushort>> keys)
        {
            var sb = new StringBuilder("keys");
            foreach (var k in keys) if (k.Key >= 0 && k.Key < Companions && k.Value != 0) sb.Append(' ').Append(k.Key).Append('=').Append(k.Value);
            return sb.ToString();
        }

        /// <summary>companion index -> entity id; bad parts dropped; null for anything that isn't a keys notice.</summary>
        public static Dictionary<int, ushort> Read(string text)
        {
            var f = (text ?? "").Split(' ');
            if (f.Length < 1 || f[0] != "keys") return null;
            var d = new Dictionary<int, ushort>();
            foreach (var p in f.Skip(1).Take(Companions))
            {
                int eq = p.IndexOf('=');
                if (eq > 0 && int.TryParse(p.Substring(0, eq), NumberStyles.None, CultureInfo.InvariantCulture, out int i) && i < Companions
                    && ushort.TryParse(p.Substring(eq + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id != 0) d[i] = id;
            }
            return d;
        }
    }

    public struct LocalPed { public int Handle; public uint Model; public Vec3 Position; }
    public struct CopyPed { public uint Model; public Vec3 Position; }

    public sealed class Doubles
    {
        public const long GraceMs = 2000;
        public const float SameSpot = 3f;
        readonly Dictionary<int, long> _pairedAt = new Dictionary<int, long>();

        /// <summary>Which of our own people to keep hidden now. ownCompanions: index -> our handle; copiedCompanions:
        /// the indices a higher-priority game sends and we show a copy of; ownScript: our other script-made people
        /// nearby; copies: the higher-priority games' copies here.</summary>
        public HashSet<int> Decide(IDictionary<int, int> ownCompanions, ICollection<int> copiedCompanions, IList<LocalPed> ownScript, IList<CopyPed> copies, long now)
        {
            foreach (var kv in ownCompanions)
                if (kv.Value != 0 && copiedCompanions.Contains(kv.Key)) _pairedAt[kv.Value] = now;
            var companionHandles = new HashSet<int>(ownCompanions.Values);
            foreach (var p in ownScript)
            {
                if (companionHandles.Contains(p.Handle)) continue;   // the gang go by the registry only
                foreach (var c in copies)
                    if (c.Model == p.Model && Vec3.Distance(c.Position, p.Position) <= SameSpot) { _pairedAt[p.Handle] = now; break; }
            }
            foreach (var h in _pairedAt.Where(kv => now - kv.Value > GraceMs).Select(kv => kv.Key).ToList()) _pairedAt.Remove(h);
            return new HashSet<int>(_pairedAt.Keys);
        }

        public void Clear() => _pairedAt.Clear();
    }
}
