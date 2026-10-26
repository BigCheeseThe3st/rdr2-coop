// Where the host's story missions start: the yellow mission markers on its
// map. The story scripts keep a table of every story mission with its start
// point and the handle of its marker (0 while the mission has none), so
// reading two numbers per mission tells which ones the host's map shows.
// Only the numbers are read; the handles never go to the game (checking
// handles the game didn't give out is what crashed the old marker scan).
// The markers inside a running mission can't be found this way: each mission
// keeps them in its own script's variables, which nothing outside can read.
using System;
using System.Collections.Generic;
using System.Linq;

namespace RDR2Coop.Net
{
    public static class MissionMarkers
    {
        // Global_1835011 in the public decompiled 1491.50 scripts (MissionProbe checks the build lines up):
        // its first slot is the number of missions, then 74 slots each, .f_18 the start point, .f_27 the marker.
        public const int Table = 1835011, EntrySize = 74, StartField = 18, MarkerField = 27;
        const int MaxMissions = 128;   // the scripts know about 80; anything bigger is a misread
        const float WorldLimit = 20000f;

        // The missions whose marker is up, the nearest few if there are more than a packet holds,
        // in table order (so the list only changes when a marker comes or goes).
        public static List<Objective> Read(Func<int, int> globalInt, Func<int, float> globalFloat, Vec3 near, float radius)
        {
            int count = Math.Max(0, Math.Min(globalInt(Table), MaxMissions));
            var found = new List<(int slot, Vec3 at)>();
            for (int i = 0; i < count; i++)
            {
                int b = Table + 1 + EntrySize * i;
                if (globalInt(b + MarkerField) == 0) continue;
                var at = new Vec3(globalFloat(b + StartField), globalFloat(b + StartField + 1), globalFloat(b + StartField + 2));
                if (Usable(at)) found.Add((i, at));
            }
            return found.OrderBy(f => Vec3.Distance(f.at, near)).Take(Objective.Max).OrderBy(f => f.slot)
                        .Select(f => new Objective { Position = f.at, Radius = radius }).ToList();
        }

        // To the guests in a NoticeKind.MissionStep notice: one "x y z" line per marker, empty for none.
        public static string Write(List<Objective> list) =>
            string.Join("\n", list.Take(Objective.Max).Select(o => WaypointNotice.Encode(o.Position)));

        // A host's notice back into markers: bad lines are dropped (fields a newer version adds are skipped), 8 at most.
        public static List<Objective> Parse(string text, float radius)
        {
            var list = new List<Objective>();
            foreach (var line in (text ?? "").Split('\n'))
                if (list.Count < Objective.Max && WaypointNotice.TryParse(line, out var at) && at.HasValue && Usable(at.Value))
                    list.Add(new Objective { Position = at.Value, Radius = radius });
            return list;
        }

        public static bool Same(List<Objective> a, List<Objective> b) =>
            a.Count == b.Count && a.Zip(b, (x, y) => x.Position.X == y.Position.X && x.Position.Y == y.Position.Y
                                                     && x.Position.Z == y.Position.Z && x.Radius == y.Radius).All(same => same);

        // A real map point: Read sends nothing else and Parse takes nothing else.
        static bool Usable(Vec3 p) => Ok(p.X) && Ok(p.Y) && Ok(p.Z) && (p.X != 0f || p.Y != 0f);

        static bool Ok(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && Math.Abs(v) <= WorldLimit;
    }
}
