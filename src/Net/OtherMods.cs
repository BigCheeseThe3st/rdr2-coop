// Other mods next to ours, listed in the log at every start (the author, 1 Nov: "we need a way of knowing if players
// have other mods installed": MR ZOOL's frozen, T-posing partners came from his other mods, found only by asking).
// Names only: what the game folder holds that the game doesn't ship, scripts\ (ScriptHook mods) and lml\ (Lenny's
// Mod Loader). Our own files and all logs are left out.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RDR2Coop.Net
{
    public static class OtherMods
    {
        // What Steam, Rockstar and Epic copies install, and what's ours: not listed.
        static readonly string[] Stock = { "RDR2.exe", "PlayRDR2.exe", "amd_ags_x64.dll", "bink2w64.dll", "dxilconv7.dll",
            "ffx_fsr2_api_dx12_x64.dll", "ffx_fsr2_api_vk_x64.dll", "ffx_fsr2_api_x64.dll", "NvLowLatencyVk.dll", "nvngx_dlss.dll",
            "oo2core_5_win64.dll", "steam_api64.dll", "EOSSDK-Win64-Shipping.dll", "installscript.vdf", "installscript_sdk.vdf",
            "title.rgl", "index.bin", "x64", "Redistributables", "scripts", "lml",
            "RDR2Coop", "RDR2Coop.asi", "README-PLAYERS.txt", "README-WINDOWS.txt", "CHANGELOG.txt", "VERSION.txt" };
        const int MaxNames = 40;

        public static string Describe(string gameDir)
        {
            try
            {
                string lmlDir = Path.Combine(gameDir, "lml");
                string lml = Directory.Exists(lmlDir)
                    ? $"{Directory.GetDirectories(lmlDir).Length} mod folder(s): {List(Directory.GetDirectories(lmlDir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}"
                    : "none";
                return $"other mods: game folder: {List(Names(gameDir, n => !IsStock(n)))}; " +
                       $"scripts\\: {List(Names(Path.Combine(gameDir, "scripts"), n => !n.StartsWith("RDR2Coop", StringComparison.OrdinalIgnoreCase)))}; lml\\: {lml}";
            }
            catch (Exception e) { return "other mods: couldn't look (" + e.GetType().Name + ")"; }
        }

        static bool IsStock(string n) =>
            n.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) || n.StartsWith("RDR2Coop.asi.old-", StringComparison.OrdinalIgnoreCase)
            || Stock.Any(s => s.Equals(n, StringComparison.OrdinalIgnoreCase));

        // Folders end in a backslash; logs never count.
        static IEnumerable<string> Names(string dir, Func<string, bool> keep) =>
            !Directory.Exists(dir) ? Enumerable.Empty<string>()
            : Directory.GetDirectories(dir).Select(d => Path.GetFileName(d)).Where(keep).Select(n => n + "\\")
                .Concat(Directory.GetFiles(dir).Select(f => Path.GetFileName(f)).Where(n => keep(n) && !n.EndsWith(".log", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        static string List(IEnumerable<string> names)
        {
            var all = names.ToList();
            if (all.Count == 0) return "none";
            return string.Join(", ", all.Take(MaxNames)) + (all.Count > MaxNames ? $" (+{all.Count - MaxNames} more)" : "");
        }
    }
}
