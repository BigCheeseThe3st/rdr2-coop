// Wire format shared by host and guest. Pure .NET so it can be tested
// outside the game.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RDR2Coop.Net
{
    public struct Vec3
    {
        public float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) =>
            new Vec3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

        public static float Distance(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public override string ToString() => $"({X:0.0}, {Y:0.0}, {Z:0.0})";
    }

    [Flags]
    public enum StateFlags : byte
    {
        None = 0,
        Mounted = 1,
        Dead = 2,
        InCutscene = 4,
        ScreenFaded = 16, // fading or faded to black (mission transitions)
        Downed = 32,      // knocked down in someone else's area, waiting for a revive
        InMission = 64,   // a story mission runs in this player's game
        Driving = 8,      // drives a vehicle: MountPosition/MountHeading/Velocity are the vehicle's
    }

    // Must match server/relay.py.
    public enum PacketType : byte
    {
        Hello = 1,      // client -> server: version, prefers hosting, name
        Challenge = 2,  // server -> client: 16-byte nonce
        Auth = 3,       // client -> server: HMAC-SHA256(password, nonce)
        Welcome = 4,    // server -> client: your player id, host id
        Reject = 5,     // server -> client: reason
        State = 6,      // player position, relayed to every other player
        Bye = 7,        // client leaving
        World = 8,      // NPCs around the sender's player, relayed to every other player
        Hit = 9,        // to the game that owns the NPC, or to the player an NPC hit
        Roster = 10,    // server -> clients: who is in, and who hosts
        Party = 11,     // mission party: invite / accept / decline / members / end, to everyone
        Appearance = 12, // a player's clothes and looks, to everyone
        Objectives = 13, // where the host's story mission wants players to go, to everyone
        Action = 14,     // something a player did to a copy (hogtie...), to the game that owns it
        Notice = 15,     // enRage news / grudge notices and nemesis lists, to one player or everyone
        EntityLook = 16, // an NPC's or horse's full look (clothes, saddle...), sent once per NPC, to everyone
    }

    public enum PartyKind : byte { Invite = 1, Accept = 2, Decline = 3, Members = 4, End = 5, Revive = 6, Reward = 7, AtObjective = 8 }

    public struct PartyMsg
    {
        public PartyKind Kind;
        public uint PartyId;
        public List<byte> Members;   // with Members; with Revive: [the player revived]
        public int Amount;           // with Reward: money in cents
    }

    public enum PlayerRole : byte { Guest = 0, Host = 1 }

    public struct Objective
    {
        public Vec3 Position;
        public float Radius;
        public const int Max = 8;
    }

    public enum ActionKind : byte { Hogtie = 1 }

    // Clock: the host's time and weather (older versions ignore kinds they don't know).
    public enum NoticeKind : byte { Text = 0, NemesesRequest = 1, Nemeses = 2, Encounter = 3, EncounterResult = 4, Names = 5, Clock = 6 }

    public struct NoticeMsg
    {
        public NoticeKind Kind;
        public string Text;          // lines separated by \n
        public const int MaxBytes = 1100;
    }

    public struct GuestAction
    {
        public uint Seq;           // shares the sender's hit sequence (sent twice, deduplicated)
        public ActionKind Kind;
        public ushort EntityId;    // in the receiver's (owner's) id space
    }

    public struct RosterEntry
    {
        public byte Id;
        public PlayerRole Role;
        public string Name;
    }

    public struct PlayerState
    {
        public uint Seq;
        public int TimeMs;        // sender's clock, used only for ordering/spacing
        public StateFlags Flags;
        public uint PedModel;     // model the partner should draw us as
        public Vec3 Position;
        public float Heading;
        public Vec3 Velocity;
        public uint MountModel;   // 0 when on foot
        public Vec3 MountPosition;
        public float MountHeading;
        public byte Following;    // whose game runs our area; 0 = our own
        public ushort VehicleId;  // vehicle we sit in, in our area owner's id space; 0 = none
        public sbyte Seat;        // -1 = driver
        public uint Weapon;       // the weapon in hand, 0 = none; sent last, so versions before 0.7.1 skip it

        public bool Mounted => (Flags & StateFlags.Mounted) != 0;
        public bool Dead => (Flags & StateFlags.Dead) != 0;
    }

    public sealed class Packet
    {
        public PacketType Type;
        public byte From;          // stamped by the server
        public byte To = Protocol.Broadcast;
        public int ProtocolVersion;
        public bool PreferHost;
        public string Name = "";
        public string Reason = "";
        public byte[] Nonce;
        public byte[] Mac;
        public byte MyId, HostId;
        public List<RosterEntry> Roster;
        public PlayerState State;
        public WorldSnapshot World;
        public Hit Hit;
        public PartyMsg Party;
        public Appearance Appearance;
        public List<Objective> Objectives;
        public GuestAction Action;
        public NoticeMsg Notice;
        public ushort LookId;        // with EntityLook: the NPC (World snapshot id)
    }

    public static class Protocol
    {
        public const int Version = 13;
        public const byte Broadcast = 255;
        static readonly byte[] Magic = { (byte)'R', (byte)'D', (byte)'C', (byte)'O' };
        const int MaxNameLength = 32;

        public static byte[] Hello(string name, bool preferHost) =>
            Write(new Packet { Type = PacketType.Hello, ProtocolVersion = Version, PreferHost = preferHost, Name = name });

        public static byte[] Auth(byte[] nonce, string password)
        {
            using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(password ?? "")))
                return Write(new Packet { Type = PacketType.Auth, Mac = h.ComputeHash(nonce) });
        }

        public static byte[] State(PlayerState s) => Write(new Packet { Type = PacketType.State, State = s });

        public static byte[] Bye() => Write(new Packet { Type = PacketType.Bye });

        public const int WorldPartBudget = 1200;   // bytes per packet, well under internet MTUs

        // One snapshot as one or more packets: each part has its own origin
        // (positions are stored relative to it) and stays under the budget.
        public static List<byte[]> World(WorldSnapshot w)
        {
            var ents = (w.Entities ?? new List<EntityState>()).Take(WorldSnapshot.MaxEntities).ToList();
            var vehs = (w.Vehicles ?? new List<VehicleState>()).Take(WorldSnapshot.MaxVehicles).ToList();
            var trains = (w.Trains ?? new List<TrainState>()).Take(WorldSnapshot.MaxTrains).ToList();
            var parts = new List<WorldSnapshot>();
            var cur = NewPart(w.Seq);
            Vec3? origin = null;
            int size = 0;
            const int header = 7 + 4 + 2 + 12 + 3;

            void Close() { if (cur.Entities.Count + cur.Vehicles.Count + cur.Trains.Count > 0) parts.Add(cur); cur = NewPart(w.Seq); origin = null; size = 0; }
            bool Fits(Vec3 p, int bytes) => (origin == null || Compact.InRange(p, origin.Value)) && header + size + bytes <= WorldPartBudget;

            foreach (var t in trains)
            {
                if (!Fits(t.Position, TrainState.WireSize)) Close();
                if (origin == null) origin = t.Position;
                cur.Trains.Add(t); size += TrainState.WireSize;
            }
            foreach (var v in vehs)
            {
                if (!Fits(v.Position, VehicleState.WireSize)) Close();
                if (origin == null) origin = v.Position;
                cur.Vehicles.Add(v); size += VehicleState.WireSize;
            }
            foreach (var e in ents)
            {
                if (!Fits(e.Position, EntityState.WireSize)) Close();
                if (origin == null) origin = e.Position;
                cur.Entities.Add(e); size += EntityState.WireSize;
            }
            Close();
            if (parts.Count == 0) parts.Add(NewPart(w.Seq));

            var packets = new List<byte[]>(parts.Count);
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                part.Part = (byte)i;
                part.PartCount = (byte)parts.Count;
                packets.Add(Write(new Packet { Type = PacketType.World, World = part }));
            }
            return packets;
        }

        static WorldSnapshot NewPart(uint seq) =>
            new WorldSnapshot { Seq = seq, Entities = new List<EntityState>(), Vehicles = new List<VehicleState>(), Trains = new List<TrainState>() };

        public static byte[] HitPacket(Hit h, byte to) => Write(new Packet { Type = PacketType.Hit, Hit = h, To = to });

        public static byte[] PartyPacket(PartyMsg m) => Write(new Packet { Type = PacketType.Party, Party = m });

        public static byte[] AppearancePacket(Appearance a) => Write(new Packet { Type = PacketType.Appearance, Appearance = a });

        public static byte[] EntityLookPacket(ushort id, Appearance a) => Write(new Packet { Type = PacketType.EntityLook, LookId = id, Appearance = a });

        public static byte[] ObjectivesPacket(List<Objective> o) => Write(new Packet { Type = PacketType.Objectives, Objectives = o });

        public static byte[] ActionPacket(GuestAction a, byte to) => Write(new Packet { Type = PacketType.Action, Action = a, To = to });

        public static byte[] NoticePacket(NoticeMsg n, byte to) => Write(new Packet { Type = PacketType.Notice, Notice = n, To = to });

        public static byte[] Write(Packet p)
        {
            using (var ms = new MemoryStream(96))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Magic);
                w.Write((byte)p.Type);
                w.Write(p.From);
                w.Write(p.To);
                switch (p.Type)
                {
                    case PacketType.Hello:
                        w.Write(p.ProtocolVersion);
                        w.Write((byte)(p.PreferHost ? 1 : 0));
                        WriteString(w, p.Name);
                        break;
                    case PacketType.Challenge:
                        w.Write(p.Nonce);
                        break;
                    case PacketType.Auth:
                        w.Write(p.Mac);
                        break;
                    case PacketType.Welcome:
                        w.Write(p.ProtocolVersion);
                        w.Write(p.MyId);
                        w.Write(p.HostId);
                        break;
                    case PacketType.Reject:
                        WriteString(w, p.Reason);
                        break;
                    case PacketType.Roster:
                        var roster = p.Roster ?? new List<RosterEntry>();
                        w.Write((byte)roster.Count);
                        foreach (var e in roster) { w.Write(e.Id); w.Write((byte)e.Role); WriteString(w, e.Name); }
                        break;
                    case PacketType.State:
                        WriteState(w, p.State);
                        break;
                    case PacketType.World:
                        var list = p.World.Entities ?? new List<EntityState>();
                        var vehicles = p.World.Vehicles ?? new List<VehicleState>();
                        var trainList = p.World.Trains ?? new List<TrainState>();
                        var origin = trainList.Count > 0 ? trainList[0].Position : vehicles.Count > 0 ? vehicles[0].Position
                                   : list.Count > 0 ? list[0].Position : new Vec3(0, 0, 0);
                        w.Write(p.World.Seq);
                        w.Write(p.World.Part);
                        w.Write(p.World.PartCount);
                        WriteVec(w, origin);
                        w.Write((byte)list.Count);
                        foreach (var e in list) e.Write(w, origin);
                        w.Write((byte)vehicles.Count);
                        foreach (var v in vehicles) v.Write(w, origin);
                        w.Write((byte)trainList.Count);
                        foreach (var t in trainList) t.Write(w, origin);
                        break;
                    case PacketType.Appearance:
                        WriteLook(w, p.Appearance);
                        break;
                    case PacketType.EntityLook:
                        w.Write(p.LookId);
                        WriteLook(w, p.Appearance);
                        break;
                    case PacketType.Party:
                        w.Write((byte)p.Party.Kind);
                        w.Write(p.Party.PartyId);
                        var members = p.Party.Members ?? new List<byte>();
                        w.Write((byte)members.Count);
                        foreach (var id in members) w.Write(id);
                        w.Write(p.Party.Amount);
                        break;
                    case PacketType.Hit:
                        w.Write(p.Hit.Seq);
                        w.Write(p.Hit.EntityId);
                        w.Write(p.Hit.Damage);
                        w.Write(p.Hit.Bone);
                        w.Write(p.Hit.Weapon);
                        break;
                    case PacketType.Objectives:
                        var objs = (p.Objectives ?? new List<Objective>()).Take(Objective.Max).ToList();
                        w.Write((byte)objs.Count);
                        foreach (var o in objs) { WriteVec(w, o.Position); w.Write(o.Radius); }
                        break;
                    case PacketType.Action:
                        w.Write(p.Action.Seq);
                        w.Write((byte)p.Action.Kind);
                        w.Write(p.Action.EntityId);
                        break;
                    case PacketType.Notice:
                        w.Write((byte)p.Notice.Kind);
                        var text = Encoding.UTF8.GetBytes(p.Notice.Text ?? "");
                        if (text.Length > NoticeMsg.MaxBytes) Array.Resize(ref text, NoticeMsg.MaxBytes);
                        w.Write((ushort)text.Length);
                        w.Write(text);
                        break;
                }
                return ms.ToArray();
            }
        }

        // Returns null for anything that isn't a well-formed packet.
        public static Packet Read(byte[] data, int length)
        {
            if (data == null || length < 7) return null;
            for (int i = 0; i < 4; i++) if (data[i] != Magic[i]) return null;
            try
            {
                using (var ms = new MemoryStream(data, 4, length - 4))
                using (var r = new BinaryReader(ms))
                {
                    var p = new Packet { Type = (PacketType)r.ReadByte(), From = r.ReadByte(), To = r.ReadByte() };
                    switch (p.Type)
                    {
                        case PacketType.Hello:
                            p.ProtocolVersion = r.ReadInt32();
                            p.PreferHost = r.ReadByte() != 0;
                            p.Name = ReadString(r);
                            break;
                        case PacketType.Challenge:
                            p.Nonce = ReadExactly(r, 16);
                            break;
                        case PacketType.Auth:
                            p.Mac = ReadExactly(r, 32);
                            break;
                        case PacketType.Welcome:
                            p.ProtocolVersion = r.ReadInt32();
                            p.MyId = r.ReadByte();
                            p.HostId = r.ReadByte();
                            break;
                        case PacketType.Reject:
                            p.Reason = ReadString(r);
                            break;
                        case PacketType.Roster:
                            int count = r.ReadByte();
                            p.Roster = new List<RosterEntry>(count);
                            for (int i = 0; i < count; i++)
                                p.Roster.Add(new RosterEntry { Id = r.ReadByte(), Role = (PlayerRole)r.ReadByte(), Name = ReadString(r) });
                            break;
                        case PacketType.State:
                            p.State = ReadState(r);
                            break;
                        case PacketType.Bye:
                            break;
                        case PacketType.World:
                            p.World.Seq = r.ReadUInt32();
                            p.World.Part = r.ReadByte();
                            p.World.PartCount = r.ReadByte();
                            var origin = ReadVec(r);
                            int n = r.ReadByte();
                            if (p.World.PartCount > WorldSnapshot.MaxParts || n > WorldSnapshot.MaxEntities) return null;
                            p.World.Entities = new List<EntityState>(n);
                            for (int i = 0; i < n; i++) p.World.Entities.Add(EntityState.Read(r, origin));
                            int nv = r.ReadByte();
                            if (nv > WorldSnapshot.MaxVehicles) return null;
                            p.World.Vehicles = new List<VehicleState>(nv);
                            for (int i = 0; i < nv; i++) p.World.Vehicles.Add(VehicleState.Read(r, origin));
                            int nt = r.ReadByte();
                            if (nt > WorldSnapshot.MaxTrains) return null;
                            p.World.Trains = new List<TrainState>(nt);
                            for (int i = 0; i < nt; i++) p.World.Trains.Add(TrainState.Read(r, origin));
                            break;
                        case PacketType.Appearance:
                            if (!ReadLook(r, out p.Appearance)) return null;
                            break;
                        case PacketType.EntityLook:
                            p.LookId = r.ReadUInt16();
                            if (!ReadLook(r, out p.Appearance)) return null;
                            break;
                        case PacketType.Party:
                            p.Party.Kind = (PartyKind)r.ReadByte();
                            p.Party.PartyId = r.ReadUInt32();
                            int nm = r.ReadByte();
                            p.Party.Members = new List<byte>(ReadExactly(r, nm));
                            p.Party.Amount = r.ReadInt32();
                            break;
                        case PacketType.Hit:
                            p.Hit = new Hit { Seq = r.ReadUInt32(), EntityId = r.ReadUInt16(), Damage = r.ReadInt16(), Bone = r.ReadInt32(), Weapon = r.ReadUInt32() };
                            break;
                        case PacketType.Objectives:
                            int no = r.ReadByte();
                            if (no > Objective.Max) return null;
                            p.Objectives = new List<Objective>(no);
                            for (int i = 0; i < no; i++) p.Objectives.Add(new Objective { Position = ReadVec(r), Radius = Math.Min(Math.Abs(Finite(r.ReadSingle())), 100f) });
                            break;
                        case PacketType.Action:
                            p.Action = new GuestAction { Seq = r.ReadUInt32(), Kind = (ActionKind)r.ReadByte(), EntityId = r.ReadUInt16() };
                            break;
                        case PacketType.Notice:
                            p.Notice.Kind = (NoticeKind)r.ReadByte();
                            int len = r.ReadUInt16();
                            if (len > NoticeMsg.MaxBytes) return null;
                            p.Notice.Text = Encoding.UTF8.GetString(ReadExactly(r, len));
                            break;
                        default:
                            return null;
                    }
                    return p;
                }
            }
            catch (EndOfStreamException)
            {
                return null;
            }
        }

        static byte[] ReadExactly(BinaryReader r, int n)
        {
            var b = r.ReadBytes(n);
            if (b.Length != n) throw new EndOfStreamException();
            return b;
        }

        static void WriteLook(BinaryWriter w, Appearance a)
        {
            var tags = (a.Tags ?? new List<MetaTag>()).Take(Appearance.MaxTags).ToList();
            w.Write(a.Model);
            w.Write((byte)tags.Count);
            foreach (var t in tags)
            {
                w.Write(t.Drawable); w.Write(t.Albedo); w.Write(t.Normal); w.Write(t.Material); w.Write(t.Palette);
                w.Write(t.Tint0); w.Write(t.Tint1); w.Write(t.Tint2);
            }
        }

        static bool ReadLook(BinaryReader r, out Appearance a)
        {
            a = new Appearance { Model = r.ReadUInt32() };
            int n = r.ReadByte();
            if (n > Appearance.MaxTags) return false;
            a.Tags = new List<MetaTag>(n);
            for (int i = 0; i < n; i++)
                a.Tags.Add(new MetaTag
                {
                    Drawable = r.ReadUInt32(), Albedo = r.ReadUInt32(), Normal = r.ReadUInt32(),
                    Material = r.ReadUInt32(), Palette = r.ReadUInt32(),
                    Tint0 = r.ReadByte(), Tint1 = r.ReadByte(), Tint2 = r.ReadByte(),
                });
            return true;
        }

        static void WriteState(BinaryWriter w, PlayerState s)
        {
            w.Write(s.Seq);
            w.Write(s.TimeMs);
            w.Write((byte)s.Flags);
            w.Write(s.PedModel);
            WriteVec(w, s.Position);
            w.Write(s.Heading);
            WriteVec(w, s.Velocity);
            w.Write(s.MountModel);
            WriteVec(w, s.MountPosition);
            w.Write(s.MountHeading);
            w.Write(s.Following);
            w.Write(s.VehicleId);
            w.Write(s.Seat);
            w.Write(s.Weapon);
        }

        static PlayerState ReadState(BinaryReader r)
        {
            var s = new PlayerState
            {
            Seq = r.ReadUInt32(),
            TimeMs = r.ReadInt32(),
            Flags = (StateFlags)r.ReadByte(),
            PedModel = r.ReadUInt32(),
            Position = ReadVec(r),
            Heading = Finite(r.ReadSingle()),
            Velocity = ReadVec(r),
            MountModel = r.ReadUInt32(),
            MountPosition = ReadVec(r),
            MountHeading = Finite(r.ReadSingle()),
            Following = r.ReadByte(),
            VehicleId = r.ReadUInt16(),
            Seat = r.ReadSByte(),
            };
            if (r.BaseStream.Length - r.BaseStream.Position >= 4) s.Weapon = r.ReadUInt32();   // absent from versions before 0.7.1
            return s;
        }

        static void WriteVec(BinaryWriter w, Vec3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        // Positions from the wire: anything not a real map coordinate makes the packet invalid.
        const float WorldLimit = 20000f;
        static Vec3 ReadVec(BinaryReader r) => new Vec3(Finite(r.ReadSingle()), Finite(r.ReadSingle()), Finite(r.ReadSingle()));

        static float Finite(float v) =>
            float.IsNaN(v) || float.IsInfinity(v) || Math.Abs(v) > WorldLimit ? throw new EndOfStreamException("bad number") : v;

        static void WriteString(BinaryWriter w, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            if (bytes.Length > MaxNameLength) Array.Resize(ref bytes, MaxNameLength);
            w.Write((byte)bytes.Length);
            w.Write(bytes);
        }

        static string ReadString(BinaryReader r)
        {
            int n = r.ReadByte();
            var bytes = r.ReadBytes(n);
            if (bytes.Length != n) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
