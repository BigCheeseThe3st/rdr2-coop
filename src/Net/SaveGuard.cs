// Keeps a guest's own saves safe while they play in the host's world (0.9.0-beta.9, Mod/HostWorld.cs). The game keeps
// autosaving (and the guest may sleep or save by hand) while it runs the host's story, which would write the host's
// world over the guest's own progress (seen 28 Oct: the autosave held the injected world 8 minutes later). So, when a
// guest joins, every save file is copied to a backup folder and a marker is written; while joined, any save file the
// game writes or changes is moved into the backup's host-world folder and the guest's original is put back; on leaving
// (or the next start after a crash, from the marker) the same check runs once more and the marker goes. Never deletes a
// save: anything moved aside stays in the backup folder. Pure file work, so it's tested on a temporary folder.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RDR2Coop.Net
{
    public sealed class SaveGuard
    {
        public const string Marker = "hostworld-joined.txt";
        public readonly string Profile, Backup;   // the game's profile folder; this join's backup folder
        readonly Dictionary<string, (long size, DateTime time)> _seen = new Dictionary<string, (long, DateTime)>();
        int _moved;

        SaveGuard(string profile, string backup) { Profile = profile; Backup = backup; }

        static IEnumerable<string> Saves(string dir) =>
            Directory.Exists(dir) ? Directory.GetFiles(dir).Where(f => Path.GetFileName(f).StartsWith("SRDR3", StringComparison.OrdinalIgnoreCase)) : Enumerable.Empty<string>();

        /// <summary>Backs up every save in the profile and writes the marker. stamp names the backup folder.</summary>
        public static SaveGuard Begin(string profile, string backupRoot, string stamp)
        {
            string backup = Path.Combine(backupRoot, "saves-" + stamp);
            Directory.CreateDirectory(backup);
            foreach (var f in Saves(profile)) File.Copy(f, Path.Combine(backup, Path.GetFileName(f)), true);
            File.WriteAllLines(Path.Combine(backupRoot, Marker), new[] { profile, backup });
            var g = new SaveGuard(profile, backup);
            g.Remember();
            return g;
        }

        /// <summary>A join that never ended (a crash, the mod stopped): the guard to finish it with, or null.</summary>
        public static SaveGuard Pending(string backupRoot)
        {
            string m = Path.Combine(backupRoot, Marker);
            if (!File.Exists(m)) return null;
            var lines = File.ReadAllLines(m);
            if (lines.Length < 2 || !Directory.Exists(lines[1])) return null;
            var g = new SaveGuard(lines[0], lines[1]);
            return g;
        }

        void Remember()
        {
            _seen.Clear();
            foreach (var f in Saves(Profile)) { var i = new FileInfo(f); _seen[i.Name] = (i.Length, i.LastWriteTimeUtc); }
        }

        /// <summary>Puts the guest's own saves back wherever the game wrote: what it did, one line per file.</summary>
        public List<string> Check(bool force = false)
        {
            var done = new List<string>();
            var now = Saves(Profile).Select(f => new FileInfo(f)).ToList();
            var seenNow = now.ToDictionary(i => i.Name, i => (i.Length, i.LastWriteTimeUtc));   // as found before this scan: a write during it shows next time
            bool changed = force || now.Count != _seen.Count || now.Any(i => !_seen.TryGetValue(i.Name, out var s) || s.size != i.Length || s.time != i.LastWriteTimeUtc);
            if (!changed) return done;
            string aside = Path.Combine(Backup, "host-world");
            foreach (var i in now)
            {
                string mine = Path.Combine(Backup, i.Name);
                if (File.Exists(mine) && Same(mine, i.FullName)) continue;
                Directory.CreateDirectory(aside);
                string to = Path.Combine(aside, $"{i.Name}.{++_moved:000}");
                while (File.Exists(to)) to = Path.Combine(aside, $"{i.Name}.{++_moved:000}");
                File.Move(i.FullName, to);
                if (File.Exists(mine)) { File.Copy(mine, i.FullName, true); var r = new FileInfo(i.FullName); seenNow[i.Name] = (r.Length, r.LastWriteTimeUtc); done.Add($"{i.Name}: the host's world written there, moved aside, your own put back"); }
                else { seenNow.Remove(i.Name); done.Add($"{i.Name}: a new save of the host's world, moved aside"); }
            }
            foreach (var f in Saves(Backup))
            {
                string there = Path.Combine(Profile, Path.GetFileName(f));
                if (!File.Exists(there)) { File.Copy(f, there); var r = new FileInfo(there); seenNow[r.Name] = (r.Length, r.LastWriteTimeUtc); done.Add($"{Path.GetFileName(f)}: missing, your own put back"); }
            }
            _seen.Clear();
            foreach (var kv in seenNow) _seen[kv.Key] = kv.Value;
            return done;
        }

        /// <summary>The last check, then the marker goes.</summary>
        public List<string> End(string backupRoot)
        {
            var done = Check(true);
            try { File.Delete(Path.Combine(backupRoot, Marker)); } catch (IOException) { }
            return done;
        }

        static bool Same(string a, string b)
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            return File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b));
        }
    }
}
