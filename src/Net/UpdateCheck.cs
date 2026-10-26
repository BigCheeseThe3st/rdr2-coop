// The update check behind the in-game notice and one-press update (the author, 24 Oct:
// "notice plus one-press update"): what the newest release is, whether it is newer
// than this one, and whether a downloaded mod really is one of the releases. Each
// release's RDR2Coop.dll is signed (RSA, SHA-256, PKCS#1 v1.5) with a key that never
// leaves the release machine (tools/make-release.sh signs it), and Verify checks it
// against the public half below, so not even a copy put on the GitHub page by
// someone else would be installed. Plain managed code (BigInteger, SHA256Managed):
// it doesn't lean on Windows' own crypto, which Proton only partly has.
using System;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;

namespace RDR2Coop.Net
{
    public static class UpdateCheck
    {
        // The release key's public half: its modulus (the exponent is 65537).
        public const string ReleaseKey = "uI2a0MjPbfTBMQ9EY8mP9YaalenpF0+jHeXY2wDu+QPnfUlixy5FXqE7/95/vYWeS5NjQQkY49q808ltv+gRuAP96NlqRvIYR0uNSzOXZLeU1nYkmwdX6TsUPMIJxHkP27znSqvrNd06TvjP9tAChcE4UfJGwbXYVW6cuphlOfTRUvFulfXDM7l9HV3nVJmVqVsJ+bX4tAYLvs4a8VbysQRf6YbYhjq0C2qvRudSnsSxjJZ2qT+7H2dIACwYJjX/GgOLgRHzXD/OkcMLqu3qIu9bhwdKPWU6owOXIVdtRR76DpXc/62YMG67Vqv7Z4uHM8dZDe7aPC+1k1QXXM1hW6CJ7ya1oG8aE7EFx3CtJYUjWByzGchnjB0mCzGmGjSPA2n0M66bKi5LTwg2VWisnzNvNeTVgAMsnYslBUuovbANBy5hmHAHtgtQKlQr4lLsVl7H7WYmlYSdduXcbzP/qDL7n5wu/Vn7FSIBDLsT5AEKv6hlLbYI5v1Rhdqpnznb";

        // latest.txt, published with each release: the version, the protocol, a line of notes, and (from
        // 0.9.0) "api <version>": the RDR2Coop.Api the mod was built against; see Api below.
        public static bool Parse(string text, out string version, out int protocol, out string notes)
        {
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            version = lines[0].Trim();
            protocol = lines.Length > 1 && int.TryParse(lines[1].Trim(), out int p) ? p : 0;
            notes = string.Join(" ", lines.Skip(2).Select(l => l.Trim()).Where(l => l.Length > 0));
            // The version goes into a download address: letters, figures, dots and dashes only.
            return version.Length > 0 && version.Length <= 32 && version.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-');
        }

        // The Api version named in latest.txt ("api 2.2.0.0"), or null: an older latest.txt, or none needed.
        public static string Api(string text)
        {
            foreach (var line in (text ?? "").Split('\n'))
            {
                var l = line.Trim();
                if (l.StartsWith("api ")) return l.Substring(4).Trim();
            }
            return null;
        }

        // Whether 'latest' comes after 'current', compared figure by figure; a test or beta build
        // ("0.8.0-beta.1") comes before its release (0.8.0), so a beta player is offered the release.
        // A test or beta build is never offered to a player on a release (0.7.x can't know that: a beta
        // goes on GitHub as a pre-release, which releases/latest skips).
        public static bool Newer(string latest, string current)
        {
            if ((latest ?? "").Contains("-") && !(current ?? "").Contains("-")) return false;
            int[] a = Parts(latest), b = Parts(current);
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
                if (x != y) return x > y;
            }
            bool betaOffered = (latest ?? "").Contains("-"), onBeta = (current ?? "").Contains("-");
            return betaOffered && onBeta ? Pre(latest, current) > 0 : onBeta && !betaOffered;
        }

        // The part after the dash, piece by piece ("beta.10" after "beta.9").
        static int Pre(string a, string b)
        {
            string[] x = a.Substring(a.IndexOf('-') + 1).Split('.'), y = b.Substring(b.IndexOf('-') + 1).Split('.');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                if (i >= x.Length || i >= y.Length) return x.Length - y.Length;
                int c = int.TryParse(x[i], out int m) && int.TryParse(y[i], out int n) ? m.CompareTo(n) : string.CompareOrdinal(x[i], y[i]);
                if (c != 0) return c;
            }
            return 0;
        }

        static int[] Parts(string v) => (v ?? "").Split('-')[0].Split('.').Select(s => int.TryParse(s, out int n) ? n : 0).ToArray();

        // DigestInfo for SHA-256, as it sits in front of the hash in a PKCS#1 v1.5 signature.
        static readonly byte[] Sha256Info = { 0x30, 0x31, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x01, 0x05, 0x00, 0x04, 0x20 };

        // Whether 'signature' is the key's signature of 'data'.
        public static bool Verify(byte[] data, byte[] signature, string modulusBase64)
        {
            byte[] mod = Convert.FromBase64String(modulusBase64);
            int k = mod.Length;
            if (data == null || signature == null || signature.Length != k) return false;
            BigInteger n = Unsigned(mod), s = Unsigned(signature);
            if (s >= n) return false;
            byte[] m = BigEndian(BigInteger.ModPow(s, 65537, n), k);
            byte[] hash;
            using (var sha = new SHA256Managed()) hash = sha.ComputeHash(data);
            // 00 01 FF..FF 00, then the DigestInfo and the hash.
            int sep = k - Sha256Info.Length - hash.Length - 1;
            if (sep < 10 || m[0] != 0 || m[1] != 1 || m[sep] != 0) return false;
            for (int i = 2; i < sep; i++) if (m[i] != 0xFF) return false;
            int diff = 0;
            for (int i = 0; i < Sha256Info.Length; i++) diff |= m[sep + 1 + i] ^ Sha256Info[i];
            for (int i = 0; i < hash.Length; i++) diff |= m[sep + 1 + Sha256Info.Length + i] ^ hash[i];
            return diff == 0;
        }

        static BigInteger Unsigned(byte[] bigEndian) => new BigInteger(bigEndian.Reverse().Concat(new byte[] { 0 }).ToArray());

        static byte[] BigEndian(BigInteger v, int length)
        {
            byte[] little = v.ToByteArray(), big = new byte[length];
            for (int i = 0; i < Math.Min(little.Length, length); i++) big[length - 1 - i] = little[i];
            return big;
        }
    }
}
