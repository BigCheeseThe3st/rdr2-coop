// The testable rules behind two of the 0.8.0 visuals (src/Mod/WearEmotes.cs has the game side).
// Wear: how wet our clothes are (the game can't tell us, so water up our body wets us that far
// and it dries off), and when a partner's stand-in is worth dressing again. Emotes: when a
// stand-in plays the scenario its player plays (sitting, leaning...).
using System;

namespace RDR2Coop.Net
{
    public static class WearLevels
    {
        public const int Step = 24;           // about a tenth: smaller changes aren't worth dressing a stand-in again
        // shortcut: a guess at how long the game takes to dry clothes; tune it once it's been seen in game.
        public const float DryMs = 120000f;

        // How wet we are, 0..1, from how deep in water we are (0..1) and how wet we were elapsedMs ago.
        public static float Wet(float was, float submerged, long elapsedMs) =>
            Math.Max(Unit(submerged), Unit(was) - elapsedMs / DryMs);

        public static byte ToByte(float level) => (byte)Math.Round(Unit(level) * 255f);

        // Only getting noticeably wetter is shown: the stand-in dries off by itself like everyone.
        public static bool Wetter(byte wet, ref byte shown)
        {
            if (wet < shown) shown = wet;
            if (wet < shown + Step) return false;
            shown = wet;
            return true;
        }

        // Dirt is matched both ways (a bath makes them clean).
        public static bool DirtChanged(byte dirt, ref byte shown)
        {
            if (Math.Abs(dirt - shown) < Step) return false;
            shown = dirt;
            return true;
        }

        static float Unit(float v) => v > 0f ? Math.Min(v, 1f) : 0f;   // NaN counts as 0
    }

    public static class EmoteRules
    {
        // The scenario a stand-in plays: its player's, while they stand still and it is with them
        // (walking up to them first, standing up when they walk off). 0 = none.
        public static uint Want(uint scenario, float speed, float gap) => speed <= 0.6f && gap < 3f ? scenario : 0;
    }
}
