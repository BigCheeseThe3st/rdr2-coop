// Crimes in a shared area (0.8.0, switch [Gameplay] Law): which crime a partner's
// hit on one of our NPCs counts as, the Law notice that tells them about it (their
// own game then charges them), and a cap on how many such notices a game takes in.
// Pure .NET so it can be tested; the game side is src/Mod/Law.cs.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RDR2Coop.Net
{
    public static class LawRules
    {
        // The game's crime hashes (joaat of CRIME_..., as LAW::_REPORT_CRIME takes them).
        public const uint Murder = 0xE28ECE7E, MurderLaw = 0x7797FCE7, AssaultLaw = 0xD7466D7C;

        // What our game could charge us with when it applies a partner's hit: kept
        // off us for a moment after one lands.
        public static readonly uint[] FromHits =
        {
            0x0BADC882 /* CRIME_ASSAULT */, Murder, AssaultLaw, MurderLaw, 0x5098CC5A /* CRIME_UNARMED_ASSAULT */,
            0xC4736181 /* CRIME_ASSAULT_HORSE */, 0xC7261D79 /* CRIME_MURDER_HORSE */,
            0x18DA55EE /* CRIME_ASSAULT_ANIMAL */, 0x48F59A66 /* CRIME_MURDER_ANIMAL */,
            0xCCE1CCBD /* CRIME_ASSAULT_LIVESTOCK */, 0x9569C546 /* CRIME_MURDER_LIVESTOCK */,
        };

        // Lawmen, as Rockstar's own scripts tell them: their default relationship group.
        public const uint RelCop = 0x2A318608, RelGuamaLaw = 0x065A12C3;

        // The crimes we pass on, in players' words; null for anything else.
        public static string Name(uint crime) =>
            crime == Murder ? "killing someone" : crime == MurderLaw ? "killing a lawman" : crime == AssaultLaw ? "attacking a lawman" : null;

        // The crime a partner committed with their hits on one of our NPCs, if anyone
        // saw it (src/Mod/Law.cs looks), or 0. provoked: it wasn't fighting any of us
        // when their first hit landed.
        public static uint For(bool human, bool lawman, bool killed, bool provoked, bool inMission)
        {
            if (inMission || !human) return 0;                 // missions run their own law; animals and horses
            if (lawman) return killed ? MurderLaw : provoked ? AssaultLaw : 0;   // killing a lawman always counts
            return killed && provoked ? Murder : 0;            // killing someone who fought you is self-defence
        }
    }

    // NoticeKind.Law text: "<crime hex> <x> <y> <z>" (where it happened), from the
    // game that runs the area to the player who did it.
    public static class LawNotice
    {
        public static string Format(uint crime, Vec3 at) =>
            string.Format(CultureInfo.InvariantCulture, "{0:X8} {1:0} {2:0} {3:0}", crime, at.X, at.Y, at.Z);

        // Fields after the fourth are left for newer versions.
        public static bool TryParse(string text, out uint crime, out Vec3 at)
        {
            at = default(Vec3);
            var f = (text ?? "").Split(' ');
            if (f.Length < 4 || !uint.TryParse(f[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out crime)
                || LawRules.Name(crime) == null) { crime = 0; return false; }
            var v = new float[3];
            for (int i = 0; i < 3; i++)
                if (!float.TryParse(f[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])
                    || float.IsNaN(v[i]) || Math.Abs(v[i]) > 20000f) { crime = 0; return false; }
            at = new Vec3(v[0], v[1], v[2]);
            return true;
        }
    }

    // Crimes from the network may be wrong or hostile: at most PerMinute are taken in.
    public sealed class LawGate
    {
        public const int PerMinute = 8;
        readonly Queue<long> _taken = new Queue<long>();

        public bool Allow(long now)
        {
            while (_taken.Count > 0 && now - _taken.Peek() >= 60000) _taken.Dequeue();
            if (_taken.Count >= PerMinute) return false;
            _taken.Enqueue(now);
            return true;
        }
    }
}
