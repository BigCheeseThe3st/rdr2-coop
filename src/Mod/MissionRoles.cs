// Who's who in the running missions and encounters, from the mission map (src/Net/MissionMap.cs: story missions,
// stranger missions, ambushes and random encounters): once a second the running scripts the map knows are found
// (ScriptMemory, the .NET 9 build's loader; read only), the statics the map names are read, and each entity handle
// found there gets the slot's side. OwnedWorld asks Side() before guessing from relationships: a mapped ally is
// guarded on guests' screens even while the game's relationship says neutral, a mapped enemy is never taken for an
// ally. Nothing mapped (other builds, a slot that changes side): None, and the old guess decides.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class MissionRoles
    {
        const int EveryMs = 1000, Piece = 16384, MaxSlots = 32768, MaxScripts = 6;
        static readonly Dictionary<int, MissionRole> _roles = new Dictionary<int, MissionRole>();
        Dictionary<uint, string> _mapped;   // script name hash -> name, for every script the map knows
        readonly uint[] _ids = new uint[1024], _hashes = new uint[1024];
        readonly ulong[] _buf = new ulong[MaxSlots];
        long _lastMs, _saidMs;
        string _said;

        /// <summary>Ally or Enemy when the map is sure about this entity, else None.</summary>
        public static MissionRole Side(int handle) => _roles.TryGetValue(handle, out var r) ? MissionMap.Side(r) : MissionRole.None;
        /// <summary>One of a running mission's or encounter's own people, horses, wagons or props (sent before bystanders).</summary>
        public static bool Known(int handle) => _roles.ContainsKey(handle);

        public void Tick(long now)
        {
            if (now - _lastMs < EveryMs) return;
            _lastMs = now;
            if (!ScriptMemory.Check(ScriptMemory.GameDir())) return;
            if (_mapped == null) { _mapped = new Dictionary<uint, string>(); foreach (var s in MissionMap.Scripts()) _mapped[MISC.GET_HASH_KEY(s)] = s; }
            int n = ScriptMemory.Threads(_ids, _hashes);
            _roles.Clear();
            if (n <= 0) return;
            var running = new List<string>();
            for (int t = 0; t < n && running.Count < MaxScripts; t++)
                if (_mapped.TryGetValue(_hashes[t], out var name) && !running.Contains(name)) { running.Add(name); Read(name, _hashes[t]); }
            string say = running.Count == 0 ? "" : string.Join(", ", running) + $": {_roles.Values.Count(r => MissionMap.Side(r) == MissionRole.Ally)} allies, " +
                                                   $"{_roles.Values.Count(r => MissionMap.Side(r) == MissionRole.Enemy)} enemies";
            if (say != _said && (now - _saidMs > 10000 || string.IsNullOrEmpty(_said)))   // as people come and go, at most every 10 s
            {
                _said = say; _saidMs = now;
                if (say.Length > 0) CoopScript.Log("mission map: " + say);
            }
        }

        void Read(string name, uint hash)
        {
            var slots = MissionMap.Slots(name);
            int count = Math.Min(MaxSlots, slots.Max(s => s.Start + (s.Count - 1) * s.Stride) + 1);
            if (ScriptMemory.Info(hash, out int size, out int sp) > 0 && sp > 0 && sp <= size) count = Math.Min(count, sp);
            for (int at = 0; at < count; at += Piece)
                if (ScriptMemory.Read(hash, at, _buf, at, Math.Min(Piece, count - at)) <= 0) return;
            foreach (var s in slots)
                foreach (int i in MissionMap.Indices(s))
                {
                    if (i >= count) break;
                    int h = (int)(uint)_buf[i];
                    if (h <= 0 || (_buf[i] >> 32) != 0) continue;   // entity handles are small positive ints
                    _roles[h] = _roles.TryGetValue(h, out var r) ? r | s.Role : s.Role;
                }
        }
    }
}
