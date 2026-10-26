// RDR2 Co-op installer for Windows (the Linux one is setup.py; same steps).
// Double-click RDR2CoopSetup.exe in the unpacked download. It finds the game
// (Steam, Rockstar Games Launcher or Epic), asks a few questions, installs
// ScriptHookRDR2 (downloaded from its author's site, checksum-checked) and the
// co-op mod with its own .NET (RDR2Coop.asi + RDR2Coop\). A host who says yes to
// enRage gets the ScriptHookRDR2DotNet version instead (ScriptHookRDR2DotNet is
// bundled for that) with the unofficial co-op patch applied to their own enRage.
//   RDR2CoopSetup.exe --uninstall   takes it out again
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

static class Setup
{
    const string ShrUrl = "https://www.dev-c.com/files/ScriptHookRDR2_1.0.1491.17.zip";
    const string ShrPage = "https://www.dev-c.com/rdr2/scripthookrdr2/";
    // Runs inside the game, so only the exact file we tested is installed.
    const string ShrSha256 = "a3be69dcd33e6cffe316d7c17ee5c4f7fcedf0c7b7fa8cf68177398e6594c39f";

    static readonly string Here = AppDomain.CurrentDomain.BaseDirectory;
    static string PatchDir => Path.Combine(Here, "enrage-coop-patch");

    static int Main(string[] args)
    {
        try
        {
            string v = Path.Combine(Here, "VERSION.txt");
            Console.WriteLine("RDR2 Co-op " + (File.Exists(v) ? File.ReadAllText(v).Trim() : "?") + " setup\n");
            string game = PickGame();
            Console.WriteLine("Game: " + game + "\n");
            if (args.Contains("--uninstall") || (!args.Contains("--install")
                                                 && (File.Exists(Path.Combine(game, "RDR2Coop.asi")) || File.Exists(Path.Combine(game, "scripts", "RDR2Coop.dll")))
                                                 && Ask("Already installed. Type U to uninstall, or Enter to install/update", "") .Trim().ToUpperInvariant() == "U"))
            {
                Uninstall(game);
                return 0;
            }
            Install(game);
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("\nSomething went wrong: " + e.Message);
            if (e is UnauthorizedAccessException)
                Console.WriteLine("The game folder needs administrator rights: right-click RDR2CoopSetup.exe > Run as administrator.");
            return 1;
        }
        finally
        {
            if (!Console.IsInputRedirected) { Console.Write("\nPress Enter to close."); Console.ReadLine(); }
        }
    }

