// Partners aiming (0.8.0), the parts that need no game: the point we send when we
// aim at nothing in particular, and when a partner's stand-in should turn to a new point.
using System;

namespace RDR2Coop.Net
{
    public static class Aim
    {
        public const float Reach = 50f;   // free aim at nothing: the point this far along the camera's view

        // The point Reach (or reach) metres along a camera's view. rot: the game's camera rotation
        // in degrees (X pitch, Z heading: 0 looks north, 90 west).
        public static Vec3 Along(Vec3 from, Vec3 rot, float reach = Reach)
        {
            double pitch = rot.X * Math.PI / 180, heading = rot.Z * Math.PI / 180;
            var dir = new Vec3((float)(-Math.Sin(heading) * Math.Cos(pitch)), (float)(Math.Cos(heading) * Math.Cos(pitch)), (float)Math.Sin(pitch));
            return from + dir * reach;
        }

        // Whether the point they aim at moved enough (about 3 degrees as seen from the
        // stand-in at `from`) to aim it again: every new aim order restarts its pose.
        public static bool Moved(Vec3 from, Vec3 aimed, Vec3 now) =>
            Vec3.Distance(aimed, now) > Math.Max(0.2f, 0.05f * Vec3.Distance(from, now));
    }
}
