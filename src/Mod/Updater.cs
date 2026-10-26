// The update notice and one-press update (the author, 24 Oct: "notice plus one-press
// update"). At start a background check reads the newest release's latest.txt from
// GitHub; when it is newer the player is told once, and the co-op menu (F10 twice)
// offers "Update to x". Updating downloads that release's RDR2Coop.dll and its
// signature, checks the signature (UpdateCheck.Verify: only the release key passes)
// and swaps the file: ScriptHookRDR2DotNet runs a copy of it, so the file isn't
// locked, and Insert (or the next start) loads the new one. Settings are untouched.
// A beta also follows the beta channel: the release tagged beta-channel, whose
// latest.txt names the newest beta (GitHub's "latest" skips pre-releases).
// The one-press update swaps the mod only: when latest.txt says the release was built
// against a newer RDR2Coop.Api than the installed one (the loader zip), the player
// is told to download the zip instead (the .NET 9 build only).
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
        readonly string _dll;
        volatile string _available, _note;
        volatile bool _busy, _done;

        public Updater(string dllPath, bool check)
        {
            _dll = dllPath;
            try { File.Delete(_dll + ".new"); } catch (Exception) { }   // left by an update that stopped half way
            if (check) new Thread(Check) { IsBackground = true, Name = "RDR2Coop.UpdateCheck" }.Start();
        }

        // The newer version on offer, or null (none, or already installed).
        public string Available => _done ? null : _available;

        // The co-op menu's row, or null when there's nothing to offer.
        public string MenuLabel => _available == null ? null
            : _done ? $"Updated to {_available}: press Insert to load it"
            : _busy ? $"Updating to {_available}..." : $"Update to {_available} (Enter)";

        // A line for the screen, once.
        public string TakeNote() { var n = _note; _note = null; return n; }

        void Check()
        {
            string v = Offered("/latest/download/latest.txt");
            if (v == null && BuildInfo.Version.Contains("-")) v = Offered("/download/beta-channel/latest.txt");
            if (v == null) return;
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
                if (!UpdateCheck.Newer(v, BuildInfo.Version)) { CoopScript.Log($"update check: {v} is the newest ({path}); this is {BuildInfo.Version}"); return null; }
                if (!NeedsZip(text)) return v;
                _note = $"Co-op {v} is out: it needs the new download (zip) from {Releases}";
                CoopScript.Log($"update check: {v} needs a newer RDR2Coop.Api than the installed one: download the zip");
            }
            catch (Exception e) { CoopScript.Log($"update check ({path}): " + e.Message); }
            return null;
        }

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
        public void Begin()
        {
            string v = _available;
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
                    _done = true;
                    _note = $"Co-op updated to {v}: press Insert to load it (or restart the game)";
                    CoopScript.Log($"update: installed {v} ({dll.Length} bytes, signature good)");
                }
                catch (Exception e) { _note = "Co-op update failed: " + e.Message; CoopScript.Log("update failed: " + e); }
                finally { _busy = false; }
            }) { IsBackground = true, Name = "RDR2Coop.Update" }.Start();
        }

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

        static byte[] Get(string url)
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
                    if (ms.Length > MaxBytes) throw new IOException("download too large");
                }
                return ms.ToArray();
            }
        }
    }
}