    // ------------------------------------------------------------ install
    static void Install(string game)
    {
        Console.WriteLine("A few questions (Enter keeps the suggestion):");
        string name = Ask("Your name, shown to the other players", Environment.UserName);
        bool runServer = Yes("Run the co-op server in your game (friends connect to you; no separate server needed)?", false);
        string server = runServer ? "127.0.0.1" : "";
        while (server.Length == 0) server = Ask("Co-op server address (ask whoever runs the server)", "");
        string port = Ask("Server port", "27460");
        string password;
        while (true)
        {
            password = ReadSecret("Room password (" + (runServer ? "pick one and tell your friends" : "ask whoever runs the server") + "; not shown as you type): ");
            if (password.Length > 0 && password == ReadSecret("Room password again: ")) break;
            Console.WriteLine("  The two didn't match (or were empty); try again.");
        }
        bool host = Yes("Do you usually host (your story, missions and enRage world are the shared ones)?", runServer);
        bool listed = runServer && Yes("List your game in the online server browser (anyone can find it; they still need the password)?", false);

        string scripts = Path.Combine(game, "scripts");
        string nem = Path.Combine(scripts, "NemesisMod.dll");
        // The enRage link only works in the ScriptHookRDR2DotNet version of the mod, so that question comes first.
        bool useEnrage = host && Directory.Exists(PatchDir)   // the optional enRage co-op patch download is unpacked here
                         && Yes("Install enRage (Nexus mod 10967) with the unofficial co-op patch? This uses the ScriptHookRDR2DotNet version of co-op", File.Exists(nem));

        Console.WriteLine("\nInstalling:");
        InstallScriptHook(game);
        Directory.CreateDirectory(scripts);
        string asi = Path.Combine(game, "RDR2Coop.asi"), folder = Path.Combine(game, "RDR2Coop"), old = Path.Combine(scripts, "RDR2Coop.dll");
        // Unpacked straight into the game folder: the files are already in place (deleting first would destroy them)
        bool inPlace = string.Equals(Path.GetFullPath(Path.Combine(Here, "RDR2Coop")), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(folder) && (useEnrage || !inPlace)) Directory.Delete(folder, true);   // a clean copy, so an older runtime doesn't pile up beside the new one
        if (useEnrage)
        {
            InstallShdn(game);
            File.Copy(Path.Combine(Here, "shdn", "scripts", "RDR2Coop.dll"), old, true);
            if (File.Exists(asi)) File.Delete(asi);   // only one copy of the mod may run
        }
        else
        {
            if (inPlace) Console.WriteLine("  RDR2Coop.asi and RDR2Coop\\ are already in the game folder");
            else
            {
                CopyDir(Path.Combine(Here, "RDR2Coop"), folder);
                File.Copy(Path.Combine(Here, "RDR2Coop.asi"), asi, true);
            }
            if (File.Exists(old))   // the ScriptHookRDR2DotNet version: two copies would both join the room and fight
            {
                File.Delete(old);
                Console.WriteLine("  old ScriptHookRDR2DotNet copy of the mod (scripts\\RDR2Coop.dll) removed; your settings are kept");
            }
        }
        string ini = Path.Combine(scripts, "RDR2Coop.ini");
        if (!File.Exists(ini) || !File.ReadAllText(ini).Contains("Server="))
            File.Copy(Path.Combine(Here, "scripts", "RDR2Coop.ini"), ini, true);
        SetIni(ini, new Dictionary<string, string> { { "Name", name }, { "Server", server }, { "Port", port },
                                                     { "Password", password }, { "PreferHost", host ? "Yes" : "No" },
                                                     { "RunServer", runServer ? "Yes" : "No" }, { "Public", listed ? "Yes" : "No" } });
        Console.WriteLine("  co-op mod installed; settings in " + ini);
        if (runServer)
        {
            AllowThroughFirewall(game, port);
            Console.WriteLine("  The server starts with your game and opens UDP " + port + " on your router by itself (UPnP);");
            Console.WriteLine("  F10 in game shows the address to give your friends. No UPnP on your router: forward UDP");
            Console.WriteLine("  " + port + " to this PC by hand.");
        }

        if (useEnrage)
            InstallEnragePatch(scripts);
        else if (host && File.Exists(nem) && !Directory.Exists(PatchDir))
            Console.WriteLine("\nYou have enRage: the optional enRage co-op patch (a separate download) shares its world with your guests.");
        else if (File.Exists(nem))
        {
            Console.WriteLine("\nenRage runs on the host only for now (a second copy would grow its own separate world).");
            if (Yes("Switch your enRage off while you play co-op as a guest?", true))
            {
                MoveOver(nem, nem + ".off-for-coop");
                Console.WriteLine("  enRage switched off (uninstalling switches it back on)");
            }
        }
        Console.WriteLine("\nDone. Start the game as usual; a subtitle says when you've joined.");
        Console.WriteLine("In game: Insert reloads the mod, F10 shows co-op status.");
    }

