// The update notice and one-press update (the author, 24 Oct: "notice plus one-press
// update"). A background check reads the newest release's latest.txt from GitHub at the
// start and every 30 minutes after (27 Oct: "notify about updates without restarting
// the game or pressing Insert"); when it names a newer version the player is told once, and the co-op menu (F10 twice)
// offers "Update to x". Updating downloads that release's RDR2Coop.dll and its
// signature, checks the signature (UpdateCheck.Verify: only the release key passes)
// and swaps the file: ScriptHookRDR2DotNet runs a copy of it, so the file isn't
// locked, and Insert (or the next start) loads the new one. Settings are untouched.
// A beta also follows the beta channel: the release tagged beta-channel, whose
// latest.txt names the newest beta (GitHub's "latest" skips pre-releases).
// The one-press update swaps the mod only: when latest.txt says the release was built
// against a newer RDR2Coop.Api than the installed one (the loader zip), the player
// is told to download the zip instead (the .NET 9 build only).
// The ScriptHookRDR2DotNet build moves itself onto the new loader at its first start (MoveToLoader).
// [Coop] UpdateCheck=No turns the check off.
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class Updater
    {
        const string Releases = "https://github.com/BigCheeseThe3st/rdr2-coop/releases";
#if NET48
        const string Asset = "RDR2Coop.dll";        // the SHDN build: the name every installed mod already downloads
#else
        const string Asset = "RDR2Coop.net9.dll";   // the build for our own .NET 9 loader; installed as RDR2Coop\RDR2Coop.dll
#endif
        const int MaxBytes = 20 << 20;   // the mod is well under 1 MB; anything this big isn't it
        const int MaxZipBytes = 120 << 20;   // the loader zip with its .NET runtime is about 40 MB
        public const int CheckEveryMs = 30 * 60_000;
        bool _quiet;   // "x is the newest" is logged on the first check only
        readonly ManualResetEvent _stop = new ManualResetEvent(false);   // the mod stops: the check loop ends (a sleeping thread kept the old copy in memory after Insert)
        string _zipSaid;   // a zip-only release: said once
        readonly string _dll;
        volatile string _available, _note;
        volatile string _other;   // the other channel's version (stable on a beta, the beta on stable), for the menu's switch
        volatile bool _busy, _done;
        volatile string _installed;   // what the last download put in place (an update or a switch)
        // Three tracks: stable, the beta (0.9.0-beta.x, the big new things) and the fixes beta (x.y.z-rc.n, the next
        // stable's small fixes early; George, 30 Oct: "a 2nd beta that is just a beta for the small fixes"). An rc
        // follows the rc channel and goes on to the stable it leads to; it's never moved onto the beta.
        static bool IsRc => BuildInfo.Version.Contains("-rc");
        static bool IsBeta => BuildInfo.Version.Contains("-") && !IsRc;
        const string RcChannel = "/download/rc-channel/latest.txt";
        volatile string _rc;   // on stable: the fixes beta to try; on an rc: the stable to go back to

        public Updater(string dllPath, bool check)
        {
            _dll = dllPath;
            try { File.Delete(_dll + ".new"); } catch (Exception) { }   // left by an update that stopped half way
            if (check) new Thread(CheckLoop) { IsBackground = true, Name = "RDR2Coop.UpdateCheck" }.Start();
        }

        /// <summary>The mod stops: no more checks.</summary>
        public void Stop() => _stop.Set();

        // The newer version on offer, or null (none, or already installed).
        public string Available => _done ? null : _available;

        // The co-op menu's row, or null when there's nothing to offer.
        public string MenuLabel => _available == null ? null
            : _done ? (_installed == _available ? $"Updated to {_available}: press Insert to load it" : null)
            : _busy ? $"Updating to {_available}..." : $"Update to {_available} (Enter)";

        // The More tab's switch between stable and the beta (George, 29 Oct: "easier to switch between stable and beta
        // from the in game F10 menu instead of having to swap out files manually"). The mod file only, like an update;
        // settings stay. null when there's nothing to switch to.
        public string SwitchLabel => _other == null ? null
            : _done ? (_installed == _other ? $"Switched to {_other}: press Insert to load it" : null)
            : _busy ? $"Switching to {_other}..." : IsBeta ? $"Switch to stable {_other} (Enter)" : $"Try the beta {_other} (Enter)";

        public string SwitchHelp => IsBeta
            ? "Goes back to the stable version: downloads it, checks it's signed by the mod, and puts it in place of this beta. Press Insert after. Your settings stay. Everyone in a session needs the same version."
            : "Tries the newest beta: downloads it, checks it's signed by the mod, and puts it in place. Press Insert after. Your settings stay; the menu offers stable again from the beta. Some beta features also need the beta's own download (the zip).";

        // A line for the screen, once.
        public string TakeNote() { var n = _note; _note = null; return n; }

        volatile bool _moved;
        /// <summary>True once, after the move to the new loader: the game has to restart.</summary>
        public bool TakeMoved() { bool m = _moved; _moved = false; return m; }

        void CheckLoop()
        {
#if NET48
            if (MoveToLoader()) return;   // the next start is the loader's, with its own update check
#endif
            for (;;)
            {
                Check();
                _quiet = true;
                if (_done || _stop.WaitOne(CheckEveryMs)) return;   // updated (the next start checks again), or the mod stopped
            }
        }

        void Check()
        {
            string v = Offered("/latest/download/latest.txt");
            if (v == null && IsBeta) v = Offered("/download/beta-channel/latest.txt");
            if (v == null && IsRc) v = Offered(RcChannel);
            _rc = IsRc ? Back() : TryRc();   // every track offers the other two (George, 30 Oct: switch to rc versions the same way)
            _other = Other(IsBeta ? "/latest/download/latest.txt" : "/download/beta-channel/latest.txt");
            if (v == null || v == _available || _busy) return;   // nothing new since the last check (already said)
            _available = v;
            _note = $"Co-op {v} is out (you have {BuildInfo.Version}): F10 twice > Update";
            CoopScript.Log($"update check: {v} is out; this is {BuildInfo.Version}");
        }

        // The version that latest.txt names when it's newer than this one, else null.
        string Offered(string path)
        {
            try
            {
                string text = Encoding.UTF8.GetString(Get(Releases + path));
                if (!UpdateCheck.Parse(text, out string v, out _, out _)) { CoopScript.Log($"update check: {path} not understood"); return null; }
                if (!UpdateCheck.Newer(v, BuildInfo.Version)) { if (!_quiet) CoopScript.Log($"update check: {v} is the newest ({path}); this is {BuildInfo.Version}"); return null; }
                if (!NeedsZip(text)) return v;
                if (_zipSaid == v) return null;
                _zipSaid = v;
                _note = $"Co-op {v} is out: it needs the new download (zip) from {Releases}";
                CoopScript.Log($"update check: {v} needs a newer RDR2Coop.Api than the installed one: download the zip");
            }
            catch (Exception e) { CoopScript.Log($"update check ({path}): " + e.Message); }
            return null;
        }

        // The other channel's version when it can be switched to with the mod file alone, else null.
        string Other(string path)
        {
            try
            {
                string text = Encoding.UTF8.GetString(Get(Releases + path));
                if (!UpdateCheck.Parse(text, out string v, out _, out _) || v == BuildInfo.Version || NeedsZip(text)) return null;
                if (IsBeta == v.Contains("-")) return null;   // the same channel: that's an update, not a switch
                return v;
            }
            catch (Exception e) { CoopScript.Log($"switch check ({path}): " + e.Message); return null; }
        }

        // On stable: the fixes beta when it leads to a newer stable than this one (an rc of a released stable isn't offered).
        // On the beta: the fixes beta whenever there is one (its numbers are below the beta's).
        string TryRc()
        {
            try
            {
                string text = Encoding.UTF8.GetString(Get(Releases + RcChannel));
                if (!UpdateCheck.Parse(text, out string v, out _, out _) || !v.Contains("-rc") || NeedsZip(text)) return null;
                return IsBeta || UpdateCheck.Newer(v.Split('-')[0], BuildInfo.Version) ? v : null;
            }
            catch (Exception) { return null; }   // no rc channel yet: nothing to offer
        }

        // On an rc: the stable to switch back to.
        string Back()
        {
            try
            {
                string text = Encoding.UTF8.GetString(Get(Releases + "/latest/download/latest.txt"));
                return UpdateCheck.Parse(text, out string v, out _, out _) && !v.Contains("-") && !NeedsZip(text) ? v : null;
            }
            catch (Exception e) { CoopScript.Log("switch check (stable): " + e.Message); return null; }
        }

        public string RcLabel => _rc == null ? null
            : _done ? (_installed == _rc ? $"Switched to {_rc}: press Insert to load it" : null)
            : _busy ? $"Switching to {_rc}..." : IsRc ? $"Switch to stable {_rc} (Enter)" : IsBeta ? $"Switch to the fixes beta {_rc} (Enter)" : $"Try the fixes beta {_rc} (Enter)";

        public string RcHelp => IsRc
            ? "Goes back to the stable version, the same way as an update: signed by the mod, your settings stay. Press Insert after."
            : "The fixes beta: the stable version plus the next version's small fixes, early, without the beta's big new things. It updates itself to the next stable. Signed by the mod, your settings stay. Press Insert after.";

        /// <summary>The menu's switch between stable and the fixes beta.</summary>
        public void SwitchRc() => Install(_rc, "switch");

        // Whether the release wants a newer RDR2Coop.Api than the one installed (the mod alone can't be swapped in then).
        static bool NeedsZip(string latest)
        {
#if NET48
            return false;   // ScriptHookRDR2DotNet's API isn't ours to update
#else
            return Version.TryParse(UpdateCheck.Api(latest) ?? "", out Version need) && need > typeof(RDR2.Script).Assembly.GetName().Version;
#endif
        }

        // The menu's Update: download, check the signature, swap the file (all off the game's thread).
        public void Begin() => Install(_available, "update");

        /// <summary>The menu's switch to the other channel.</summary>
        public void Switch() => Install(_other, "switch");

        void Install(string v, string what)
        {
            if (v == null || _busy || _done) return;
            _busy = true;
            new Thread(() =>
            {
                try
                {
                    byte[] dll = Get($"{Releases}/download/v{v}/{Asset}"), sig = Get($"{Releases}/download/v{v}/{Asset}.sig");
                    if (!UpdateCheck.Verify(dll, sig, UpdateCheck.ReleaseKey))
                    {
                        _note = "Co-op update refused: the download isn't signed with the mod's release key";
                        CoopScript.Log($"update: {v} refused, signature doesn't match the release key");
                        return;
                    }
#if !NET48
                    if (!ReferencesApi(dll))   // both builds are signed with the same key; Core would silently skip the SHDN one
                    {
                        _note = "Co-op update refused: the download isn't the build for this loader";
                        CoopScript.Log($"update: {v} refused, {Asset} doesn't reference RDR2Coop.Api (the ScriptHookRDR2DotNet build uploaded under the wrong name?)");
                        return;
                    }
#endif
                    File.WriteAllBytes(_dll + ".new", dll);
                    File.Copy(_dll + ".new", _dll, true);
                    File.Delete(_dll + ".new");
                    _installed = v;
                    _done = true;
                    _note = $"Co-op {(what == "switch" ? "switched" : "updated")} to {v}: press Insert to load it (or restart the game)";
                    CoopScript.Log($"{what}: installed {v} ({dll.Length} bytes, signature good)");
                }
                catch (Exception e) { _note = $"Co-op {what} failed: " + e.Message; CoopScript.Log($"{what} failed: " + e); }
                finally { _busy = false; }
            }) { IsBackground = true, Name = "RDR2Coop.Update" }.Start();
        }

#if NET48
        // The new loader for every version (George, 31 Oct: "it should be default now that the new loader is used for all
        // versions"): this version's loader comes from the release's own zip, signed like the dll, unpacked as the zip lays
        // it out (RDR2Coop.asi + the RDR2Coop folder; the player's scripts\RDR2Coop.ini stays). This dll goes to
        // RDR2Coop\backup: the loader won't start while scripts\RDR2Coop.dll and ScriptHookRDRDotNet.asi are both there.
        // ScriptHookRDRDotNet.asi is switched off too (renamed .off; George, 31 Oct: "I don't care about other mod support
        // yet. Make it so it moves it out the way"): the loader runs what's in scripts\ itself then, enRage with its co-op
        // link. The asi only loads when the game starts: true = restart the game.

        bool MoveToLoader()
        {
            try
            {
                if (!File.Exists(_dll)) return false;
                string scripts = Path.GetDirectoryName(Path.GetFullPath(_dll)), game = Path.GetDirectoryName(scripts);
                string asi = Path.Combine(game, "RDR2Coop.asi"), folder = Path.Combine(game, "RDR2Coop");
                if (!File.Exists(asi) || !File.Exists(Path.Combine(folder, "RDR2Coop.dll")))   // else a loader is there already, refusing to start beside us
                {
                    string v = BuildInfo.Version, url = $"{Releases}/download/v{v}/RDR2-Coop-{v}.zip";
                    byte[] sig = Get(url + ".sig"), zip = Get(url, MaxZipBytes);   // the signature first: older releases have none
                    if (!UpdateCheck.Verify(zip, sig, UpdateCheck.ReleaseKey)) { CoopScript.Log($"new loader: {v}'s zip refused, signature doesn't match the release key"); return false; }
                    Unpack(zip, game);
                }
                string backup = Path.Combine(folder, "backup"), old = Path.Combine(backup, "RDR2Coop.dll.scripthookdotnet-" + BuildInfo.Version);
                Directory.CreateDirectory(backup);
                File.Delete(old);
                File.Move(_dll, old);
                string shdn = Path.Combine(game, "ScriptHookRDRDotNet.asi"), shdnSaid = "no ScriptHookRDRDotNet.asi";
                if (File.Exists(shdn))
                    try { File.Delete(shdn + ".off"); File.Move(shdn, shdn + ".off"); shdnSaid = "ScriptHookRDRDotNet.asi switched off (.off)"; }
                    catch (Exception e) { shdnSaid = "ScriptHookRDRDotNet.asi couldn't be switched off (" + e.Message + ")"; }
                CoopScript.Log($"new loader: {BuildInfo.Version} in place (RDR2Coop.asi + RDR2Coop folder), this ScriptHookRDR2DotNet copy moved to {old}; {shdnSaid}; restart the game");
                _moved = true;
                return true;
            }
            catch (Exception e) { CoopScript.Log("new loader: not moved yet (" + e.Message + "); the next start tries again"); return false; }
        }

        // The zip's RDR2-Coop/RDR2Coop/... and RDR2-Coop/RDR2Coop.asi into the game folder; the asi last, so the loader never
        // starts beside a half-written folder.
        static void Unpack(byte[] zip, string game)
        {
            const string Top = "RDR2-Coop/";
            string root = Path.GetFullPath(game).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            using (var z = new System.IO.Compression.ZipArchive(new MemoryStream(zip)))
            {
                var asi = z.GetEntry(Top + "RDR2Coop.asi") ?? throw new IOException("no RDR2Coop.asi in the zip");
                foreach (var e in z.Entries)
                    if (e.FullName.StartsWith(Top + "RDR2Coop/", StringComparison.Ordinal) && !e.FullName.EndsWith("/")) Extract(e, root, e.FullName.Substring(Top.Length));
                // The game loaded an older RDR2Coop.asi at start (it refused to run beside this copy): a loaded file can't be
                // overwritten but can be renamed (asphyxia's game, 31 Oct). Nothing loads a .asi.old-*.
                string loaded = Path.Combine(root, "RDR2Coop.asi");
                if (File.Exists(loaded)) File.Move(loaded, loaded + ".old-" + DateTime.UtcNow.Ticks);
                Extract(asi, root, "RDR2Coop.asi");
            }
        }

        static void Extract(System.IO.Compression.ZipArchiveEntry e, string root, string rel)
        {
            string to = Path.GetFullPath(Path.Combine(root, rel));
            if (!to.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("a path outside the game folder in the zip: " + e.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            using (var s = e.Open())
            using (var f = File.Create(to + ".new")) s.CopyTo(f);
            File.Copy(to + ".new", to, true);
            File.Delete(to + ".new");
        }
#endif

#if !NET48
        // Whether the assembly references RDR2Coop.Api (read from its metadata, nothing is loaded): the same test Core applies before loading a mod.
        static bool ReferencesApi(byte[] dll)
        {
            using (var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(dll)))
            {
                var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
                foreach (var h in md.AssemblyReferences)
                    if (md.GetString(md.GetAssemblyReference(h).Name) == "RDR2Coop.Api") return true;
            }
            return false;
        }
#endif

        static byte[] Get(string url, int max = MaxBytes)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = req.ReadWriteTimeout = 30000;
            req.UserAgent = "RDR2Coop/" + BuildInfo.Version;
            using (var resp = req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[65536];
                for (int n; (n = s.Read(buf, 0, buf.Length)) > 0;)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > max) throw new IOException("download too large");
                }
                return ms.ToArray();
            }
        }
    }
}
