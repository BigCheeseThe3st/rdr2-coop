// Stage 2 logic that needs no game: which NPCs to send, stable ids for them,
// and smoothing their movement on the guest.
using System;
using System.Collections.Generic;
using System.Linq;

namespace RDR2Coop.Net
{
    // Game handles are reused after an entity is deleted, so the host gives
    // each tracked entity its own small id for as long as it stays tracked.
    public sealed class EntityIds
    {
        readonly Dictionary<int, ushort> _byHandle = new Dictionary<int, ushort>();
        ushort _next = 1;

        public ushort IdFor(int handle)
        {
            if (_byHandle.TryGetValue(handle, out var id)) return id;
            do { id = _next++; if (_next == 0) _next = 1; } while (_byHandle.ContainsValue(id));
            _byHandle[handle] = id;
            return id;
        }

        public bool TryGet(int handle, out ushort id) => _byHandle.TryGetValue(handle, out id);

        public int HandleFor(ushort id)
        {
            foreach (var kv in _byHandle) if (kv.Value == id) return kv.Key;
            return 0;
        }

        // Forget handles that were not tracked this round.
        public void KeepOnly(ICollection<int> handles)
        {
            foreach (var h in _byHandle.Keys.Where(h => !handles.Contains(h)).ToList()) _byHandle.Remove(h);
        }
    }

    public struct Candidate
    {
        public int Handle;
        public Vec3 Position;
        public int MountHandle;     // 0 = on foot
        public bool Dead;
        public bool Hostile;
    }

    public static class EntitySelector
    {
        public const float Radius = 120f;
        public const float DeadRadius = 50f;

        // Nearest to any of the players first (the owner and everyone sharing
        // its area), hostiles before bystanders at equal distance; a rider
        // always brings its horse along. Returns handles.
        public static List<int> Pick(IEnumerable<Candidate> all, IList<Vec3> players, int cap)
        {
            var byHandle = new Dictionary<int, Candidate>();
            foreach (var c in all) byHandle[c.Handle] = c;

            float Dist(Candidate c)
            {
                float d = float.MaxValue;
                foreach (var p in players) d = Math.Min(d, Vec3.Distance(c.Position, p));
                return d;
            }

            var ranked = byHandle.Values
                .Where(c => Dist(c) <= (c.Dead ? DeadRadius : Radius))
                .OrderBy(c => Dist(c) - (c.Hostile ? 15f : 0f))
                .ToList();

            var picked = new List<int>();
            var set = new HashSet<int>();
            foreach (var c in ranked)
            {
                if (set.Contains(c.Handle)) continue;
                bool needsMount = c.MountHandle != 0 && !set.Contains(c.MountHandle) && byHandle.ContainsKey(c.MountHandle);
                if (picked.Count + (needsMount ? 2 : 1) > cap) continue;
                if (needsMount) { picked.Add(c.MountHandle); set.Add(c.MountHandle); }
                picked.Add(c.Handle);
                set.Add(c.Handle);
            }
            return picked;
        }
    }

    // Keeps recent world snapshots and returns every entity's state slightly in
    // the past, blended between the two snapshots either side of that moment.
    public sealed class WorldBuffer
    {
        struct Entry
        {
            public long ArrivedMs;
            public uint Seq;
            public Dictionary<ushort, EntityState> Entities;
            public Dictionary<ushort, VehicleState> Vehicles;
            public List<TrainState> Trains;
        }

        readonly List<Entry> _buffer = new List<Entry>();
        readonly int _delayMs, _maxExtrapolateMs;

        public WorldBuffer(int delayMs = 70, int maxExtrapolateMs = 250)
        {
            _delayMs = delayMs;
            _maxExtrapolateMs = maxExtrapolateMs;
        }

        public void Clear() => _buffer.Clear();

        public bool Push(WorldSnapshot w, long arrivedMs)
        {
            if (_buffer.Count > 0 && w.Seq <= _buffer[_buffer.Count - 1].Seq) return false;
            var d = new Dictionary<ushort, EntityState>();
            foreach (var e in w.Entities ?? new List<EntityState>()) d[e.Id] = e;
            var v = new Dictionary<ushort, VehicleState>();
            foreach (var e in w.Vehicles ?? new List<VehicleState>()) v[e.Id] = e;
            _buffer.Add(new Entry { ArrivedMs = arrivedMs, Seq = w.Seq, Entities = d, Vehicles = v, Trains = w.Trains ?? new List<TrainState>() });
            if (_buffer.Count > 16) _buffer.RemoveAt(0);
            return true;
        }

