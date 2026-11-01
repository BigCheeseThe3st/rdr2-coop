// The game side of sharing logs with the author (rules and scrubbing: Net/LogShare.cs). Every build asks
// once ([Testing] SendLogs= empty): the question stays on screen for a while, Y or N is written to the ini.
// With a Yes, the tail of scripts\RDR2Coop.log (and the loader's and host's logs when they exist) goes to
// the log store a minute after the start, every 10 minutes, when the tick throws, and when the mod stops
// (reload or quitting). Every step is logged, and nothing is sent without the Yes. A Yes typed into the ini by hand
// gets its install id here (the log store refused uploads without one before 0.9.0-beta.8), and F10 shows whether
// the logs actually go out (Status).
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using RDR2Coop.Net;
using Keys = System.Windows.Forms.Keys;

namespace RDR2Coop.Mod
{
    sealed class LogSender
    {
        const string Store = "https://rdr2coop-logs.beese.workers.dev/v1/upload";
        readonly string _iniPath, _scripts, _name, _version;
        LogShare.Consent _consent;
        string _install;
        long _askedAt = -1, _lastSay, _lastSentMs = -1, _startedMs;
        int _sending;
        volatile string _lastResult;   // null: nothing tried yet; "": the last upload went through; else why it failed
        long _lastResultMs;

        public bool Asking => _askedAt >= 0;

        // The co-op menu row (betas only): the same switch as the question, for later.
        public string MenuLabel => LogShare.Label(_version) + (_consent == LogShare.Consent.Yes ? ": sending to the developer (switch off)" : ": not sent (switch on)");
        public void Toggle() => Answer(_consent == LogShare.Consent.Yes ? LogShare.Consent.No : LogShare.Consent.Yes);

        readonly Func<long> _clockMs;

        public LogSender(string iniPath, Ini ini, string name, string version, long now, Func<long> clockMs)
        {
            _clockMs = clockMs;
            _iniPath = iniPath;
            _scripts = Path.GetDirectoryName(iniPath) ?? "scripts";
            _name = name; _version = version; _startedMs = now;
            _consent = LogShare.Parse(ini.Get("Testing", "SendLogs", ""));
            _install = ini.Get("Testing", "LogsInstall", "").Trim();
            if (_consent == LogShare.Consent.Unasked) { _askedAt = now; CoopScript.Log("beta logs: asking for consent (Y/N on screen)"); }
            else CoopScript.Log($"beta logs: consent {_consent} from the settings file");
            if (_consent == LogShare.Consent.Yes && !LogShare.ValidInstall(_install))
            {
                _install = LogShare.NewInstall();
                try { Ini.Set(_iniPath, "Testing", new Dictionary<string, string> { { "LogsInstall", _install } }); }
                catch (Exception e) { CoopScript.Log("beta logs: couldn't save the install id: " + e.Message); }
                CoopScript.Log("beta logs: SendLogs=Yes had no install id (typed into the ini by hand?): made one, so the uploads are accepted");
            }
        }

        /// <summary>For the F10 status line: whether the logs actually go out (null on a stable build).</summary>
        public string Status(long now)
        {
            string what = LogShare.Label(_version).ToLowerInvariant();
            if (_consent != LogShare.Consent.Yes) return what + ": off (F10 twice to switch on)";
            string r = _lastResult;
            return r == null ? what + ": on, first send soon"
                 : r.Length == 0 ? $"{what}: on, last sent {Math.Max(0, (now - _lastResultMs) / 60000)} min ago"
                 : $"{what}: on, but sending failed: {r}";
        }

        // Y or N while the question is up. True when the key was ours.
        public bool OnKey(Keys key)
        {
            if (!Asking || (key != Keys.Y && key != Keys.N)) return false;
            Answer(key == Keys.Y ? LogShare.Consent.Yes : LogShare.Consent.No);
            return true;
        }

