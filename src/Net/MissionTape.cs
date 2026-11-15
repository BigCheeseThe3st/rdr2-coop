// Recording a story mission's script memory for mapping it (0.9.0-beta.8.4). Beta players with logs on record the
// missions they play (Mod/MissionRecorder.cs reads the memory and labels the values that are people or map markers);
// the author merges everyone's recordings of a mission to learn where it keeps its enemies, allies, targets and
// stages. Pure: what changed since the last read, which slots change too often to keep (timers, counters), the
// tape's lines and its size cap.
//
// LINES (scripts\RDR2Coop.missions.log; ms = the mod's clock; slots and values in hex where marked; Mod/MissionRecorder
// puts "@<script hash> " in front of each script's lines, as several scripts can be recorded at once):
//   M <ms> start <script> <hash> size <n> sp <n> read <n>    a mission's script found: its stack and how much is read
//   S <ms> <slot>=<value> ...                                 the first read: every non-zero slot (value hex)
//   C <ms> <slot>=<old>><new> ...                             later reads: what changed
//   B <ms> <slot> ...                                         slots muted from now on (changed more than BusyAfter times)
//   E <ms> <slot> <handle> ...                                a changed value is an entity or a blip (Mod/MissionRecorder)
//   K / X / V / P <ms> ...                                    deaths, entities gone, the mod's mission events, our position
//   M <ms> end <script>                                       the mission's script stopped
using System;
using System.Collections.Generic;
using System.Text;

namespace RDR2Coop.Net
{
    public sealed class MissionTape
    {
        public const int BusyAfter = 24;           // a slot that changed this often is a timer or a counter: muted
        public const int MaxBytes = 900_000;       // under the log store's 1 MB per upload
        const int PerLine = 200;

        readonly Action<string> _write;
        readonly long _max;
        ulong[] _last = new ulong[0];
        int[] _changes = new int[0];
        readonly HashSet<int> _busy = new HashSet<int>();

        public long Bytes { get; private set; }
        public bool Full => Bytes >= _max;

        /// <summary>max: the size cap in bytes (MaxBytes for uploads; long.MaxValue keeps everything).</summary>
        public MissionTape(Action<string> write, long max = MaxBytes) { _write = write; _max = max; }

        /// <summary>One line, unless the tape is full (the line saying so is the last one).</summary>
        public bool Line(string s)
        {
            if (Full) return false;
            Bytes += s.Length + 1;
            _write(s);
            if (Full) _write($"F full at {Bytes} bytes: no more recording until the mod starts again");
            return true;
        }

        public void Start(long ms, string script, uint hash, int size, int sp, ulong[] now, int count)
        {
            _last = new ulong[count];
            _changes = new int[count];
            _busy.Clear();
            Array.Copy(now, _last, count);
            Line($"M {ms} start {script} {hash:x8} size {size} sp {sp} read {count}");
            var parts = new List<string>();
            for (int i = 0; i < count; i++) if (now[i] != 0) parts.Add($"{i}={now[i]:x}");
            Lines("S", ms, parts);
        }

        /// <summary>A later read: logs what changed (muting busy slots) and returns the changed slots still recorded.</summary>
        public List<int> Changes(long ms, ulong[] now, int count)
        {
            if (count > _last.Length)   // the stack in use grew: the new slots count as changed from 0
            {
                Array.Resize(ref _last, count);
                Array.Resize(ref _changes, count);
            }
            var changed = new List<int>();
            var parts = new List<string>();
            var muted = new List<string>();
            for (int i = 0; i < count; i++)
            {
                if (now[i] == _last[i]) continue;
                ulong was = _last[i];
                _last[i] = now[i];
                if (_busy.Contains(i)) continue;
                if (++_changes[i] > BusyAfter) { _busy.Add(i); muted.Add(i.ToString()); continue; }
                parts.Add($"{i}={was:x}>{now[i]:x}");
                changed.Add(i);
            }
            Lines("C", ms, parts);
            Lines("B", ms, muted);
            return changed;
        }

        void Lines(string tag, long ms, List<string> parts)
        {
            for (int at = 0; at < parts.Count; at += PerLine)
            {
                var sb = new StringBuilder($"{tag} {ms}");
                for (int i = at; i < Math.Min(parts.Count, at + PerLine); i++) sb.Append(' ').Append(parts[i]);
                if (!Line(sb.ToString())) return;
            }
        }
    }
}
