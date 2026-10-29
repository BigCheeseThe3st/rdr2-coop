// The unofficial enRage co-op patch, applied in memory as the loader starts enRage: the player drops the official
// enRage into scripts\ and the patch rides in the mod (RDR2Coop\enrage-coop-patch\), so no installer is needed and the
// player's own file stays untouched. A copy the installer already patched on disk runs as it is; any other enRage
// version runs unpatched (without the co-op link) and the log says why. Same RDCOPAT1 format as setup.py's apply_patch.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace RDR2DN
{
	internal static class EnragePatch
	{
		/// <summary>
		/// The bytes to load for scripts\NemesisMod.dll: patched when they're the official enRage the patch is for.
		/// Any other file, or no patch in the mod: the bytes unchanged.
		/// </summary>
		internal static byte[] ForLoad(string filename, byte[] dll, string patchDir, Action<string> log)
		{
			if (!Path.GetFileName(filename).Equals("NemesisMod.dll", StringComparison.OrdinalIgnoreCase)) return dll;
			string txt = Path.Combine(patchDir, "patch.txt"), bin = Path.Combine(patchDir, "NemesisMod.coop.patch");
			if (!File.Exists(txt) || !File.Exists(bin))
			{
				log("enRage: no co-op patch in RDR2Coop\\enrage-coop-patch: enRage runs without the co-op link");
				return dll;
			}
			var info = new Dictionary<string, string>();
			foreach (var line in File.ReadAllLines(txt))
			{
				int eq = line.IndexOf('=');
				if (eq > 0) info[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
			}
			info.TryGetValue("base_sha256", out var baseSha);
			info.TryGetValue("patched_sha256", out var patchedSha);
			info.TryGetValue("enrage_version", out var version);
			info.TryGetValue("patch_version", out var patchVersion);
			string sha = Sha256(dll);
			if (sha == patchedSha)
			{
				log($"enRage: already has co-op patch {patchVersion} (patched by the installer)");
				return dll;
			}
			if (sha != baseSha)
			{
				log($"enRage: this NemesisMod.dll isn't the official enRage {version} the co-op patch {patchVersion} is for: it runs without the co-op link (get enRage {version} from Nexus, or a co-op update for your version)");
				return dll;
			}
			byte[] patched;
			try { patched = Apply(dll, File.ReadAllBytes(bin)); }
			catch (Exception e) { log("enRage: the co-op patch couldn't be applied (" + e.Message + "): enRage runs without the co-op link"); return dll; }
			if (Sha256(patched) != patchedSha)
			{
				log("enRage: the co-op patch came out wrong: enRage runs without the co-op link");
				return dll;
			}
			log($"enRage {version}: co-op patch {patchVersion} applied in memory (your NemesisMod.dll is unchanged)");
			try { if (NoMenuPause(Path.Combine(Path.GetDirectoryName(filename), "NemesisMod.ini"))) log("enRage: PauseWorld=False set in NemesisMod.ini (its menu would freeze the host's world for everyone)"); }
			catch (Exception e) { log("enRage: couldn't set PauseWorld=False in NemesisMod.ini: " + e.Message); }
			return patched;
		}

		/// <summary>enRage's menu pauses the world by default: in co-op that freezes it for every partner. True = changed.</summary>
		internal static bool NoMenuPause(string ini)
		{
			if (!File.Exists(ini)) { File.WriteAllText(ini, "[Menu]\r\nPauseWorld=False\r\n"); return true; }   // enRage writes the rest around it
			var lines = new List<string>(File.ReadAllLines(ini));
			int at = lines.FindIndex(l => l.TrimStart().StartsWith("PauseWorld", StringComparison.OrdinalIgnoreCase) && l.Contains('='));
			if (at >= 0)
			{
				if (lines[at].Substring(lines[at].IndexOf('=') + 1).Trim().Equals("False", StringComparison.OrdinalIgnoreCase)) return false;
				lines[at] = "PauseWorld=False";
			}
			else
			{
				int menu = lines.FindIndex(l => l.Trim().Equals("[Menu]", StringComparison.OrdinalIgnoreCase));
				if (menu >= 0) lines.Insert(menu + 1, "PauseWorld=False");
				else { lines.Add("[Menu]"); lines.Add("PauseWorld=False"); }
			}
			File.WriteAllLines(ini, lines);
			return true;
		}

		/// <summary>bsdiff's control/diff/extra blocks, each raw-deflate compressed, behind "RDCOPAT1" and four int64s.</summary>
		internal static byte[] Apply(byte[] old, byte[] patch)
		{
			if (patch.Length < 40 || System.Text.Encoding.ASCII.GetString(patch, 0, 8) != "RDCOPAT1") throw new InvalidDataException("not an RDCOPAT1 patch");
			long newSize = BitConverter.ToInt64(patch, 8), lc = BitConverter.ToInt64(patch, 16), ld = BitConverter.ToInt64(patch, 24), le = BitConverter.ToInt64(patch, 32);
			if (newSize < 0 || lc < 0 || ld < 0 || le < 0 || 40 + lc + ld + le > patch.Length) throw new InvalidDataException("corrupt patch");
			byte[] ctrl = Inflate(patch, 40, lc), diff = Inflate(patch, 40 + lc, ld), extra = Inflate(patch, 40 + lc + ld, le);
			var result = new byte[newSize];
			long oldPos = 0, newPos = 0, ci = 0, di = 0, ei = 0;
			while (newPos < newSize)
			{
				if (ci + 24 > ctrl.Length) throw new InvalidDataException("corrupt patch");
				long x = Offtin(ctrl, ci), y = Offtin(ctrl, ci + 8), z = Offtin(ctrl, ci + 16);
				ci += 24;
				if (x < 0 || y < 0 || newPos + x + y > newSize || di + x > diff.Length || ei + y > extra.Length) throw new InvalidDataException("corrupt patch");
				for (long i = 0; i < x; i++)
				{
					long o = oldPos + i;
					result[newPos + i] = (byte)(diff[di + i] + (o >= 0 && o < old.Length ? old[o] : 0));
				}
				di += x; newPos += x; oldPos += x;
				Array.Copy(extra, ei, result, newPos, y);
				ei += y; newPos += y; oldPos += z;
			}
			return result;
		}

		// bsdiff's sign-magnitude 64-bit integers.
		static long Offtin(byte[] b, long at)
		{
			ulong v = BitConverter.ToUInt64(b, (int)at);
			long m = (long)(v & 0x7FFFFFFFFFFFFFFF);
			return (v & 0x8000000000000000) != 0 ? -m : m;
		}

		static byte[] Inflate(byte[] src, long at, long count)
		{
			using var input = new DeflateStream(new MemoryStream(src, (int)at, (int)count), CompressionMode.Decompress);
			using var output = new MemoryStream();
			input.CopyTo(output);
			return output.ToArray();
		}

		static string Sha256(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
	}
}
