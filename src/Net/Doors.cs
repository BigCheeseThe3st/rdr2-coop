// Doors and broken things in a shared area (0.8.0, [Gameplay] Doors): the door list and the
// text of NoticeKind.Door notices; the game side is src/Mod/DoorSync.cs. Lines of a notice:
//   P                                  the doors below are all the area owner's game manages near you
//   <door hash> <state> <ratio> <open>  a door, by its hash (hex): state 0 unlocked, 1 locked, 2 locked
//                                      but breakable, 3 and 4 held open; ratio -1..1 how far open
//                                      (0 shut); open 1 when it stands open
//   B <model> <x> <y> <z>              an object of that model (hex) broke there
// From the owner's game the door lines are how its doors are; from a partner sharing its area,
// a door their player just opened or kicked open.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RDR2Coop.Net
{
    public struct DoorLine
    {
        public uint Hash;
        public int State;
        public float Ratio;
        public bool Open;
    }

    public struct BreakLine
    {
        public uint Model;
        public Vec3 At;
    }

    public static class Doors
    {
        // A door line is 18 characters at most, so a picture of MaxPicture doors always fits one notice.
        public const int MaxState = 4, MaxLines = 80, MaxPicture = 56;
        static readonly Dictionary<uint, Vec3> At = Load();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static Dictionary<uint, Vec3> Load()
        {
            var d = new Dictionary<uint, Vec3>();
            var r = DoorTable.Raw;
            for (int i = 0; i + 3 < r.Length; i += 4) d[(uint)r[i]] = new Vec3(r[i + 1] / 10f, r[i + 2] / 10f, r[i + 3] / 10f);
            return d;
        }

        // Where a door on the list stands (false for any other hash).
        public static bool TryGet(uint hash, out Vec3 at) => At.TryGetValue(hash, out at);

        // Doors on the list within `range` of any of the points, nearest first.
        public static List<uint> Near(IList<Vec3> points, float range, int cap)
        {
            var found = new List<(uint hash, float d)>();
            foreach (var kv in At)
            {
                float d = float.MaxValue;
                foreach (var p in points) d = Math.Min(d, Vec3.Distance(kv.Value, p));
                if (d <= range) found.Add((kv.Key, d));
            }
            return found.OrderBy(f => f.d).Take(cap).Select(f => f.hash).ToList();
        }

        public static string Line(DoorLine d) =>
            d.Hash.ToString("X", Inv) + " " + d.State.ToString(Inv) + " " + d.Ratio.ToString("0.##", Inv) + (d.Open ? " 1" : " 0");

        public static string Line(BreakLine b) =>
            string.Join(" ", "B", b.Model.ToString("X", Inv), b.At.X.ToString("0.#", Inv), b.At.Y.ToString("0.#", Inv), b.At.Z.ToString("0.#", Inv));

        // head (if any), then as many lines as fit one notice.
        public static string Join(string head, IEnumerable<string> lines)
        {
            var sb = new StringBuilder(head ?? "");
            foreach (var l in lines)
            {
                if (sb.Length + l.Length + 1 > NoticeMsg.MaxBytes) break;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(l);
            }
            return sb.ToString();
        }

        // Anything that isn't a well-formed line about a door on the list, or a break, is dropped; fields a
        // newer version adds at the end of a line are left for it.
        public static void Parse(string text, out bool picture, List<DoorLine> doors, List<BreakLine> breaks)
        {
            picture = false;
            foreach (var line in (text ?? "").Split('\n').Take(MaxLines))
            {
                var f = line.Trim().Split(' ');
                if (f[0] == "P") { picture = true; continue; }
                if (f.Length >= 5 && f[0] == "B")
                {
                    if (uint.TryParse(f[1], NumberStyles.HexNumber, Inv, out uint model) && model != 0
                        && Coord(f[2], out float x) && Coord(f[3], out float y) && Coord(f[4], out float z))
                        breaks.Add(new BreakLine { Model = model, At = new Vec3(x, y, z) });
                    continue;
                }
                if (f.Length < 4 || !uint.TryParse(f[0], NumberStyles.HexNumber, Inv, out uint hash) || !At.ContainsKey(hash)
                    || !int.TryParse(f[1], NumberStyles.Integer, Inv, out int state) || state < 0 || state > MaxState
                    || !float.TryParse(f[2], NumberStyles.Float, Inv, out float ratio) || float.IsNaN(ratio)
                    || (f[3] != "0" && f[3] != "1")) continue;
                doors.Add(new DoorLine { Hash = hash, State = state, Ratio = Math.Max(-1f, Math.Min(1f, ratio)), Open = f[3] == "1" });
            }
        }

        static bool Coord(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, Inv, out v) && !float.IsNaN(v) && Math.Abs(v) < 20000f;
    }
}
