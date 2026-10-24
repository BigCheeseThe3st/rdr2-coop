// The update notice and one-press update (George, 24 Oct: "notice plus one-press
// update"). At start a background check reads the newest release's latest.txt from
// GitHub; when it is newer the player is told once, and the co-op menu (F10 twice)
// offers "Update to x". Updating downloads that release's RDR2Coop.dll and its
// signature, checks the signature (UpdateCheck.Verify: only the release key passes)
// and swaps the file: ScriptHookRDR2DotNet runs a copy of it, so the file isn't
// locked, and Insert (or the next start) loads the new one. Settings are untouched.
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
            try
            {
                string text = Encoding.UTF8.GetString(Get(Releases + "/latest/download/latest.txt"));
                if (!UpdateCheck.Parse(text, out string v, out _, out _)) { CoopScript.Log("update check: latest.txt not understood"); return; }
                if (!UpdateCheck.Newer(v, BuildInfo.Version)) { CoopScript.Log($"update check: {v} is the newest; this is {BuildInfo.Version}"); return; }
                _available = v;
                _note = $"Co-op {v} is out (you have {BuildInfo.Version}): F10 twice > Update";
                CoopScript.Log($"update check: {v} is out; this is {BuildInfo.Version}");
            }
            catch (Exception e) { CoopScript.Log("update check: " + e.Message); }
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
                    byte[] dll = Get($"{Releases}/download/v{v}/RDR2Coop.dll"), sig = Get($"{Releases}/download/v{v}/RDR2Coop.dll.sig");
                    if (!UpdateCheck.Verify(dll, sig, UpdateCheck.ReleaseKey))
                    {
                        _note = "Co-op update refused: the download isn't signed with the mod's release key";
                        CoopScript.Log($"update: {v} refused, signature doesn't match the release key");
                        return;
                    }
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
