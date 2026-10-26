// Shared map waypoints (0.8.0): a player's map waypoint travels to the others as a
// NoticeKind.Waypoint notice, "x y z" in metres, or empty text when they clear it.
// Pure, so the format, its checks and the follow rule are tested without the game.
using System;
using System.Globalization;

namespace RDR2Coop.Net
{
    public static class WaypointNotice
    {
        const float Limit = 20000f;   // as Protocol's check on positions: further out isn't a map coordinate

        public static string Encode(Vec3 at) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1:0.#} {2:0.#}", Fix(at.X), Fix(at.Y), Fix(at.Z));

        // False: not a waypoint we can use (a bad number, or garbage): ignore it. True: at is
        // their waypoint, or null when they cleared it. Fields after the third are left for
        // newer versions.
        public static bool TryParse(string text, out Vec3? at)
        {
            at = null;
            var f = (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length == 0) return true;
            if (f.Length < 3 || !Num(f[0], out float x) || !Num(f[1], out float y) || !Num(f[2], out float z)) return false;
            at = new Vec3(x, y, z);
            return true;
        }

        // The host's waypoint becomes our GPS route only while we have none of our own (a
        // player's own waypoint always wins), we aren't the host, and no story mission of ours
        // could be using the GPS.
        public static Vec3? Follow(bool iAmHost, Vec3? hosts, bool ownWaypoint, bool inMission) =>
            iAmHost || ownWaypoint || inMission ? null : hosts;

        // NaN and infinity fail the range check too.
        static bool Num(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && Math.Abs(v) <= Limit;

        static float Fix(float v) => float.IsNaN(v) ? 0f : Math.Max(-Limit, Math.Min(Limit, v));
    }
}
