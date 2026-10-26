// Which train configuration a real train most likely came from, by its cars'
// models: the game can list a configuration's cars, but can't say which
// configuration a train was built from. Pure, so it's tested; TrainConfigs
// gives it the game's lists (0.8.0, [Gameplay] Trains).
using System;
using System.Collections.Generic;
using System.Linq;

namespace RDR2Coop.Net
{
    public static class TrainMatch
    {
        public const int Exact = 1000;

        // trains: the train's car models in order (tried with and without the
        // engine: which way the game counts isn't sure). An exact match wins;
        // else the configuration with the same first car and the most cars in
        // the same place (cars uncoupled, say), then the nearest length. 0 when
        // none starts with the same car (a loose carriage): no copy beats a
        // wrong train. alike: the cars in place, or Exact.
        public static uint Closest(IDictionary<uint, List<uint>> configs, IEnumerable<IList<uint>> trains, out int alike)
        {
            uint best = 0;
            int bestGap = int.MaxValue;
            alike = 0;
            foreach (var t in trains)
                foreach (var kv in configs)
                {
                    var c = kv.Value;
                    if (t.Count == 0 || c.Count == 0 || t[0] != c[0]) continue;
                    if (c.SequenceEqual(t)) { alike = Exact; return kv.Key; }
                    int same = 0;
                    for (int i = 0; i < Math.Min(c.Count, t.Count); i++) if (c[i] == t[i]) same++;
                    int gap = Math.Abs(c.Count - t.Count);
                    if (same > alike || (same == alike && gap < bestGap)) { best = kv.Key; alike = same; bestGap = gap; }
                }
            return best;
        }
    }
}
