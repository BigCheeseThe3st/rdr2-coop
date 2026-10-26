// Partners' names over their heads (0.8.0, Visuals.Names): what a tag says and how
// big it is, kept out of the game code so the tests can check it.
using System;
using System.Text.RegularExpressions;

namespace RDR2Coop.Net
{
    public static class NameTag
    {
        public const float Range = 40f;   // further from you than this: no name
        const int MaxLength = 32;         // the protocol's name length (a hostile server could send 255)

        // Names come from other players: the game's ~x~ formatting codes come out (the
        // drawing drops any stray ~ < > as well).
        public static string Clean(string name)
        {
            string s = Regex.Replace(name ?? "", "~[^~]*~", "").Trim();
            return s.Length > MaxLength ? s.Substring(0, MaxLength) : s;
        }

        // Text size: 0.40 up close, shrinking to 0.24 at Range.
        public static float Scale(float distance) => Math.Max(0.24f, Math.Min(0.40f, 0.42f - 0.0045f * distance));
    }
}
