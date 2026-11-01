// The host's world for guests (0.9.0-beta.9, [Gameplay] HostWorld): a guest plays in the host's story (chapter, camp,
// finished missions, world state) while keeping their own player (cash, honor, horse, clothes, items), with no Load
// Game. The story lives in nine blocks of save globals (Global_40..36307, 36268 slots, as the game's own softsave
// copies them); the guest's game takes the host's through the game's own menu-less restore once (Mod/HostWorld.cs),
// then the host streams what changes. Pure: layout, which slots are the guest's own, the full transfer (deflated,
// in pieces, resends, SHA-256), the changes, and the 1024-slot sums that catch lost changes.
//
// The WorldShare notice (one text line):
//   want                                          guest -> host: send me your world
//   full <id> <part> <parts> <sha16> <model> <base64>   host -> guest: piece <part> of transfer <id> (deflated blob)
//   miss <id> <part> <part> ...                   guest -> host: these pieces didn't arrive
//   diff <seq> <start>+<count>=<base64> ...        host -> guest: these slots now hold these values (absolute global index)
//   sums <seq> <first chunk> <crc> <crc> ...      host -> guest: CRC32 of 1024-slot chunks (guest-own slots counted as 0)
//   chunk <k>                                     guest -> host: send chunk k's values again (as diff lines)
//   reswap                                        host -> guest: camp or chapter changed: take my world again with a restore
//   left                                          guest -> host: I'm back in my own world
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace RDR2Coop.Net
{
    public static class WorldLayout
    {
        // live start, softsave staging array base, size (slots); slot s of block k is at base + 1 + s * size
        public static readonly int[,] Blocks =
        {
            { 40, 2621440, 12066 }, { 12106, 2645573, 5398 }, { 17504, 2656370, 3206 }, { 20710, 2662783, 2408 },
            { 23118, 2667600, 1769 }, { 24887, 2671139, 1909 }, { 26796, 2674958, 777 }, { 27573, 2676513, 4501 }, { 32074, 2685516, 4234 },
        };
        public const int First = 40, Total = 36268, ChunkSlots = 1024;
        public static int Chunks => (Total + ChunkSlots - 1) / ChunkSlots;

        // The guest's own (absolute global, length): spawn/clock/bounty fields, horse, outfits, honor and cores, items,
        // cheats and the save checksum, and the whole outfit block Global_26796. Proven 28 Oct: the 100% save's world in
        // George's game kept his cash, clothes and map discovery (map and cash are engine side anyway).
        public static readonly int[,] Own = { { 49, 30 }, { 1135, 3142 }, { 7197, 599 }, { 11135, 71 }, { 11983, 123 }, { 26796, 777 } };

        static bool[] _own;
        public static bool IsOwn(int global)
        {
            if (_own == null)
            {
                var o = new bool[Total];
                for (int r = 0; r < Own.GetLength(0); r++) for (int j = 0; j < Own[r, 1]; j++) o[Own[r, 0] + j - First] = true;
                _own = o;
            }
            int i = global - First;
            return i >= 0 && i < Total && _own[i];
        }

        /// <summary>The host's world with the guest's own slots kept: what goes into the restore slot.</summary>
        public static ulong[] Merge(ulong[] host, ulong[] mine)
        {
            var m = (ulong[])host.Clone();
            for (int i = 0; i < Total; i++) if (IsOwn(First + i)) m[i] = mine[i];
            return m;
        }

        public static uint ChunkSum(ulong[] world, int chunk)
        {
            uint crc = 0xFFFFFFFF;
            int from = chunk * ChunkSlots, to = Math.Min(Total, from + ChunkSlots);
            for (int i = from; i < to; i++)
            {
                ulong v = IsOwn(First + i) ? 0 : world[i];
                for (int b = 0; b < 8; b++) crc = Crc(crc, (byte)(v >> (8 * b)));
            }
            return ~crc;
        }

        static uint[] _table;
        static uint Crc(uint crc, byte b)
        {
            if (_table == null)
            {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; t[n] = c; }
                _table = t;
            }
            return _table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
    }

    public static class WorldShare
    {
        public const int PieceBytes = 720;          // raw bytes per piece: base64 ~960 chars, under NoticeMsg.MaxBytes with the header
        public const int SendEveryMs = 500, SumsEveryMs = 5000, PiecesPerTick = 24;
        const int DiffLineBytes = 1000;

        public static byte[] Pack(ulong[] world)
        {
            var raw = new byte[world.Length * 8];
            Buffer.BlockCopy(world, 0, raw, 0, raw.Length);
            using (var ms = new MemoryStream())
            {
                using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
                return ms.ToArray();
            }
        }

        public static ulong[] Unpack(byte[] packed)
        {
            using (var z = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress))
            using (var ms = new MemoryStream())
            {
                z.CopyTo(ms);
                var raw = ms.ToArray();
                if (raw.Length != WorldLayout.Total * 8) return null;
                var w = new ulong[WorldLayout.Total];
                Buffer.BlockCopy(raw, 0, w, 0, raw.Length);
                return w;
            }
        }

        public static string Sha16(byte[] data)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return string.Concat(sha.ComputeHash(data).Take(8).Select(b => b.ToString("x2")));
        }

        /// <summary>The full-transfer lines for a packed world.</summary>
        public static List<string> Pieces(uint id, byte[] packed, uint model)
        {
            int parts = (packed.Length + PieceBytes - 1) / PieceBytes;
            string sha = Sha16(packed);
            var lines = new List<string>(parts);
            for (int p = 0; p < parts; p++)
            {
                int n = Math.Min(PieceBytes, packed.Length - p * PieceBytes);
                lines.Add($"full {id} {p} {parts} {sha} {model:x8} " + Convert.ToBase64String(packed, p * PieceBytes, n));
            }
            return lines;
        }

        /// <summary>The changes from old to now as diff lines (each under the notice size), guest-own slots left out.
        /// Runs of changed slots; a gap of 2 unchanged slots or fewer is sent inside a run.</summary>
        public static List<string> Diff(uint seq, ulong[] old, ulong[] now)
        {
            var runs = new List<(int start, int count)>();
            int i = 0;
            while (i < WorldLayout.Total)
            {
                if (now[i] == old[i] || WorldLayout.IsOwn(WorldLayout.First + i)) { i++; continue; }
                int s = i, last = i;
                for (int j = i + 1; j < WorldLayout.Total && j <= last + 3; j++)
                    if (now[j] != old[j] && !WorldLayout.IsOwn(WorldLayout.First + j)) last = j;
                runs.Add((s, last - s + 1));
                i = last + 1;
            }
            return Lines(seq, now, runs);
        }

        /// <summary>One chunk's values as diff lines (a resend after a sum mismatch).</summary>
        public static List<string> Chunk(uint seq, ulong[] now, int chunk)
        {
            var runs = new List<(int, int)>();
            int from = chunk * WorldLayout.ChunkSlots, to = Math.Min(WorldLayout.Total, from + WorldLayout.ChunkSlots);
            int s = -1;
            for (int i = from; i <= to; i++)
            {
                bool send = i < to && !WorldLayout.IsOwn(WorldLayout.First + i);
                if (send && s < 0) s = i;
                if (!send && s >= 0) { runs.Add((s, i - s)); s = -1; }
            }
            return Lines(seq, now, runs);
        }

        static List<string> Lines(uint seq, ulong[] now, List<(int start, int count)> runs)
        {
            var lines = new List<string>();
            var sb = new StringBuilder();
            string head = $"diff {seq}";
            foreach (var r in runs)
            {
                // split long runs so one item fits a line
                for (int off = 0; off < r.count; off += 90)
                {
                    int n = Math.Min(90, r.count - off);
                    var bytes = new byte[n * 8];
                    Buffer.BlockCopy(now, (r.start + off) * 8, bytes, 0, bytes.Length);
                    string item = $" {WorldLayout.First + r.start + off}+{n}=" + Convert.ToBase64String(bytes);
                    if (sb.Length > 0 && sb.Length + item.Length > DiffLineBytes) { lines.Add(head + sb); sb.Clear(); }
                    sb.Append(item);
                }
            }
            if (sb.Length > 0) lines.Add(head + sb);
            return lines;
        }

        /// <summary>Applies a diff line to a world (guest-own slots never written). Returns the changed absolute
        /// globals, or null for a malformed line.</summary>
        public static List<int> Apply(string line, ulong[] world)
        {
            var f = line.Split(' ');
            if (f.Length < 2 || f[0] != "diff" || !uint.TryParse(f[1], out _)) return null;
            var changed = new List<int>();
            for (int k = 2; k < f.Length; k++)
            {
                int plus = f[k].IndexOf('+'), eq = f[k].IndexOf('=');
                if (plus <= 0 || eq <= plus) return null;
                if (!int.TryParse(f[k].Substring(0, plus), NumberStyles.None, CultureInfo.InvariantCulture, out int start)
                    || !int.TryParse(f[k].Substring(plus + 1, eq - plus - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return null;
                byte[] bytes;
                try { bytes = Convert.FromBase64String(f[k].Substring(eq + 1)); } catch (FormatException) { return null; }
                if (n < 1 || n > 90 || bytes.Length != n * 8 || start < WorldLayout.First || start + n > WorldLayout.First + WorldLayout.Total) return null;
                for (int j = 0; j < n; j++)
                {
                    int g = start + j;
                    if (WorldLayout.IsOwn(g)) continue;
                    world[g - WorldLayout.First] = BitConverter.ToUInt64(bytes, j * 8);
                    changed.Add(g);
                }
            }
            return changed;
        }

        public static List<string> Sums(uint seq, ulong[] world)
        {
            var sb = new StringBuilder($"sums {seq} 0");   // all 36 sums in one line (36 x 9 chars)
            for (int k = 0; k < WorldLayout.Chunks; k++) sb.Append(' ').Append(WorldLayout.ChunkSum(world, k).ToString("x8"));
            return new List<string> { sb.ToString() };
        }

        /// <summary>The chunks whose sums differ from ours (a sums line), or null for a malformed line.</summary>
        public static List<int> Mismatches(string line, ulong[] world)
        {
            var f = line.Split(' ');
            if (f.Length < 4 || f[0] != "sums" || !int.TryParse(f[2], out int first) || first < 0) return null;
            var bad = new List<int>();
            for (int k = 3; k < f.Length; k++)
            {
                int chunk = first + k - 3;
                if (chunk >= WorldLayout.Chunks) return null;
                if (!uint.TryParse(f[k], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint crc)) return null;
                if (WorldLayout.ChunkSum(world, chunk) != crc) bad.Add(chunk);
            }
            return bad;
        }
    }

    /// <summary>The guest's side of a full transfer: pieces in any order, the missing list, the SHA check.</summary>
    public sealed class WorldStateAssembler
    {
        public uint Id { get; private set; }
        public uint Model { get; private set; }
        string _sha;
        byte[][] _parts;

        /// <summary>Takes one full line. Returns the finished, checked world once every piece is in, else null.</summary>
        public ulong[] Add(string line)
        {
            var f = line.Split(' ');
            if (f.Length != 7 || f[0] != "full" || !uint.TryParse(f[1], out uint id) || !int.TryParse(f[2], out int part)
                || !int.TryParse(f[3], out int parts) || parts < 1 || parts > 2000 || part < 0 || part >= parts || f[4].Length != 16
                || !uint.TryParse(f[5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint model)) return null;
            if (_parts == null || id != Id || parts != _parts.Length || f[4] != _sha) { Id = id; _sha = f[4]; Model = model; _parts = new byte[parts][]; }
            try { _parts[part] = Convert.FromBase64String(f[6]); } catch (FormatException) { return null; }
            if (_parts.Any(p => p == null)) return null;
            var packed = _parts.SelectMany(p => p).ToArray();
            if (WorldShare.Sha16(packed) != _sha) { _parts = new byte[parts][]; return null; }   // a bad piece: start over
            try { return WorldShare.Unpack(packed); } catch (InvalidDataException) { return null; }
        }

        public List<int> Missing() => _parts == null ? new List<int>() : Enumerable.Range(0, _parts.Length).Where(i => _parts[i] == null).ToList();
        public bool Started => _parts != null;
        public void Reset() { _parts = null; }
    }
}
