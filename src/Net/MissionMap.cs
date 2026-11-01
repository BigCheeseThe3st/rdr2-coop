// What each story mission, stranger mission and random encounter keeps in its script statics (0.9.0-beta.8.4): which slot holds which person, horse,
// wagon or prop, which side they're on, and the cutscene part they play. Made offline from the decompiled 1491.50
// scripts and checked against recordings (rdr2-research/missionmap; the table is MissionMapData.cs). Pure: the game
// side (Mod/MissionRoles.cs) reads the slots and asks Slots() which ones to look at.
//
// A table line: "<script>|<entry>;<entry>..."; an entry: "<slot>[+<stride>x<count>]:<roles>[:<name>/<name>]"
// (a range is <count> slots from <slot>, <stride> apart: an array of people). Roles: a ally, e enemy, m changes
// side during the mission, n neutral, k the mission checks who killed them.
using System;
using System.Collections.Generic;

namespace RDR2Coop.Net
{
    [Flags]
    public enum MissionRole : byte { None = 0, Ally = 1, Enemy = 2, Neutral = 4, KillerChecked = 8 }

    public sealed class MissionSlot
    {
        public int Start, Stride, Count = 1;
        public MissionRole Role;
        public string[] Names = new string[0];   // the cutscene parts (anim scene entity names) they play
    }

    public static class MissionMap
    {
        static Dictionary<string, List<MissionSlot>> _all;

        /// <summary>The mission's slots, or null for a script the table doesn't know.</summary>
        public static List<MissionSlot> Slots(string script)
        {
            if (_all == null) _all = Parse(MissionMapData.Lines);
            return script != null && _all.TryGetValue(script, out var l) ? l : null;
        }

        /// <summary>Every script the table knows.</summary>
        public static IEnumerable<string> Scripts()
        {
            if (_all == null) _all = Parse(MissionMapData.Lines);
            return _all.Keys;
        }

        public static Dictionary<string, List<MissionSlot>> Parse(IEnumerable<string> lines)
        {
            var all = new Dictionary<string, List<MissionSlot>>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                int bar = line.IndexOf('|');
                if (bar <= 0) continue;
                var slots = new List<MissionSlot>();
                foreach (var e in line.Substring(bar + 1).Split(';'))
                {
                    var p = e.Split(':');
                    var s = new MissionSlot();
                    var range = p[0].Split('+');
                    if (!int.TryParse(range[0], out s.Start) || s.Start < 0) continue;
                    if (range.Length == 2)
                    {
                        var sc = range[1].Split('x');
                        if (sc.Length != 2 || !int.TryParse(sc[0], out s.Stride) || !int.TryParse(sc[1], out s.Count) || s.Stride < 1 || s.Count < 1 || s.Count > 256) continue;
                    }
                    foreach (char c in p.Length > 1 ? p[1] : "")
                        s.Role |= c == 'a' ? MissionRole.Ally : c == 'e' ? MissionRole.Enemy : c == 'm' ? MissionRole.Ally | MissionRole.Enemy
                                : c == 'n' ? MissionRole.Neutral : c == 'k' ? MissionRole.KillerChecked : MissionRole.None;
                    if (p.Length > 2) s.Names = p[2].Split('/');
                    slots.Add(s);
                }
                all[line.Substring(0, bar)] = slots;
            }
            return all;
        }

        /// <summary>Every slot index an entry covers.</summary>
        public static IEnumerable<int> Indices(MissionSlot s)
        {
            for (int i = 0; i < s.Count; i++) yield return s.Start + i * s.Stride;
        }

        /// <summary>The side a mission gives someone: Ally or Enemy when the map is sure, None when it isn't
        /// (not in the map, neutral, or a slot that changes side: the game's own relationship decides then).</summary>
        public static MissionRole Side(MissionRole r)
        {
            var side = r & (MissionRole.Ally | MissionRole.Enemy);
            return side == MissionRole.Ally || side == MissionRole.Enemy ? side : MissionRole.None;
        }
    }
}
