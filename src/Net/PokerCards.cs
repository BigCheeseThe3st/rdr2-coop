// The game's own cards for a real poker table (0.9.0-beta.8.4, poker at the story's tables): a card (Poker's int: rank
// c >> 2 from 2 to ace, suit c & 3 in "shdc") as the game's card model, the face the game's poker draws on screen, and
// the deck a town's tables use. Names from the 1491.50 poker scripts (p_crd_<rank>_<suit>01x_<deck>) and the card_set
// texture dictionaries. Pure, so it can be tested.
using System;

namespace RDR2Coop.Net
{
    public static class PokerCards
    {
        static readonly string[] Rank = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "j", "q", "k", "a" };
        static readonly string[] SuitLetter = { "s", "h", "d", "c" }, SuitName = { "spades", "hearts", "diamonds", "clubs" };
        public const string FaceDict = "card_set_1";

        static bool Valid(int c) => c >= 0 && c < 52;

        /// <summary>The card lying face up on the table: "p_crd_10_c01x_val".</summary>
        public static string Model(int c, string deck) => Valid(c) ? $"p_crd_{Rank[c >> 2]}_{SuitLetter[c & 3]}01x_{deck}" : null;

        /// <summary>A card lying face down (its back): "p_crd_01x_val".</summary>
        public static string Back(string deck) => $"p_crd_01x_{deck}";

        /// <summary>The card's face in FaceDict, for our own two on screen: "clubs_10".</summary>
        public static string Face(int c) => Valid(c) ? $"{SuitName[c & 3]}_{Rank[c >> 2]}" : null;

        // The towns whose saloons have poker (their tables' deck); elsewhere, Valentine's.
        static readonly (float x, float y, string deck)[] Towns =
        {
            (-306f, 792f, "val"), (-803f, -1319f, "bla"), (1347f, -1301f, "rho"), (2955f, 523f, "van"), (2644f, -1227f, "std_labastille"),
        };

        /// <summary>The deck a table there plays with: the nearest poker town's within 400 m, else Valentine's.</summary>
        public static string DeckFor(Vec3 at)
        {
            string best = "val";
            float bestD = 400f * 400f;
            foreach (var t in Towns)
            {
                float dx = at.X - t.x, dy = at.Y - t.y, d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = t.deck; }
            }
            return best;
        }
    }
}
