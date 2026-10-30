// The game side of the script recordings (0.9.0-beta.8.4; the tape's rules and lines: Net/MissionTape.cs). With beta
// logs on, every game script that starts after the mod does (story missions, random encounters, ambient events, shops:
// the scripts already running at the start are the game's always-on ones and are skipped) is recorded while it runs:
// its memory is read every half second (the .NET 9 build's loader, Mod/ScriptMemory.cs) and what changed goes to
// scripts\RDR2Coop.missions.log, which goes up with the other beta logs. Each line starts with "@<script hash>" (story
// missions also give their name on the start line). Values that are people, vehicles, objects or map markers are
// labelled (model, relationship to us, fighting, blip, dead), and when one of those people dies the line says who
// killed them. The mod's own mission events are copied in. Read-only: nothing is written into the game.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RDR2;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class MissionRecorder
    {
        public const string FileName = "RDR2Coop.missions.log";
        const int EveryMs = 500, ScanEveryMs = 1000, MaxSlots = 32768, Piece = 16384, LabelsPerRead = 200, MaxWatched = 512,
                  PositionEveryMs = 5000, MaxActive = 4, MaxStartsPerScript = 5;
        static readonly string PathOf = Path.Combine("scripts", FileName);
        static readonly object PendingLock = new object();
        static readonly List<string> Pending = new List<string>();   // the mod's own log lines while something is recorded
        static volatile bool _recording;

        sealed class Rec
        {
            public string Name; public uint Hash; public MissionTape Tape;
            public readonly Dictionary<int, bool> Watched = new Dictionary<int, bool>();   // entity handle -> dead when last looked at
            public long LastReadMs;
        }

        readonly bool _on, _all;   // _all: [Testing] RecordAllScripts=Yes: every new script, not only story missions
        readonly long _maxBytes;   // MissionTape.MaxBytes, or no cap with [Testing] KeepRecordings=Yes
        readonly StringBuilder _out = new StringBuilder();
        readonly ulong[] _buf = new ulong[MaxSlots];
        readonly List<Rec> _active = new List<Rec>();
        readonly Dictionary<uint, int> _starts = new Dictionary<uint, int>();
        readonly Dictionary<uint, string> _names = new Dictionary<uint, string>();
        HashSet<uint> _baseline;
        readonly uint[] _ids = new uint[1024], _hashes = new uint[1024];
        long _bytes, _lastScanMs, _lastPosMs;
        bool _saidOff, _full;

        /// <summary>keep ([Testing] KeepRecordings=Yes, for the author's own game): no size cap, and the last recording is
        /// moved to scripts\RDR2Coop.recordings\ instead of being cleared (uploads still send the newest 1 MB).</summary>
        public MissionRecorder(bool on, bool keep, bool all)
        {
            _on = on;
            _all = all;
            _maxBytes = keep ? long.MaxValue : MissionTape.MaxBytes;
            if (on && keep)
                try
                {
                    var old = new FileInfo(PathOf);
                    if (old.Exists && old.Length > 0)
                    {
                        string dir = Path.Combine("scripts", "RDR2Coop.recordings");
                        Directory.CreateDirectory(dir);
                        File.Move(PathOf, Path.Combine(dir, old.LastWriteTime.ToString("yyyyMMdd-HHmmss") + ".log"));
                    }
                }
                catch (Exception e) { CoopScript.Log("script recording: couldn't keep the last recording: " + e.Message); }
            _recording = false;
            lock (PendingLock) Pending.Clear();
            foreach (var n in MissionProbe.Names) _names[MISC.GET_HASH_KEY(n)] = n;
            try { if (on) File.WriteAllText(PathOf, ""); else File.Delete(PathOf); } catch (Exception) { }   // one recording per mod start: the last one went up when the mod stopped
        }

        /// <summary>A line of the mod's own log: mission events go into the recording too.</summary>
        public static void OnLog(string line)
        {
            if (!_recording || line.StartsWith("mission recording") || line.StartsWith("script recording")) return;
            if (!(line.StartsWith("story ") || line.StartsWith("mission") || line.StartsWith("hit from") || line.StartsWith("down:")
                  || line.StartsWith("up:") || line.StartsWith("copy ") || line.StartsWith("party"))) return;
            lock (PendingLock) if (Pending.Count < 200) Pending.Add(line);
        }

        void Write(string line)
        {
            if (_full) return;
            _bytes += line.Length + 1;
            _out.Append(line).Append('\n');
            if (_bytes < _maxBytes) return;
            _full = true;
            _out.Append($"F full at {_bytes} bytes: no more recording until the mod starts again\n");
            CoopScript.Log($"script recording: the recording is full ({_maxBytes / 1024} KB) until the mod starts again");
        }

        public void Tick(long now, Func<int, string> partnerOf)
        {
            if (!_on || _full) { Flush(now); return; }
            if (now - _lastScanMs >= ScanEveryMs) { _lastScanMs = now; Scan(now); }
            foreach (var r in _active.ToList())
                if (now - r.LastReadMs >= EveryMs) { r.LastReadMs = now; Read(now, r, partnerOf); }
            if (_active.Count > 0 && now - _lastPosMs >= PositionEveryMs)
            {
                _lastPosMs = now;
                var p = Game.Player.Character.Position;
                Write($"P {now} {p.X:0.0} {p.Y:0.0} {p.Z:0.0}");
            }
            _recording = _active.Count > 0;
            Flush(now);
        }

        bool Ready()
        {
            if (ScriptMemory.Check(ScriptMemory.GameDir())) return true;
            if (!_saidOff) { _saidOff = true; CoopScript.Log("script recording: off (needs the .NET 9 build's script memory; see the script memory line above)"); }
            return false;
        }

        // New scripts since the mod started: recorded while they run (a few at a time).
        void Scan(long now)
        {
            if (!Ready()) return;
            int n = ScriptMemory.Threads(_ids, _hashes);
            if (n < 0) return;
            var running = new HashSet<uint>();
            for (int i = 0; i < n; i++) running.Add(_hashes[i]);
            if (_baseline == null)
            {
                _baseline = running;
                CoopScript.Log($"script recording: on; {running.Count} scripts already running are the game's own and skipped");
                return;
            }
            foreach (uint h in running)
            {
                if (_baseline.Contains(h) || _active.Any(r => r.Hash == h)) continue;
                if (!_all && !_names.ContainsKey(h)) continue;   // story missions only, unless RecordAllScripts=Yes
                if (_active.Count >= MaxActive) break;
                _starts.TryGetValue(h, out int k);
                if (k >= MaxStartsPerScript) continue;   // a script that keeps starting and stopping: enough of it
                _starts[h] = k + 1;
                Begin(now, h);
            }
        }

        // How many slots to read: [0, sp) when the stack pointer is plausible, else the whole stack. 0 not running, < 0 an error.
        static int Count(uint hash, out int size, out int sp)
        {
            int r = ScriptMemory.Info(hash, out size, out sp);
            if (r <= 0) return r;
            return Math.Min(sp > 0 && sp <= size ? sp : size, MaxSlots);
        }

        bool ReadAll(uint hash, int count)
        {
            for (int at = 0; at < count; at += Piece)
                if (ScriptMemory.Read(hash, at, _buf, at, Math.Min(Piece, count - at)) <= 0) return false;
            return true;
        }

        void Begin(long now, uint hash)
        {
            int count = Count(hash, out int size, out int sp);
            if (count <= 0 || !ReadAll(hash, count)) return;   // gone already, or a loader older than 0.9.0-beta.8.4
            string name = _names.TryGetValue(hash, out var known) ? known : "?";
            string tag = $"@{hash:x8} ";
            var r = new Rec { Name = name, Hash = hash, LastReadMs = now, Tape = new MissionTape(l => Write(tag + l), _maxBytes) };
            _active.Add(r);
            r.Tape.Start(now, name, hash, size, sp, _buf, count);
            var nonZero = new List<int>();
            for (int i = 0; i < count; i++) if (_buf[i] != 0) nonZero.Add(i);
            if (name != "?") Label(now, r, nonZero, null, 5 * LabelsPerRead);   // other scripts: memory only, no natives on their values
            CoopScript.Log($"script recording: {(name == "?" ? $"script {hash:X8}" : name)} ({count} of {size} slots)");
        }

        void Read(long now, Rec r, Func<int, string> partnerOf)
        {
            int count = Count(r.Hash, out _, out _);
            if (count <= 0 || !ReadAll(r.Hash, count)) { End(now, r, count == 0 ? "it stopped" : "reading failed (it was stopping)"); return; }
            var changed = r.Tape.Changes(now, _buf, count);
            if (r.Name == "?") return;   // a script that isn't a story mission: its values aren't asked about (27 Oct: a crash 1 min into recording them)
            Label(now, r, changed, partnerOf, LabelsPerRead);
            Deaths(now, r, partnerOf);
        }

        void End(long now, Rec r, string why)
        {
            r.Tape.Line($"M {now} end {r.Name} ({why})");
            _active.Remove(r);
            if (r.Name != "?") CoopScript.Log($"script recording: {r.Name} done ({why}), {_bytes / 1024} KB recorded since the mod started");
        }

        // Changed (or, at the start, non-zero) values that are entities or blips: what they are, once per entity.
        void Label(long now, Rec r, List<int> slots, Func<int, string> partnerOf, int max)
        {
            int me = PLAYER.PLAYER_PED_ID(), n = 0;
            foreach (int i in slots)
            {
                ulong v = _buf[i];
                if (v < 256 || v >= 1UL << 28) continue;   // too small or too big to be a handle
                if (++n > max) break;
                int h = (int)v;
                if (ENTITY.DOES_ENTITY_EXIST(h))
                {
                    if (r.Watched.ContainsKey(h)) { r.Tape.Line($"E {now} {i} {h}"); continue; }
                    if (r.Watched.Count < MaxWatched) r.Watched[h] = ENTITY.IS_ENTITY_DEAD(h);
                    r.Tape.Line($"E {now} {i} {h} {Describe(h, me, partnerOf)}");
                }
                // No DOES_BLIP_EXIST on unknown values: on arbitrary numbers it crashed the game (23 Oct, MissionAssist's
                // ObjectiveScan; and likely the recorder 27 Oct 20:06).
            }
        }

        static string Describe(int h, int me, Func<int, string> partnerOf)
        {
            int type = ENTITY.GET_ENTITY_TYPE(h);   // 1 ped, 2 vehicle, 3 object
            var sb = new StringBuilder(type == 1 ? "ped" : type == 2 ? "vehicle" : type == 3 ? "object" : "entity");
            sb.Append(' ').Append(ENTITY.GET_ENTITY_MODEL(h).ToString("x8"));
            if (type == 1)
            {
                if (h == me) sb.Append(" me");
                else if (partnerOf?.Invoke(h) != null) sb.Append(" partner");
                sb.Append(" rel ").Append(PED.GET_RELATIONSHIP_BETWEEN_PEDS(h, me));
                if (PED.IS_PED_IN_COMBAT(h, me)) sb.Append(" fighting");
                if (!PED.IS_PED_HUMAN(h)) sb.Append(" animal");
            }
            if (ENTITY.IS_ENTITY_A_MISSION_ENTITY(h)) sb.Append(" mission");
            if (MAP._DOES_ENTITY_HAVE_BLIP(h)) sb.Append(" blip");
            if (ENTITY.IS_ENTITY_DEAD(h)) sb.Append(" dead");
            return sb.ToString();
        }

        // The labelled entities: who died (and by whom), which are gone.
        void Deaths(long now, Rec r, Func<int, string> partnerOf)
        {
            int me = PLAYER.PLAYER_PED_ID();
            foreach (int h in r.Watched.Keys.ToList())
            {
                if (!ENTITY.DOES_ENTITY_EXIST(h)) { r.Tape.Line($"X {now} {h}"); r.Watched.Remove(h); continue; }
                bool dead = ENTITY.IS_ENTITY_DEAD(h);
                if (dead && !r.Watched[h])
                {
                    int src = ENTITY.GET_ENTITY_TYPE(h) == 1 ? PED.GET_PED_SOURCE_OF_DEATH(h) : 0;
                    string by = src == 0 ? "-" : src == me ? "me" : partnerOf?.Invoke(src) != null ? "partner"
                              : ENTITY.DOES_ENTITY_EXIST(src) ? ENTITY.GET_ENTITY_MODEL(src).ToString("x8") : "?";
                    r.Tape.Line($"K {now} {h} by {by}");
                }
                r.Watched[h] = dead;
            }
        }

        void Flush(long now)
        {
            List<string> lines;
            lock (PendingLock) { lines = new List<string>(Pending); Pending.Clear(); }
            if (_active.Count > 0) foreach (var l in lines) Write($"V {now} {l}");
            if (_out.Length == 0) return;
            try { File.AppendAllText(PathOf, _out.ToString()); } catch (Exception) { }
            _out.Clear();
        }
    }
}
