// What a partner carries, as their game sends it (0.8.0, [Visuals] Carrying): PlayerState.Carrying is
// the model (for a pelt stowed on a horse, the pelt's item hash) and CarryKind says where it is.
// These rules decide where our copy of it goes; pure, so they're tested without the game.
using System.Collections.Generic;

namespace RDR2Coop.Net
{
    public static class Carry
    {
        public const byte Nothing = 0, Shoulder = 1, Horse = 2;   // CarryKind values

        // On the shoulder only on foot (not riding, behind a rider, in a vehicle, down or dead). A horse's load stays on
        // their horse, ridden or waiting for them ([Visuals] Horses); with no horse of theirs here it shows
        // nowhere. Players on 0.7.x send 0 and a bad packet anything at all: both mean nothing.
        public static byte Where(PlayerState s)
        {
            if (s.Carrying == 0) return Nothing;
            if (s.CarryKind == Horse) return Horse;
            bool onFoot = !s.Mounted && s.RidingWith == 0 && s.VehicleId == 0 && (s.Flags & (StateFlags.Downed | StateFlags.Dead)) == 0;
            return s.CarryKind == Shoulder && onFoot ? Shoulder : Nothing;
        }

        // Which of the things a horse carries is the biggest (by size); -1 when there are none.
        public static int Biggest(IList<float> sizes)
        {
            int best = -1;
            float most = 0f;
            for (int i = 0; i < sizes.Count; i++)
            {
                float v = float.IsNaN(sizes[i]) ? -1f : sizes[i];   // a bad size still counts, but loses
                if (best < 0 || v > most) { best = i; most = v; }
            }
            return best;
        }
    }
}
