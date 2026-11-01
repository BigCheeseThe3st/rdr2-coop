// Story cutscenes on guests' screens (0.9.0-beta.8.4, [Gameplay] Cutscenes): while one plays in the host's game, the
// host sends what it is and who plays in it, and each guest in the mission party plays the same anim scene in their
// own game with their copies in it (game side: src/Mod/Cutscenes.cs). The Cutscene notice, broadcast by the host:
//   scene <label> <playback list> <time s> <x> <y> <z> <rx> <ry> <rz>    the anim scene "cutscene@<label>", where
//                                                                        the host's runs (0 0 0: the scene's own
//                                                                        place) and how far it has got
//   part <name> <entity id>                                              one per line: the host's NPC, horse, wagon
//   part <name> @                                                        or prop playing <name> (ids as in the
//                                                                        host's world packets); @: the host himself
//   next <list> ...                                                      the playback lists (phases) the mission has
//                                                                        queued next, up to 4: load them early
//   end <label>                                                          it's over
// Pure: format, checks and the playback-rate rule are tested without the game.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RDR2Coop.Net
{
    public sealed class CutsceneMsg
    {
        public const int MaxParts = 24, SendEveryMs = 1000, StaleMs = 4000;
        public const ushort Host = 0;      // part id meaning the host's own character

        public string Label, List = "NormalStart";
        public bool End;
        public float Time;
        public Vec3 Origin, Rotation;
        public readonly List<KeyValuePair<string, ushort>> Parts = new List<KeyValuePair<string, ushort>>();
        public readonly List<string> Next = new List<string>();   // phases the mission has queued after List

        public string Write()
        {
            if (End) return "end " + Label;
            var sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture, "scene {0} {1} {2:0.00} {3:0.00} {4:0.00} {5:0.00} {6:0.0} {7:0.0} {8:0.0}",
                                    Label, List, Time, Origin.X, Origin.Y, Origin.Z, Rotation.X, Rotation.Y, Rotation.Z));
            if (Next.Count > 0) sb.Append("\nnext " + string.Join(" ", Next.Where(n => Name(n, 32)).Take(4)));
            foreach (var p in Parts.Take(MaxParts))
            {
                string line = "\npart " + p.Key + " " + (p.Value == Host ? "@" : p.Value.ToString(CultureInfo.InvariantCulture));
                if (sb.Length + line.Length > NoticeMsg.MaxBytes) break;
                sb.Append(line);
            }
            return sb.ToString();
        }

        /// <summary>Null for anything that isn't a well-formed notice.</summary>
        public static CutsceneMsg Read(string text)
        {
            var lines = (text ?? "").Split('\n');
            var f = lines[0].Split(' ');
            if (f.Length == 2 && f[0] == "end" && Name(f[1], 32)) return new CutsceneMsg { Label = f[1], End = true };
            if (f.Length < 10 || f[0] != "scene" || !Name(f[1], 32) || !Name(f[2], 32)) return null;
            var n = new float[7];
            for (int i = 0; i < 7; i++)
                if (!float.TryParse(f[3 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]) || float.IsNaN(n[i]) || Math.Abs(n[i]) > 20000f) return null;
            if (n[0] < 0 || n[0] > 3600) return null;
            var m = new CutsceneMsg { Label = f[1], List = f[2], Time = n[0], Origin = new Vec3(n[1], n[2], n[3]), Rotation = new Vec3(n[4], n[5], n[6]) };
            foreach (var l in lines.Skip(1))
            {
                var p = l.Split(' ');
                if (p[0] == "next") { foreach (var q in p.Skip(1).Take(4)) if (Name(q, 32)) m.Next.Add(q); continue; }
                if (p.Length != 3 || p[0] != "part" || !Name(p[1], 48) || m.Parts.Count >= MaxParts) continue;
                if (p[2] == "@") m.Parts.Add(new KeyValuePair<string, ushort>(p[1], Host));
                else if (ushort.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id != 0) m.Parts.Add(new KeyValuePair<string, ushort>(p[1], id));
            }
            return m;
        }

        static bool Name(string s, int max) => s.Length > 0 && s.Length <= max && s.All(c => c == '_' || c == '-' || c == '^' || c < 128 && char.IsLetterOrDigit(c));   // Horse_01^1

        /// <summary>How fast our copy plays to catch the host's up: a second behind runs 1.5x, ahead runs slower;
        /// within 0.15 s it's 1. hostTime is the host's time now (its last report plus the time since).</summary>
        public static float Rate(float hostTime, float ourTime)
        {
            float d = hostTime - ourTime;
            return Math.Abs(d) < 0.15f ? 1f : Math.Max(0.25f, Math.Min(3f, 1f + d * 0.5f));
        }
    }
}
