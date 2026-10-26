// Stage 2: the host's NPCs as seen by the guest, and hits sent back.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RDR2Coop.Net
{
    [Flags]
    public enum EntityFlags : byte
    {
        None = 0,
        Dead = 1,
        Hostile = 2,      // hates or is fighting a player
        Shooting = 4,     // fired within the last second
        Animal = 8,
        Fleeing = 16,
        PlayerHorse = 32, // the host's own horse: shown, never hurt through co-op
        Blipped = 64,     // marked on the owner's map (mission characters, targets, enemies)
    }

    // What an NPC is busy with, beyond moving: copies act it out.
    public enum EntityAction : byte
    {
        None = 0,
        Scenario = 1,     // sitting, leaning, smoking, working...: Scenario says which
        HandsUp = 2,
        Cower = 3,
        Ragdoll = 4,      // knocked over, falling
        Hogtied = 5,
    }

    public struct EntityState
    {
        public ushort Id;           // host-assigned, stable while the entity exists
        public EntityFlags Flags;
        public uint Model;
        public Vec3 Position;
        public float Heading;       // degrees
        public Vec3 Velocity;
        public byte HealthPct;      // 0-100 of max health
        public ushort MountId;      // Id of the horse it rides, 0 = on foot
        public uint Weapon;         // weapon hash in hand, 0 = none
        public byte Aim;            // player id the NPC is fighting, 0 = none
        public byte Shots;          // running count of shots fired (wraps at 256)
        public ushort VehicleId;    // vehicle it sits in (same id space), 0 = none
        public sbyte Seat;          // -1 = driver
        public uint Outfit;         // meta-ped outfit it wears, 0 = unknown
        public EntityAction Action;
        public uint Scenario;       // scenario type hash when Action is Scenario

        public bool Dead => (Flags & EntityFlags.Dead) != 0;

        // 39 bytes on the wire: positions relative to the packet's origin.
        public const int WireSize = 2 + 1 + 4 + 6 + 2 + 3 + 1 + 2 + 4 + 1 + 1 + 2 + 1 + 4 + 1 + 4;

        internal void Write(BinaryWriter w, Vec3 origin)
        {
            w.Write(Id);
            w.Write((byte)Flags);
            w.Write(Model);
            Compact.WritePosition(w, Position, origin);
            w.Write((ushort)(((Heading % 360f) + 360f) % 360f * 100f));
            Compact.WriteVelocity(w, Velocity);
            w.Write(HealthPct);
            w.Write(MountId);
            w.Write(Weapon);
            w.Write(Aim);
            w.Write(Shots);
            w.Write(VehicleId);
            w.Write(Seat);
            w.Write(Outfit);
            w.Write((byte)Action);
            w.Write(Scenario);
        }

        internal static EntityState Read(BinaryReader r, Vec3 origin) => new EntityState
        {
            Id = r.ReadUInt16(),
            Flags = (EntityFlags)r.ReadByte(),
            Model = r.ReadUInt32(),
            Position = Compact.ReadPosition(r, origin),
            Heading = r.ReadUInt16() / 100f,
            Velocity = Compact.ReadVelocity(r),
            HealthPct = r.ReadByte(),
            MountId = r.ReadUInt16(),
            Weapon = r.ReadUInt32(),
            Aim = r.ReadByte(),
            Shots = r.ReadByte(),
            VehicleId = r.ReadUInt16(),
            Seat = r.ReadSByte(),
            Outfit = r.ReadUInt32(),
            Action = (EntityAction)r.ReadByte(),
            Scenario = r.ReadUInt32(),
        };
    }

    // Small encodings: positions as 2 cm steps from an origin (+-655 m),
    // velocities as 0.25 m/s steps (+-31.75 m/s), angles as hundredths.
    public static class Compact
    {
        public const float PositionStep = 0.02f;
        public const float MaxOffset = short.MaxValue * PositionStep;   // ~655 m
        public const float VelocityStep = 0.25f;

        public static bool InRange(Vec3 p, Vec3 origin) =>
            Math.Abs(p.X - origin.X) < MaxOffset && Math.Abs(p.Y - origin.Y) < MaxOffset && Math.Abs(p.Z - origin.Z) < MaxOffset;

        static short Step(float v, float step) => (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(v / step)));

        internal static void WritePosition(BinaryWriter w, Vec3 p, Vec3 o)
        {
            w.Write(Step(p.X - o.X, PositionStep)); w.Write(Step(p.Y - o.Y, PositionStep)); w.Write(Step(p.Z - o.Z, PositionStep));
        }

        internal static Vec3 ReadPosition(BinaryReader r, Vec3 o) =>
            new Vec3(o.X + r.ReadInt16() * PositionStep, o.Y + r.ReadInt16() * PositionStep, o.Z + r.ReadInt16() * PositionStep);

        static sbyte Vel(float v) => (sbyte)Math.Max(sbyte.MinValue, Math.Min(sbyte.MaxValue, Math.Round(v / VelocityStep)));

        internal static void WriteVelocity(BinaryWriter w, Vec3 v) { w.Write(Vel(v.X)); w.Write(Vel(v.Y)); w.Write(Vel(v.Z)); }

        internal static Vec3 ReadVelocity(BinaryReader r) =>
            new Vec3(r.ReadSByte() * VelocityStep, r.ReadSByte() * VelocityStep, r.ReadSByte() * VelocityStep);

        // Degrees in (-180, 180] as hundredths in a short.
        internal static short Angle(float deg)
        {
            float d = ((deg % 360f) + 540f) % 360f - 180f;
            return (short)Math.Round(d * 100f);
        }
    }

    [Flags]
    public enum VehicleFlags : byte
    {
        None = 0,
        Destroyed = 1,
        Boat = 2,
        Train = 4,
        Draft = 8,        // pulled by horses (wagons, carts, stagecoaches)
        Blipped = 16,     // marked on the owner's map (mission wagons, targets)
    }

    public struct VehicleState
    {
        public ushort Id;           // shares the id space with EntityState
        public uint Model;
        public Vec3 Position;
        public Vec3 Rotation;       // pitch, roll, yaw in degrees
        public Vec3 Velocity;
        public VehicleFlags Flags;
        public byte HealthPct;

        public const int WireSize = 2 + 4 + 6 + 6 + 3 + 1 + 1;   // 23

        internal void Write(BinaryWriter w, Vec3 origin)
        {
            w.Write(Id);
            w.Write(Model);
            Compact.WritePosition(w, Position, origin);
            w.Write(Compact.Angle(Rotation.X)); w.Write(Compact.Angle(Rotation.Y)); w.Write(Compact.Angle(Rotation.Z));
            Compact.WriteVelocity(w, Velocity);
            w.Write((byte)Flags);
            w.Write(HealthPct);
        }

        internal static VehicleState Read(BinaryReader r, Vec3 origin) => new VehicleState
        {
            Id = r.ReadUInt16(),
            Model = r.ReadUInt32(),
            Position = Compact.ReadPosition(r, origin),
            Rotation = new Vec3(r.ReadInt16() / 100f, r.ReadInt16() / 100f, r.ReadInt16() / 100f),
            Velocity = Compact.ReadVelocity(r),
            Flags = (VehicleFlags)r.ReadByte(),
            HealthPct = r.ReadByte(),
        };
    }

    // A train, sent as its configuration (which carriages) plus where it is on
    // the track and how fast it goes; the other game builds its own copy.
    public struct TrainState
    {
        public ushort Id;           // shares the id space with the others
        public uint Config;         // train configuration hash
        public Vec3 Position;       // the engine
        public float Speed;         // m/s, signed: + = the train's own direction
        public bool Direction;
        public byte Cars;

        public const int WireSize = 2 + 4 + 6 + 2 + 1 + 1;   // 16

        internal void Write(BinaryWriter w, Vec3 origin)
        {
            w.Write(Id);
            w.Write(Config);
            Compact.WritePosition(w, Position, origin);
            w.Write((short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(Speed * 100f))));
            w.Write((byte)(Direction ? 1 : 0));
            w.Write(Cars);
        }

        internal static TrainState Read(BinaryReader r, Vec3 origin) => new TrainState
        {
            Id = r.ReadUInt16(),
            Config = r.ReadUInt32(),
            Position = Compact.ReadPosition(r, origin),
            Speed = r.ReadInt16() / 100f,
            Direction = r.ReadByte() != 0,
            Cars = r.ReadByte(),
        };
    }

    public struct WorldSnapshot
    {
        public uint Seq;
        public byte Part, PartCount;   // a snapshot is sent as one or more packets
        public List<EntityState> Entities;
        public List<VehicleState> Vehicles;
        public List<TrainState> Trains;
        public const int MaxEntities = 48;
        public const int MaxParts = 16;   // more than a full snapshot ever needs
        public const int MaxVehicles = 16;
        public const int MaxTrains = 4;
    }

    // Puts a snapshot's parts back together. A snapshot with a lost part is
    // dropped (the next one is 50 ms away), never shown half-empty.
    public sealed class WorldAssembler
    {
        sealed class Pending
        {
            public uint Seq;
            public WorldSnapshot[] Parts;
            public int Have;
        }

        readonly Dictionary<byte, Pending> _bySender = new Dictionary<byte, Pending>();
        readonly Dictionary<byte, uint> _lastDone = new Dictionary<byte, uint>();

        public bool Add(byte from, WorldSnapshot part, out WorldSnapshot whole)
        {
            whole = default(WorldSnapshot);
            if (part.PartCount == 0 || part.Part >= part.PartCount) return false;
            if (_lastDone.TryGetValue(from, out var done) && part.Seq <= done) return false;
            if (!_bySender.TryGetValue(from, out var p) || part.Seq > p.Seq)
                _bySender[from] = p = new Pending { Seq = part.Seq, Parts = new WorldSnapshot[part.PartCount] };
            else if (part.Seq < p.Seq || p.Parts.Length != part.PartCount) return false;
            if (p.Parts[part.Part].PartCount != 0) return false;   // duplicate
            p.Parts[part.Part] = part;
            if (++p.Have < p.Parts.Length) return false;

            whole = new WorldSnapshot { Seq = p.Seq, PartCount = 1, Entities = new List<EntityState>(),
                                        Vehicles = new List<VehicleState>(), Trains = new List<TrainState>() };
            foreach (var x in p.Parts)
            {
                whole.Entities.AddRange(x.Entities ?? new List<EntityState>());
                whole.Vehicles.AddRange(x.Vehicles ?? new List<VehicleState>());
                whole.Trains.AddRange(x.Trains ?? new List<TrainState>());
            }
            // Whatever the sender claimed, never more than a full snapshot.
            whole.Entities = whole.Entities.Take(WorldSnapshot.MaxEntities).ToList();
            whole.Vehicles = whole.Vehicles.Take(WorldSnapshot.MaxVehicles).ToList();
            whole.Trains = whole.Trains.Take(WorldSnapshot.MaxTrains).ToList();
            _bySender.Remove(from);
            _lastDone[from] = p.Seq;
            return true;
        }

        public void Forget(byte from)
        {
            _bySender.Remove(from);
            _lastDone.Remove(from);
        }
    }

    // One piece of a character's look: the model part, its textures and colours.
    public struct MetaTag
    {
        public uint Drawable, Albedo, Normal, Material, Palette;
        public byte Tint0, Tint1, Tint2;
        public const int WireSize = 5 * 4 + 3;
    }

    // A player's whole look, sent when it changes (and now and then for late
    // joiners) so the others' puppets wear the same clothes.
    public struct Appearance
    {
        public uint Model;
        public List<MetaTag> Tags;
        public const int MaxTags = 48;

        // Cheap fingerprint to notice changes. The order the game lists the pieces in isn't
        // stable for every outfit, so each piece is hashed on its own and the hashes are added:
        // the same pieces in another order give the same signature.
        public uint Signature()
        {
            uint h = 2166136261u ^ Model;
            foreach (var t in Tags ?? new List<MetaTag>())
            {
                uint p = 2166136261u;
                foreach (uint v in new[] { t.Drawable, t.Albedo, t.Normal, t.Material, t.Palette, (uint)(t.Tint0 | t.Tint1 << 8 | t.Tint2 << 16) })
                    p = (p ^ v) * 16777619u;
                h += p;
            }
            return h;
        }
    }

    // When to send our look: the first time, when it has changed and held for two looks in a
    // row, and now and then for late joiners. A look that reads differently every time (some
    // outfits do) is never sent twice a second: every send makes the others rebuild the
    // stand-in's clothes, which freezes it in a T-pose while it keeps happening.
    public sealed class LookSender
    {
        public const int RepeatMs = 15000;
        uint _sent, _pending;
        long _sentAt = long.MinValue / 2;   // "long ago" without overflowing now - _sentAt

        public uint Sent => _sent;

        /// <summary>True when this look should go out now; changed says whether it's a new look (for the log).</summary>
        public bool ShouldSend(uint sig, long now, out bool changed)
        {
            bool due = now - _sentAt >= RepeatMs;
            changed = _sent == 0;
            if (sig != _sent && sig != _pending) { _pending = sig; if (!due) return false; }   // seen once: wait for the next look, unless a repeat is due anyway
            else if (sig != _sent) changed = true;                                            // held for two looks: a real change
            else if (!due) return false;
            _sent = sig; _pending = 0; _sentAt = now;
            return true;
        }

        /// <summary>Someone joined: our look goes out again at the next look.</summary>
        public void Resend() { _sentAt = long.MinValue / 2; }
    }

    // A hit, either on an NPC (sent to the game that runs it) or on a player's
    // character in someone else's world (sent to that player, EntityId 0).
    public struct Hit
    {
        public uint Seq;            // per sender; each hit is sent twice, receiver drops repeats
        public ushort EntityId;
        public short Damage;
        public int Bone;
        public uint Weapon;         // what the shooter had in hand (0 = unknown), for enRage's grudges

        public const int HeadBone = 21030;   // SKEL_Head
    }
}