        // The entities to show now. Something missing from the newest snapshot
        // the moment we draw is gone.
        public List<EntityState> Sample(long nowMs)
        {
            var result = new List<EntityState>();
            if (_buffer.Count == 0) return result;
            long t = nowMs - _delayMs;

            int i = _buffer.FindLastIndex(e => e.ArrivedMs <= t);
            if (i < 0) return _buffer[0].Entities.Values.ToList();

            var a = _buffer[i];
            if (i == _buffer.Count - 1)
            {
                float ahead = Math.Min(t - a.ArrivedMs, _maxExtrapolateMs) / 1000f;
                foreach (var e in a.Entities.Values)
                {
                    var s = e;
                    if (!s.Dead) s.Position = s.Position + s.Velocity * ahead;
                    result.Add(s);
                }
                return result;
            }

            var b = _buffer[i + 1];
            float f = (t - a.ArrivedMs) / (float)Math.Max(1, b.ArrivedMs - a.ArrivedMs);
            foreach (var eb in b.Entities.Values)
            {
                if (!a.Entities.TryGetValue(eb.Id, out var ea) || ea.Model != eb.Model || ea.Dead != eb.Dead
                    || ea.MountId != eb.MountId)
                {
                    result.Add(eb);
                    continue;
                }
                var s = eb;
                s.Position = Vec3.Lerp(ea.Position, eb.Position, f);
                s.Velocity = Vec3.Lerp(ea.Velocity, eb.Velocity, f);
                s.Heading = Interpolator.LerpAngle(ea.Heading, eb.Heading, f);
                result.Add(s);
            }
            return result;
        }

        // Trains: the latest snapshot as is (the copy is driven by speed, and
        // corrected along the track; no blending needed).
        public List<TrainState> LatestTrains() => _buffer.Count == 0 ? new List<TrainState>() : _buffer[_buffer.Count - 1].Trains;

        // Vehicles, the same way: blended between snapshots, a short way ahead
        // along their velocity when the next snapshot is late.
        public List<VehicleState> SampleVehicles(long nowMs)
        {
            var result = new List<VehicleState>();
            if (_buffer.Count == 0) return result;
            long t = nowMs - _delayMs;
            int i = _buffer.FindLastIndex(e => e.ArrivedMs <= t);
            if (i < 0) return _buffer[0].Vehicles.Values.ToList();

            var a = _buffer[i];
            if (i == _buffer.Count - 1)
            {
                float ahead = Math.Min(t - a.ArrivedMs, _maxExtrapolateMs) / 1000f;
                foreach (var e in a.Vehicles.Values)
                {
                    var s = e;
                    if ((s.Flags & VehicleFlags.Destroyed) == 0) s.Position = s.Position + s.Velocity * ahead;
                    result.Add(s);
                }
                return result;
            }

            var b = _buffer[i + 1];
            float f = (t - a.ArrivedMs) / (float)Math.Max(1, b.ArrivedMs - a.ArrivedMs);
            foreach (var vb in b.Vehicles.Values)
            {
                if (!a.Vehicles.TryGetValue(vb.Id, out var va) || va.Model != vb.Model) { result.Add(vb); continue; }
                var s = vb;
                s.Position = Vec3.Lerp(va.Position, vb.Position, f);
                s.Velocity = Vec3.Lerp(va.Velocity, vb.Velocity, f);
                s.Rotation = new Vec3(Interpolator.LerpAngle(va.Rotation.X, vb.Rotation.X, f),
                                      Interpolator.LerpAngle(va.Rotation.Y, vb.Rotation.Y, f),
                                      Interpolator.LerpAngle(va.Rotation.Z, vb.Rotation.Z, f));
                result.Add(s);
            }
            return result;
        }
    }

    public struct VehicleCandidate
    {
        public int Handle;
        public Vec3 Position;
        public bool HasPlayer;       // a player (or a player's puppet) is in it
        public bool HasPickedPed;    // carries an NPC we are sending
    }

    public static class VehicleSelector
    {
        public const float Radius = 150f;

        // Players' vehicles first, then ones carrying NPCs we send, then the
        // nearest; anything further than Radius from every player is left out
        // unless a player is in it.
        public static List<int> Pick(IEnumerable<VehicleCandidate> all, IList<Vec3> players, int cap)
        {
            float Dist(VehicleCandidate c)
            {
                float d = float.MaxValue;
                foreach (var p in players) d = Math.Min(d, Vec3.Distance(c.Position, p));
                return d;
            }
            return all.Where(c => c.HasPlayer || Dist(c) <= Radius)
                      .OrderBy(c => c.HasPlayer ? 0 : c.HasPickedPed ? 1 : 2)
                      .ThenBy(Dist)
                      .Take(cap)
                      .Select(c => c.Handle)
                      .ToList();
        }
    }

    public static class Shots
    {
        public const int MaxBurst = 6;

        // Shots fired since the last count we acted on; -1 = first sighting.
        public static int NewShots(int last, byte now) =>
            last < 0 ? 0 : Math.Min(((now - last) & 0xFF), MaxBurst);
    }
}
