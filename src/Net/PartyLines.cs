// The words of the on-screen party list (src/Mod/PartyList.cs draws them): one
// line per player with their name, whether they host, their health or what's up
// with them (down, dead, in a cutscene), and how far away they are and which way.
// Pure, so the tests run it without the game.
using System;
using System.Globalization;
using System.Linq;

namespace RDR2Coop.Net
{
    public static class PartyLines
    {
        public const float FarAway = 400f;   // CoopScript.PuppetRange: past this they have no stand-in in your game
        const int MaxName = 20;
        static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        // Health for PlayerState.Health: 1..255 of full, so 0 keeps meaning "unknown" (versions before 0.8.0 send none).
        public static byte HealthByte(int health, int max) =>
            (byte)Math.Max(1, Math.Min(255, (int)Math.Round(255.0 * health / Math.Max(1, max))));

        // The compass point from one place to another (the map's north is +Y, east +X).
        public static string Direction(Vec3 from, Vec3 to) =>
            Points[(int)Math.Round(Math.Atan2(to.X - from.X, to.Y - from.Y) * 4 / Math.PI) & 7];

        public static string Distance(float m) =>
            m < 1000f ? m.ToString("0", CultureInfo.InvariantCulture) + " m"
                      : (m / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km";

        // One player's line. s: their latest state, null before any arrived; you: the line is ours (no distance).
        public static string Line(string name, bool host, bool you, PlayerState? s, Vec3 me)
        {
            // Names come from strangers: no control characters or game markup (~ < >), and not too long.
            string who = new string((name ?? "").Where(c => !char.IsControl(c) && c != '~' && c != '<' && c != '>').Take(MaxName).ToArray());
            who = (who.Length > 0 ? who : "?") + (you ? (host ? " (you, host)" : " (you)") : host ? " (host)" : "");
            if (s == null) return who + "   loading";
            var st = s.Value;
            string state = st.Dead ? "dead" : (st.Flags & StateFlags.Downed) != 0 ? "DOWN"
                         : (st.Flags & StateFlags.InCutscene) != 0 ? "in a cutscene"
                         : st.Health > 0 ? (st.Health * 100 + 127) / 255 + "%" : "";
            string where = "";
            if (!you)
            {
                float d = Vec3.Distance(me, st.Position);
                where = (d > FarAway ? "far away, " : "") + Distance(d) + " " + Direction(me, st.Position);
            }
            return string.Join("   ", new[] { who, state, where }.Where(p => p.Length > 0));
        }
    }
}