    static void InstallScriptHook(string game)
    {
        if (!File.Exists(Path.Combine(game, "ScriptHookRDR2.dll")) || !File.Exists(Path.Combine(game, "dinput8.dll")))
        {
            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rdr2-coop");
            Directory.CreateDirectory(cache);
            string zip = Path.Combine(cache, "ScriptHookRDR2_1.0.1491.17.zip");
            if (!File.Exists(zip) || Sha256(zip) != ShrSha256)
            {
                Console.WriteLine("  downloading ScriptHookRDR2 ...");
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    wc.Headers.Add("Referer", ShrPage);
                    wc.DownloadFile(ShrUrl, zip);
                }
                if (Sha256(zip) != ShrSha256)
                {
                    File.Delete(zip);
                    throw new Exception("the ScriptHookRDR2 download isn't the file this installer was tested with, so it wasn't installed. " +
                                        "Get it from " + ShrPage + " or ask whoever gave you the co-op mod.");
                }
            }
            using (var z = ZipFile.OpenRead(zip))
                foreach (var name in new[] { "bin/ScriptHookRDR2.dll", "bin/dinput8.dll" })
                    z.GetEntry(name).ExtractToFile(Path.Combine(game, Path.GetFileName(name)), true);
            Console.WriteLine("  ScriptHookRDR2 1.0.1491.17 installed");
        }
    }

    // ScriptHookRDR2DotNet V2: only the enRage (.NET 4.8) version of the mod needs it.
    static void InstallShdn(string game)
    {
        if (!File.Exists(Path.Combine(game, "ScriptHookRDRDotNet.asi")))
        {
            // Bundled (zlib licence, see scripthookdotnet\LICENSE.txt).
            foreach (var name in new[] { "ScriptHookRDRDotNet.asi", "ScriptHookRDRDotNet.ini", "ScriptHookRDRNetAPI.dll" })
                File.Copy(Path.Combine(Here, "scripthookdotnet", name), Path.Combine(game, name), true);
            Console.WriteLine("  ScriptHookRDR2DotNet V2 2.2.0 installed");
        }
    }

    // ------------------------------------------------------------ enRage
    static bool InstallEnragePatch(string scripts)
    {
        var info = File.ReadAllLines(Path.Combine(PatchDir, "patch.txt")).Where(l => l.Contains("="))
                       .ToDictionary(l => l.Substring(0, l.IndexOf('=')), l => l.Substring(l.IndexOf('=') + 1).Trim());
        string bas = info["base_sha256"], patched = info["patched_sha256"], version = info["enrage_version"];
        Console.WriteLine("  enRage co-op patch " + (info.TryGetValue("patch_version", out var pv) ? pv : "?"));
        string dll = Path.Combine(scripts, "NemesisMod.dll"), official = dll + ".official";

        string current = File.Exists(dll) ? Sha256(dll) : null;
        if (current == patched) { Console.WriteLine("  enRage already has the co-op patch"); return true; }
        if (current != bas && File.Exists(official) && Sha256(official) == bas) { File.Copy(official, dll, true); current = bas; }
        if (current != bas && !CopyOfficialEnrage(scripts, bas, version)) return false;

        File.Copy(dll, official, true);
        byte[] result = ApplyPatch(File.ReadAllBytes(dll), File.ReadAllBytes(Path.Combine(PatchDir, "NemesisMod.coop.patch")));
        if (Hex(SHA256.Create().ComputeHash(result)) != patched)
        {
            Console.WriteLine("  the patched enRage didn't come out right: enRage left as the official version");
            return false;
        }
        File.WriteAllBytes(dll, result);
        // Opening enRage's menu would otherwise freeze the host's world for everyone.
        string nemIni = Path.Combine(scripts, "NemesisMod.ini");
        if (File.Exists(nemIni)) SetIni(nemIni, new Dictionary<string, string> { { "PauseWorld", "False" } });
        else File.WriteAllText(nemIni, "[Menu]\r\nPauseWorld=False\r\n");
        Console.WriteLine("  enRage " + version + " + unofficial co-op patch installed (official copy kept as NemesisMod.dll.official)");
        return true;
    }

    // The player's own official enRage: from the installer's folder if they put
    // the Nexus zip (or its unpacked files) there, else from a path they give.
    static bool CopyOfficialEnrage(string scripts, string bas, string version)
    {
        string work = Path.Combine(Path.GetTempPath(), "rdr2coop-" + Guid.NewGuid().ToString("N"));
        try
        {
            string src = null;
            int i = 0;
            foreach (var z in Directory.GetFiles(Here, "*.zip"))
                src = src ?? EnrageIn(z, bas, Path.Combine(work, (i++).ToString()));
            src = src ?? EnrageIn(Here, bas, work);
            if (src != null) Console.WriteLine("  using the enRage found in the installer's folder");
            else
            {
                Console.WriteLine("\n  The co-op patch needs your own copy of enRage " + version + " from Nexus:");
                Console.WriteLine("  https://www.nexusmods.com/reddeadredemption2/mods/10967 (Files tab, version " + version + ")");
                Console.WriteLine("  (or put the zip next to RDR2CoopSetup.exe and run it again)");
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string guess = Directory.Exists(downloads)
                    ? Directory.GetFiles(downloads, "*.zip").Where(f => Regex.IsMatch(Path.GetFileName(f), "enrage|nemesis", RegexOptions.IgnoreCase))
                               .OrderBy(File.GetLastWriteTime).LastOrDefault() ?? "" : "";
                string answer = Ask("  Path to the enRage zip (or its unpacked folder)", guess).Trim('"');
                if (answer.Length > 0) src = EnrageIn(answer, bas, Path.Combine(work, "asked"));
            }
            if (src == null) { Console.WriteLine("  no enRage " + version + " found: enRage left as it is"); return false; }
            string dll = Path.Combine(scripts, "NemesisMod.dll");
            if (File.Exists(dll)) File.Copy(dll, dll + ".before-coop", true);   // some other enRage version: kept
            // Only enRage's own files, never whatever else sits next to it.
            foreach (var f in Directory.GetFiles(src))
            {
                string n = Path.GetFileName(f), target = Path.Combine(scripts, n);
                if (n == "NemesisMod.dll" || n == "Newtonsoft.Json.dll" || (n.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) && !File.Exists(target)))
                    File.Copy(f, target, true);
            }
            return true;
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { }
        }
    }

    static string EnrageIn(string path, string bas, string work)
    {
        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try { ZipFile.ExtractToDirectory(path, work); } catch (InvalidDataException) { return null; }
            path = work;
        }
        if (!Directory.Exists(path)) return null;
        foreach (var dll in Directory.GetFiles(path, "NemesisMod.dll", SearchOption.AllDirectories))
        {
            if (Sha256(dll) == bas) return Path.GetDirectoryName(dll);
            Console.WriteLine("  " + dll + " is a different enRage version");
        }
        return null;
    }

    // RDCOPAT1: bsdiff's control/diff/extra blocks, each raw-deflate compressed.
    internal static byte[] ApplyPatch(byte[] old, byte[] patch)
    {
        if (Encoding.ASCII.GetString(patch, 0, 8) != "RDCOPAT1") throw new InvalidDataException("not an RDCOPAT1 patch");
        long newSize = BitConverter.ToInt64(patch, 8);
        int lc = (int)BitConverter.ToInt64(patch, 16), ld = (int)BitConverter.ToInt64(patch, 24), le = (int)BitConverter.ToInt64(patch, 32);
        byte[] ctrl = Inflate(patch, 40, lc), diff = Inflate(patch, 40 + lc, ld), extra = Inflate(patch, 40 + lc + ld, le);
        var result = new byte[newSize];
        long oldPos = 0, newPos = 0; int ci = 0, di = 0, ei = 0;
        while (newPos < newSize)
        {
            long x = Offtin(ctrl, ci), y = Offtin(ctrl, ci + 8), z = Offtin(ctrl, ci + 16);
            ci += 24;
            if (x < 0 || y < 0 || newPos + x + y > newSize) throw new InvalidDataException("corrupt patch");
            for (long i = 0; i < x; i++)
            {
                long o = oldPos + i;
                result[newPos + i] = (byte)(diff[di + i] + (o >= 0 && o < old.Length ? old[o] : 0));
            }
            di += (int)x; newPos += x; oldPos += x;
            Array.Copy(extra, ei, result, newPos, y);
            ei += (int)y; newPos += y; oldPos += z;
        }
        return result;
    }

    static long Offtin(byte[] b, int at)
    {
        ulong v = BitConverter.ToUInt64(b, at);
        return (v & 0x8000000000000000UL) != 0 ? -(long)(v & 0x7FFFFFFFFFFFFFFFUL) : (long)v;
    }

    static byte[] Inflate(byte[] data, int at, int length)
    {
        using (var input = new MemoryStream(data, at, length))
        using (var d = new DeflateStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            d.CopyTo(output);
            return output.ToArray();
        }
    }

    // ------------------------------------------------------------ uninstall
    static void Uninstall(string game)
    {
        string scripts = Path.Combine(game, "scripts");
        if (File.Exists(Path.Combine(game, "RDR2Coop.asi"))) File.Delete(Path.Combine(game, "RDR2Coop.asi"));
        if (Directory.Exists(Path.Combine(game, "RDR2Coop"))) Directory.Delete(Path.Combine(game, "RDR2Coop"), true);
        foreach (var f in new[] { "RDR2Coop.dll", "RDR2Coop.ini", "RDR2Coop.log", "RDR2Coop.Host.log", "RDR2Coop.Core.log" })
            if (File.Exists(Path.Combine(scripts, f))) File.Delete(Path.Combine(scripts, f));
        Netsh("advfirewall firewall delete rule name=\"" + FirewallRule + "\"");
        string dll = Path.Combine(scripts, "NemesisMod.dll");
        if (File.Exists(dll + ".off-for-coop")) { MoveOver(dll + ".off-for-coop", dll); Console.WriteLine("  enRage switched back on"); }
        if (File.Exists(dll + ".official")) { MoveOver(dll + ".official", dll); Console.WriteLine("  official enRage put back"); }   // last: wins over a patched copy
        if (File.Exists(dll + ".before-coop")) { MoveOver(dll + ".before-coop", dll); Console.WriteLine("  your previous enRage version put back"); }
        Console.WriteLine("  co-op mod removed (ScriptHookRDR2 and ScriptHookRDR2DotNet are left for your other mods)");
    }

    // ------------------------------------------------------------ finding the game
    static string PickGame()
    {
        var games = FindGames().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (games.Count == 1) return games[0];
        if (games.Count > 1)
        {
            for (int i = 0; i < games.Count; i++) Console.WriteLine("  " + (i + 1) + ". " + games[i]);
            if (int.TryParse(Ask("Which one", "1"), out int n) && n >= 1 && n <= games.Count) return games[n - 1];
            return games[0];
        }
        string path = Ask("RDR2 not found. Path to the folder with RDR2.exe", "").Trim('"');
        if (!File.Exists(Path.Combine(path, "RDR2.exe"))) throw new Exception("RDR2.exe isn't in that folder.");
        return path;
    }

    static IEnumerable<string> FindGames()
    {
        var dirs = new List<string>();
        // Steam: every library in libraryfolders.vdf.
        foreach (var steam in new[] { Reg(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath"),
                                      Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath") })
        {
            if (steam == null) continue;
            dirs.Add(steam);
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    dirs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        var found = dirs.Select(d => Path.Combine(d, "steamapps", "common", "Red Dead Redemption 2")).ToList();
        // Rockstar Games Launcher.
        found.Add(Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Rockstar Games\Red Dead Redemption 2", "InstallFolder"));
        // Epic Games Launcher manifests.
        string epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (Directory.Exists(epic))
            foreach (var item in Directory.GetFiles(epic, "*.item"))
            {
                string text = File.ReadAllText(item);
                var m = Regex.Match(text, "\"InstallLocation\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success && text.Contains("Red Dead Redemption 2")) found.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
        return found.Where(d => d != null && File.Exists(Path.Combine(d, "RDR2.exe")));
    }

    static string Reg(string key, string value)
    {
        try { return Registry.GetValue(key, value, null) as string; } catch (Exception) { return null; }
    }

    // ------------------------------------------------------------ helpers
    static string Ask(string q, string def)
    {
        Console.Write(q + (def.Length > 0 ? " [" + def + "]" : "") + ": ");
        string a = (Console.ReadLine() ?? "").Trim();
        return a.Length > 0 ? a : def;
    }

    static bool Yes(string q, bool def)
    {
        while (true)
        {
            string a = Ask(q + (def ? " [Y/n]" : " [y/N]"), "").ToLowerInvariant();
            if (a.Length == 0) return def;
            if (a == "y" || a == "yes") return true;
            if (a == "n" || a == "no") return false;
        }
    }

    static string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected) return (Console.ReadLine() ?? "").Trim();
        var sb = new StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString().Trim();
    }

    // key=value lines set in place, comments and other settings kept; values literal.
    const string FirewallRule = "RDR2 Co-op server";

    // The game's own server: let RDR2.exe receive on the co-op port (otherwise
    // Windows asks the first time, and that prompt hides behind the game).
    static void AllowThroughFirewall(string game, string port)
    {
        Netsh("advfirewall firewall delete rule name=\"" + FirewallRule + "\"");
        bool ok = Netsh("advfirewall firewall add rule name=\"" + FirewallRule + "\" dir=in action=allow protocol=UDP localport=" + port +
                        " program=\"" + Path.Combine(game, "RDR2.exe") + "\" profile=any");
        Console.WriteLine(ok ? "  Windows Firewall: RDR2 may receive on UDP " + port
                             : "  Couldn't add the Windows Firewall rule: allow RDR2 when Windows asks.");
    }

    static bool Netsh(string args)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("netsh", args)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (Exception) { return false; }
    }

    static void SetIni(string path, Dictionary<string, string> values)
    {
        string text = File.Exists(path) ? File.ReadAllText(path) : "";
        foreach (var kv in values)
        {
            var re = new Regex("^" + Regex.Escape(kv.Key) + "=[^\\r\\n]*", RegexOptions.Multiline);
            var coop = new Regex(@"^\[Coop\][ \t]*\r?$", RegexOptions.Multiline);
            if (re.IsMatch(text)) text = re.Replace(text, m => kv.Key + "=" + kv.Value, 1);
            else if (coop.IsMatch(text)) text = coop.Replace(text, m => m.Value.TrimEnd('\r') + "\r\n" + kv.Key + "=" + kv.Value, 1);   // new setting in an older file: into [Coop]
            else text += "\r\n" + kv.Key + "=" + kv.Value + "\r\n";
        }
        File.WriteAllText(path, text);
    }

    static void MoveOver(string from, string to)
    {
        if (File.Exists(to)) File.Delete(to);
        File.Move(from, to);
    }

    static void CopyDir(string from, string to)   // .NET Framework has no recursive copy
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    static string Sha256(string path)
    {
        using (var s = File.OpenRead(path)) return Hex(SHA256.Create().ComputeHash(s));
    }

    static string Hex(byte[] b) => string.Concat(b.Select(x => x.ToString("x2")));
}
