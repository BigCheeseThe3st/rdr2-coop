// Panel colours (0.9.0): src/Net hands out "tones" (it has no System.Drawing); every panel maps them here.
// 0 white, 1 gold (mine, selected), 2 grey, 3 red (title, warning), 4 green (money won, the winner).
using System;
using System.Drawing;

namespace RDR2Coop.Mod
{
    static class Tone
    {
        static readonly Color[] Colors = { Color.White, Color.FromArgb(255, 235, 190, 70), Color.FromArgb(255, 170, 170, 170), Color.FromArgb(255, 200, 40, 40), Color.FromArgb(255, 90, 200, 90) };

        public static Color Of(int tone) => Colors[Math.Max(0, Math.Min(Colors.Length - 1, tone))];
    }
}