        void Answer(LogShare.Consent c)
        {
            _askedAt = -1;
            _consent = c;
            if (c == LogShare.Consent.Yes && !LogShare.ValidInstall(_install)) _install = LogShare.NewInstall();
            try { Ini.Set(_iniPath, "Testing", new Dictionary<string, string> { { "SendLogs", c == LogShare.Consent.Yes ? "Yes" : "No" }, { "LogsInstall", _install } }); }
            catch (Exception e) { CoopScript.Log("beta logs: couldn't save the answer: " + e.Message); }
            CoopScript.Log($"beta logs: consent answered {c}");
            CoopScript.Say(c == LogShare.Consent.Yes ? "Co-op: thanks, your co-op logs go to the developer while you play (SendLogs=No in RDR2Coop.ini stops it)"
                                                      : "Co-op: understood, nothing is sent (SendLogs=Yes in RDR2Coop.ini turns it on)");
        }

        public void Tick(long now)
        {
            if (Asking)
            {
                if (now - _askedAt > LogShare.AskForMs) { _askedAt = -1; CoopScript.Log("beta logs: no answer, asking again next time"); return; }
                if (now - _lastSay > 3000) { _lastSay = now; CoopScript.Say(LogShare.IsBeta(_version) ? "Co-op " + LogShare.Question : LogShare.QuestionFor(_version)); }   // other subtitles replace it
                return;
            }
            if (_consent == LogShare.Consent.Yes && LogShare.Due(now, _lastSentMs, _startedMs, LogShare.EveryFor(_version))) { _lastSentMs = now; Send("session"); }
        }

        /// <summary>The tick threw: the log goes up now (once a minute at most).</summary>
        public void OnError(long now)
        {
            if (_consent != LogShare.Consent.Yes || now - _lastSentMs < 60_000) return;
            _lastSentMs = now;
            Send("error");
        }

        /// <summary>The mod stops (reload, quit): the log goes up, on this thread so it finishes.</summary>
        public void OnStop()
        {
            if (_consent != LogShare.Consent.Yes) return;
            Send("stop", wait: true);
        }

        void Send(string why, bool wait = false)
        {
            if (Interlocked.CompareExchange(ref _sending, 1, 0) != 0) return;   // one at a time
            var work = new ThreadStart(() =>
            {
                try
                {
                    foreach (var (file, kind) in new[] { ("RDR2Coop.log", "mod"), ("RDR2Coop.Host.log", "host"), ("RDR2Coop.Core.log", "core") })
                    {
                        string path = Path.Combine(_scripts, file);
                        if (!File.Exists(path)) continue;
                        string text;
                        using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var r = new StreamReader(f, Encoding.UTF8)) text = r.ReadToEnd();
                        byte[] body = Encoding.UTF8.GetBytes(LogShare.Scrub(LogShare.Tail(text)));
                        var req = (HttpWebRequest)WebRequest.Create(Store);
                        req.Method = "POST"; req.Timeout = req.ReadWriteTimeout = 20000;
                        req.UserAgent = "RDR2Coop/" + _version; req.ContentType = "text/plain; charset=utf-8";
                        req.Headers["x-coop-name"] = Ascii(_name); req.Headers["x-coop-version"] = _version; req.Headers["x-coop-install"] = _install;
                        req.Headers["x-coop-kind"] = kind; req.Headers["x-coop-build"] = Build; req.Headers["x-coop-why"] = why;
                        req.ContentLength = body.Length;
                        using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                        using (var resp = (HttpWebResponse)req.GetResponse()) CoopScript.Log($"beta logs: sent {file} ({body.Length} bytes, {why}): {(int)resp.StatusCode}");
                        _lastResultMs = _clockMs(); _lastResult = "";
                    }
                }
                catch (Exception e) { CoopScript.Log("beta logs: upload failed: " + e.Message); _lastResultMs = _clockMs(); _lastResult = e.Message; }
                finally { _sending = 0; }
            });
            if (wait) work();
            else new Thread(work) { IsBackground = true, Name = "RDR2Coop.LogSender" }.Start();
        }

#if NET48
        public const string Build = "net48";
#else
        public const string Build = "net9";
#endif

        static string Ascii(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") if (c >= 32 && c < 127) sb.Append(c);
            return sb.Length == 0 ? "player" : sb.ToString();
        }
    }
}
