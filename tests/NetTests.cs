// Tests for the network layer, run on Linux with mono (no game needed).
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RDR2Coop.Net;

static class NetTests
{
    static int _failed, _passed;

    static int Main()
    {
        Run("state packet round trip", StateRoundTrip);
        Run("garbage and truncated packets are ignored", RejectsGarbage);
        Run("long names are cut to 32 bytes", LongNames);
        Run("interpolates between snapshots", InterpolatesMidpoint);
        Run("heading blends the short way round", HeadingWrap);
        Run("stale and duplicate snapshots are dropped", DropsStale);
        Run("extrapolation is capped", ExtrapolationCapped);
        Run("mounting snaps instead of blending", MountSnaps);
        Run("ini reader handles the shipped settings file", IniShippedFile);
        Run("world snapshot round trip, compact encoding precision", WorldRoundTrip);
        Run("oversized world snapshot is cut to 48", WorldCapped);
        Run("48 NPCs + 16 vehicles split into packets under the budget", WorldSplitsAcrossPackets);
        Run("far-apart areas split by origin range", WorldFarApartSplits);
        Run("trains round trip with NPCs and vehicles", TrainsRoundTrip);
        Run("party messages round trip", PartyRoundTrip);
        Run("appearance round trip, signature notices changes", AppearanceRoundTrip);
        Run("assembler: lost parts drop the snapshot, order and repeats handled", AssemblerDropsBrokenSnapshots);
        Run("hit round trip", HitRoundTrip);
        Run("objectives + guest actions round trip", ObjectivesActionsRoundTrip);
        Run("bad numbers and oversized counts make a packet invalid", RejectsHostileValues);
        Run("notices round trip, long text cut, oversize rejected", NoticeRoundTrip);
        Run("entity look round trip", EntityLookRoundTrip);
        Run("entity ids stay stable and are forgotten", EntityIdsStable);
        Run("selector: nearest first, riders bring horses, cap holds", SelectorPicks);
        Run("selector: hostiles win ties, far dead bodies dropped", SelectorPriorities);
        Run("world buffer blends positions per entity", WorldBufferBlends);
        Run("world buffer drops entities gone from the newest snapshot", WorldBufferDrops);
        Run("shot counter: new shots across wrap, capped burst", ShotCounter);
        Run("v3 join packets round trip", JoinPackets);
        Run("vehicles + seats round trip, a full snapshot fits one packet", VehiclesRoundTrip);
        Run("vehicle selector: players' first, then carriers, then nearest", VehicleSelectorOrder);
        Run("world buffer blends vehicles incl. rotation", WorldBufferVehicles);
        Run("authority: alone, host first, lowest id, hysteresis", AuthorityBasics);
        Run("authority: chains end on the real owner, never loop", AuthorityChains);
        Run("built-in server answers padded browser queries only", BrowserQuery);
        // The same server tests against relay.py and the server built into the mod.
        foreach (bool builtIn in new[] { false, true })
            using (var server = RelayServer.Start(builtIn))
            {
                string s = builtIn ? "built-in server" : "relay.py";
                Run(s + ": join, roster, first in hosts", () => ServerJoin(server.Port));
                Run(s + ": positions, NPC updates and hits reach the right game", () => ServerRouting(server.Port));
                Run(s + ": wrong password refused", () => ServerWrongPassword(server.Port));
                Run(s + ": alone you stay connected (keepalive)", () => ServerKeepalive(server.Port));
                Run(s + ": host leaves, a guest becomes host", () => ServerHandover(server.Port));
            }

        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("  ok   " + name); }
        catch (Exception e) { _failed++; Console.WriteLine("  FAIL " + name + ": " + e.Message); }
    }

    static void Check(bool ok, string what) { if (!ok) throw new Exception(what); }
    static void Near(float a, float b, string what, float tol = 0.01f) =>
        Check(Math.Abs(a - b) <= tol, $"{what}: {a} vs {b}");

    static PlayerState At(uint seq, float x, float heading = 0, StateFlags flags = StateFlags.None) =>
        new PlayerState { Seq = seq, Position = new Vec3(x, 0, 0), Heading = heading, Flags = flags, PedModel = 7 };

    // ---- protocol ----

    static void StateRoundTrip()
    {
        var s = new PlayerState
        {
            Seq = 42, TimeMs = 123456, Flags = StateFlags.Mounted | StateFlags.InCutscene,
            PedModel = 0xD7F1E7F6, Position = new Vec3(-300.5f, 800.25f, 117f), Heading = 271.5f,
            Velocity = new Vec3(3, -1, 0.5f), MountModel = 0x2B5D4F3C,
            MountPosition = new Vec3(-300.4f, 800.2f, 116f), MountHeading = 270f, Following = 2,
        };
        var bytes = Protocol.State(s);
        var p = Protocol.Read(bytes, bytes.Length);
        Check(p != null && p.Type == PacketType.State, "parsed as state");
        var r = p.State;
        Check(r.Seq == 42 && r.TimeMs == 123456 && r.Flags == s.Flags, "header fields");
        Check(r.PedModel == s.PedModel && r.MountModel == s.MountModel, "models");
        Near(r.Position.Y, 800.25f, "position");
        Near(r.MountHeading, 270f, "mount heading");
        Check(r.Mounted && !r.Dead && r.Following == 2, "flag helpers + following");
    }

    static void RejectsGarbage()
    {
        Check(Protocol.Read(new byte[] { 1, 2, 3, 4, 5, 6 }, 6) == null, "bad magic");
        var bytes = Protocol.State(At(1, 5));
        Check(Protocol.Read(bytes, bytes.Length - 3) == null, "truncated state");
        var hello = Protocol.Hello("Alice", false);
        Check(Protocol.Read(hello, hello.Length - 2) == null, "truncated name");
        var bad = (byte[])hello.Clone();
        bad[4] = 99;
        Check(Protocol.Read(bad, bad.Length) == null, "unknown type");
    }

    static void LongNames()
    {
        var bytes = Protocol.Hello(new string('x', 100), true);
        var p = Protocol.Read(bytes, bytes.Length);
        Check(p.Name.Length == 32 && p.ProtocolVersion == Protocol.Version && p.PreferHost, "name cut, version + preference kept");
    }

    // ---- interpolation ----

    static void InterpolatesMidpoint()
    {
        var i = new Interpolator(delayMs: 100);
        i.Push(At(1, 0), 1000);
        i.Push(At(2, 10), 1050);
        Near(i.Sample(1125).Position.X, 5f, "halfway between snapshots");
        Near(i.Sample(1050).Position.X, 0f, "before first is first");
    }

    static void HeadingWrap()
    {
        Near(Interpolator.LerpAngle(350, 10, 0.5f), 0f, "350 -> 10");
        Near(Interpolator.LerpAngle(10, 350, 0.25f), 5f, "10 -> 350");
        Near(Interpolator.LerpAngle(90, 180, 0.5f), 135f, "plain");
    }

    static void DropsStale()
    {
        var i = new Interpolator();
        Check(i.Push(At(5, 0), 0), "first accepted");
        Check(!i.Push(At(5, 1), 10), "duplicate dropped");
        Check(!i.Push(At(3, 1), 20), "older dropped");
        Check(i.Push(At(6, 1), 30), "newer accepted");
    }

    static void ExtrapolationCapped()
    {
        var i = new Interpolator(delayMs: 100, maxExtrapolateMs: 250);
        var s = At(1, 0);
        s.Velocity = new Vec3(10, 0, 0);
        i.Push(s, 1000);
        Near(i.Sample(1200).Position.X, 1f, "100 ms ahead");
        Near(i.Sample(9000).Position.X, 2.5f, "capped at 250 ms");
    }

    static void MountSnaps()
    {
        var i = new Interpolator(delayMs: 0);
        i.Push(At(1, 0), 0);
        i.Push(At(2, 10, 0, StateFlags.Mounted), 100);
        Check(!i.Sample(40).Mounted && i.Sample(40).Position.X == 0, "early half stays on foot");
        Check(i.Sample(60).Mounted && i.Sample(60).Position.X == 10, "late half is mounted");
    }

    // ---- sessions over loopback ----

    static readonly Stopwatch Clock = Stopwatch.StartNew();

    static bool PumpUntil(Func<bool> done, params ServerLink[] sessions)
    {
        var until = Clock.ElapsedMilliseconds + 3000;
        while (Clock.ElapsedMilliseconds < until)
        {
            foreach (var s in sessions) s.Poll(Clock.ElapsedMilliseconds);
            if (done()) return true;
            Thread.Sleep(5);
        }
        return false;
    }

    // ---- settings ----

    static void IniShippedFile()
    {
        string text = System.IO.File.ReadAllText("RDR2Coop.ini").Replace("Name=Player", "Name=alice");
        foreach (var t in new[] { text, text.Replace("\n", "\r\n") })
        {
            var ini = Ini.Parse(t);
            Check(ini.Get("Coop", "Name", "x") == "alice", "name: " + ini.Get("Coop", "Name", "x"));
            Check(ini.Get("coop", "preferhost", "x") == "No", "case-insensitive keys");
            Check(ini.Get("Coop", "Port", 0) == 27460, "port");
            Check(ini.Get("Coop", "AppearAs", "fallback") == "fallback", "empty value falls back");
            Check(ini.Get("Keys", "ShowStatus", "x") == "F10", "second section");
            Check(ini.Get("Coop", "Missing", "fb") == "fb", "missing key");
        }
    }

    // ---- stage 2 ----

    static EntityState Ent(ushort id, float x, EntityFlags f = EntityFlags.None) =>
        new EntityState { Id = id, Model = 0x53DD98DF, Position = new Vec3(x, 2, 3), Heading = 359.5f,
                          Velocity = new Vec3(1.25f, -2.5f, 0), HealthPct = 80, Flags = f, Weapon = 0xABCD, Aim = 2, Shots = 250, Outfit = 0xFEEDBEEF,
                          Action = EntityAction.Scenario, Scenario = 0xC0FFEE11 };

    // Sends a snapshot through the wire format and reassembles it.
    static WorldSnapshot Wire(WorldSnapshot w, out System.Collections.Generic.List<byte[]> packets)
    {
        packets = Protocol.World(w);
        var asm = new WorldAssembler();
        WorldSnapshot whole = default(WorldSnapshot);
        bool done = false;
        foreach (var pk in packets)
        {
            var p = Protocol.Read(pk, pk.Length);
            Check(p != null && p.Type == PacketType.World, "each part parses");
            done = asm.Add(1, p.World, out whole);
        }
        Check(done, "reassembled");
        return whole;
    }

    static void WorldRoundTrip()
    {
        var list = new System.Collections.Generic.List<EntityState>();
        for (ushort i = 1; i <= 24; i++) list.Add(Ent(i, i));
        list[3] = new EntityState { Id = 4, MountId = 9, Flags = EntityFlags.Dead | EntityFlags.Hostile, Position = new Vec3(3, 2, 3) };
        var w = Wire(new WorldSnapshot { Seq = 77, Entities = list }, out var packets);
        Check(packets.Count == 1 && packets[0].Length < Protocol.WorldPartBudget, "24 NPCs fit one small packet: " + packets[0].Length);
        Check(w.Seq == 77 && w.Entities.Count == 24, "header");
        var e = w.Entities[0];
        Near(e.Position.X, 1, "x", 0.011f); Near(e.Position.Z, 3, "z", 0.011f); Near(e.Heading, 359.5f, "heading");
        Near(e.Velocity.Y, -2.5f, "velocity to 0.25 m/s", 0.13f);
        Check(e.Model == 0x53DD98DF && e.HealthPct == 80 && e.Weapon == 0xABCD && e.Aim == 2 && e.Shots == 250 && e.Outfit == 0xFEEDBEEF
              && e.Action == EntityAction.Scenario && e.Scenario == 0xC0FFEE11, "fields");
        var d = w.Entities[3];
        Check(d.Dead && d.MountId == 9 && (d.Flags & EntityFlags.Hostile) != 0, "flags + mount");
        Check(Protocol.Read(packets[0], packets[0].Length - 5) == null, "truncated world ignored");
    }

    static void WorldCapped()
    {
        var list = new System.Collections.Generic.List<EntityState>();
        for (ushort i = 1; i <= 70; i++) list.Add(Ent(i, i));
        Check(Wire(new WorldSnapshot { Seq = 1, Entities = list }, out _).Entities.Count == 48, "capped at 48");
    }

    static void WorldSplitsAcrossPackets()
    {
        var ents = new System.Collections.Generic.List<EntityState>();
        for (ushort i = 1; i <= 48; i++) ents.Add(Ent(i, i * 2));
        var vehs = new System.Collections.Generic.List<VehicleState>();
        for (ushort i = 100; i < 116; i++) vehs.Add(Veh(i, i));
        var w = Wire(new WorldSnapshot { Seq = 5, Entities = ents, Vehicles = vehs }, out var packets);
        Check(packets.Count == 2, "48 NPCs + 16 vehicles = 2 packets, got " + packets.Count);
        Check(packets.TrueForAll(p => p.Length <= Protocol.WorldPartBudget), "each under the budget: " + string.Join(",", packets.Select(p => p.Length)));
        Check(w.Entities.Count == 48 && w.Vehicles.Count == 16, "everything arrives");
        Near(w.Entities[47].Position.X, 96, "last NPC position", 0.011f);
    }

    static void WorldFarApartSplits()
    {
        // Two players 1.5 km apart: positions can't share one origin.
        var ents = new System.Collections.Generic.List<EntityState> { Ent(1, 0), Ent(2, 1500), Ent(3, 3) };
        var w = Wire(new WorldSnapshot { Seq = 1, Entities = ents }, out var packets);
        Check(packets.Count >= 2, "split by distance");
        Near(w.Entities.Find(e => e.Id == 2).Position.X, 1500, "far NPC exact", 0.011f);
    }

    static void AssemblerDropsBrokenSnapshots()
    {
        var ents = new System.Collections.Generic.List<EntityState>();
        for (ushort i = 1; i <= 48; i++) ents.Add(Ent(i, i));
        var vehs = new System.Collections.Generic.List<VehicleState>();
        for (ushort i = 100; i < 116; i++) vehs.Add(Veh(i, i));
        WorldSnapshot P(uint seq, int part) { var pk = Protocol.World(new WorldSnapshot { Seq = seq, Entities = ents, Vehicles = vehs })[part]; return Protocol.Read(pk, pk.Length).World; }
        var asm = new WorldAssembler();
        Check(!asm.Add(1, P(1, 0), out _), "seq 1 part 0 waits");
        Check(!asm.Add(1, P(2, 1), out _), "seq 2 part 1 replaces the unfinished seq 1");
        Check(!asm.Add(1, P(1, 1), out _), "late part of the dropped seq 1 ignored");
        Check(asm.Add(1, P(2, 0), out var whole) && whole.Seq == 2, "seq 2 completes (parts out of order)");
        Check(!asm.Add(1, P(2, 0), out _), "repeat after completion ignored");
        Check(!asm.Add(2, P(3, 0), out _) && asm.Add(1, P(3, 0), out _) == false, "senders kept apart");
        Check(asm.Add(2, P(3, 1), out _), "sender 2 completes its own");
    }

    static void HitRoundTrip()
    {
        var bytes = Protocol.HitPacket(new Hit { Seq = 5, EntityId = 12, Damage = 37, Bone = Hit.HeadBone, Weapon = 0xDEADBEEF }, 3);
        var p = Protocol.Read(bytes, bytes.Length);
        var h = p.Hit;
        Check(p.To == 3 && h.Seq == 5 && h.EntityId == 12 && h.Damage == 37 && h.Bone == Hit.HeadBone && h.Weapon == 0xDEADBEEF, "hit fields + addressee");
    }

    static void EntityLookRoundTrip()
    {
        var a = new Appearance { Model = 0xAABBCCDD, Tags = new System.Collections.Generic.List<MetaTag> {
            new MetaTag { Drawable = 1, Albedo = 2, Normal = 3, Material = 4, Palette = 5, Tint0 = 6, Tint1 = 7, Tint2 = 8 } } };
        var b = Protocol.EntityLookPacket(321, a);
        var p = Protocol.Read(b, b.Length);
        Check(p.Type == PacketType.EntityLook && p.LookId == 321 && p.Appearance.Model == 0xAABBCCDD && p.Appearance.Tags.Count == 1
              && p.Appearance.Tags[0].Tint2 == 8 && p.Appearance.Signature() == a.Signature(), "id + look");
    }

    static void NoticeRoundTrip()
    {
        var b = Protocol.NoticePacket(new NoticeMsg { Kind = NoticeKind.Nemeses, Text = "Bill Dunn - rank 2\nÉmile - rank 1" }, 3);
        var p = Protocol.Read(b, b.Length);
        Check(p.To == 3 && p.Notice.Kind == NoticeKind.Nemeses && p.Notice.Text == "Bill Dunn - rank 2\nÉmile - rank 1", "fields + utf8");
        var big = Protocol.NoticePacket(new NoticeMsg { Text = new string('x', 5000) }, 255);
        Check(Protocol.Read(big, big.Length).Notice.Text.Length == NoticeMsg.MaxBytes, "cut to the limit when sent");
        var raw = (byte[])b.Clone();
        raw[8] = 0xFF; raw[9] = 0xFF;   // claim 65535 bytes of text
        Check(Protocol.Read(raw, raw.Length) == null, "oversized length rejected");
    }

    static void RejectsHostileValues()
    {
        var nan = Protocol.State(new PlayerState { Position = new Vec3(float.NaN, 0, 0) });
        Check(Protocol.Read(nan, nan.Length) == null, "NaN position rejected");
        var far = Protocol.State(new PlayerState { Position = new Vec3(1e9f, 0, 0) });
        Check(Protocol.Read(far, far.Length) == null, "off-map position rejected");
        var ok = Protocol.State(new PlayerState { Position = new Vec3(-282f, 791f, 118f), Heading = 90f });
        Check(Protocol.Read(ok, ok.Length) != null, "a normal state still reads");

        var many = new System.Collections.Generic.List<EntityState>();
        for (int i = 0; i < WorldSnapshot.MaxEntities + 1; i++) many.Add(new EntityState { Id = (ushort)(i + 1), Model = 1 });
        var big = Protocol.Write(new Packet { Type = PacketType.World, World = new WorldSnapshot { Seq = 1, PartCount = 1, Entities = many } });
        Check(Protocol.Read(big, big.Length) == null, "more NPCs than a snapshot can hold rejected");
        var parts = Protocol.Write(new Packet { Type = PacketType.World, World = new WorldSnapshot { Seq = 1, Part = 0, PartCount = 200 } });
        Check(Protocol.Read(parts, parts.Length) == null, "absurd part count rejected");
    }

    static void ObjectivesActionsRoundTrip()
    {
        var objs = new System.Collections.Generic.List<Objective>();
        for (int i = 0; i < 10; i++) objs.Add(new Objective { Position = new Vec3(i, -i, 100.5f), Radius = 4f + i });
        var bytes = Protocol.ObjectivesPacket(objs);
        var p = Protocol.Read(bytes, bytes.Length);
        Check(p.Type == PacketType.Objectives && p.Objectives.Count == Objective.Max, "capped at 8: " + p.Objectives.Count);
        Check(p.Objectives[3].Position.X == 3 && p.Objectives[3].Position.Z == 100.5f && p.Objectives[3].Radius == 7f, "fields");
        var empty = Protocol.ObjectivesPacket(new System.Collections.Generic.List<Objective>());
        Check(Protocol.Read(empty, empty.Length).Objectives.Count == 0, "empty list clears");

        var a = Protocol.ActionPacket(new GuestAction { Seq = 9, Kind = ActionKind.Hogtie, EntityId = 321 }, 1);
        var q = Protocol.Read(a, a.Length);
        Check(q.To == 1 && q.Action.Seq == 9 && q.Action.Kind == ActionKind.Hogtie && q.Action.EntityId == 321, "action fields + addressee");
    }

    static void EntityIdsStable()
    {
        var ids = new EntityIds();
        ushort a = ids.IdFor(1001), b = ids.IdFor(2002);
        Check(a != b && ids.IdFor(1001) == a, "stable");
        Check(ids.HandleFor(b) == 2002, "reverse lookup");
        ids.KeepOnly(new[] { 2002 });
        Check(!ids.TryGet(1001, out _), "forgotten");
        Check(ids.IdFor(1001) != a && ids.IdFor(2002) == b, "a re-used handle gets a new id");
    }

    static Candidate C(int h, float x, int mount = 0, bool dead = false, bool hostile = false) =>
        new Candidate { Handle = h, Position = new Vec3(x, 0, 0), MountHandle = mount, Dead = dead, Hostile = hostile };

    static void SelectorPicks()
    {
        var all = new[] { C(1, 50), C(2, 10), C(3, 200), C(4, 30, mount: 5), C(5, 31), C(6, 5) };
        var origin = new[] { new Vec3(0, 0, 0) };
        var p = EntitySelector.Pick(all, origin, 24);
        Check(string.Join(",", p) == "6,2,5,4,1", "order: " + string.Join(",", p));
        var capped = EntitySelector.Pick(all, origin, 3);
        Check(string.Join(",", capped) == "6,2,5", "rider+horse pair skipped when one slot is left; the nearer horse alone fits: " + string.Join(",", capped));
        var nearGuest = EntitySelector.Pick(new[] { C(7, 300) }, new[] { new Vec3(0, 0, 0), new Vec3(290, 0, 0) }, 24);
        Check(nearGuest.Count == 1, "near another player counts too");
    }

    static void SelectorPriorities()
    {
        var p = EntitySelector.Pick(new[] { C(1, 20), C(2, 30, hostile: true), C(3, 60, dead: true), C(4, 40, dead: true) },
                                    new[] { new Vec3(0, 0, 0) }, 24);
        Check(string.Join(",", p) == "2,1,4", "got " + string.Join(",", p));
    }

    static WorldSnapshot Snap(uint seq, params EntityState[] es) =>
        new WorldSnapshot { Seq = seq, Entities = new System.Collections.Generic.List<EntityState>(es) };

    static void WorldBufferBlends()
    {
        var b = new WorldBuffer(delayMs: 50);
        b.Push(Snap(1, Ent(1, 0), Ent(2, 100)), 1000);
        b.Push(Snap(2, Ent(1, 10), Ent(2, 120)), 1050);
        var s = b.Sample(1075);
        Near(s.Find(e => e.Id == 1).Position.X, 5, "entity 1 halfway");
        Near(s.Find(e => e.Id == 2).Position.X, 110, "entity 2 halfway");
        Check(!b.Push(Snap(2, Ent(1, 99)), 1100), "stale snapshot refused");
    }

    static void WorldBufferDrops()
    {
        var b = new WorldBuffer(delayMs: 0);
        b.Push(Snap(1, Ent(1, 0), Ent(2, 0)), 1000);
        b.Push(Snap(2, Ent(1, 1)), 1050);
        var s = b.Sample(1060);
        Check(s.Count == 1 && s[0].Id == 1, "entity 2 gone");
        var dead = Ent(1, 1, EntityFlags.Dead);
        b.Push(Snap(3, dead), 1100);
        var d = b.Sample(1500);
        Near(d[0].Position.X, 1, "dead bodies are not extrapolated");
    }

    static void ShotCounter()
    {
        Check(Shots.NewShots(-1, 7) == 0, "first sight fires nothing");
        Check(Shots.NewShots(7, 9) == 2, "two new");
        Check(Shots.NewShots(254, 1) == 3, "wraps at 256");
        Check(Shots.NewShots(10, 10) == 0, "none");
        Check(Shots.NewShots(0, 200) == Shots.MaxBurst, "capped after a long gap");
    }

    // ---- protocol v3 + server ----

    static void JoinPackets()
    {
        var nonce = new byte[16]; for (int i = 0; i < 16; i++) nonce[i] = (byte)i;
        var auth = Protocol.Read(Protocol.Auth(nonce, "pw"), 39);
        Check(auth != null && auth.Type == PacketType.Auth && auth.Mac.Length == 32, "auth carries a 32-byte HMAC");
        using (var h = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes("pw")))
            Check(Convert.ToBase64String(h.ComputeHash(nonce)) == Convert.ToBase64String(auth.Mac), "HMAC of the nonce");
        var roster = Protocol.Write(new Packet { Type = PacketType.Roster, Roster = new System.Collections.Generic.List<RosterEntry> {
            new RosterEntry { Id = 1, Role = PlayerRole.Host, Name = "Alice" }, new RosterEntry { Id = 2, Name = "Friend" } } });
        var r = Protocol.Read(roster, roster.Length).Roster;
        Check(r.Count == 2 && r[0].Role == PlayerRole.Host && r[1].Name == "Friend", "roster");
    }

    static OtherPlayer O(float x, byte following = 0) => new OtherPlayer { Position = new Vec3(x, 0, 0), Following = following };

    static void AuthorityBasics()
    {
        var none = new System.Collections.Generic.Dictionary<byte, OtherPlayer>();
        Check(Authority.Pick(3, new Vec3(0, 0, 0), 1, none, 3) == 3, "alone: my own game");
        var others = new System.Collections.Generic.Dictionary<byte, OtherPlayer> { [1] = O(100), [2] = O(50) };
        Check(Authority.Pick(3, new Vec3(0, 0, 0), 1, others, 3) == 1, "host wins when near");
        Check(Authority.Pick(3, new Vec3(0, 0, 0), 9, others, 3) == 1, "else lowest id");
        others[1] = O(200);
        Check(Authority.Pick(3, new Vec3(0, 0, 0), 1, others, 3) == 2, "host 200 m away: not joined");
        Check(Authority.Pick(3, new Vec3(0, 0, 0), 1, others, 1) == 1, "but kept while under 250 m");
        others[1] = O(300);
        Check(Authority.Pick(3, new Vec3(0, 0, 0), 1, others, 1) == 2, "dropped past 250 m");
        Check(Authority.Pick(1, new Vec3(0, 0, 0), 1, new System.Collections.Generic.Dictionary<byte, OtherPlayer> { [2] = O(10) }, 1) == 1,
              "the host runs its own area with guests near");
    }

    static void AuthorityChains()
    {
        // 3 is near 2 only; 2 shows 1's world. 3 should follow 1.
        var others = new System.Collections.Generic.Dictionary<byte, OtherPlayer> { [2] = O(100, following: 1), [1] = O(400) };
        Check(Authority.Pick(3, new Vec3(0, 0, 0), 9, others, 3) == 1, "follows the chain to the owner");
        // 2 claims to follow me: I run it.
        var loop = new System.Collections.Generic.Dictionary<byte, OtherPlayer> { [2] = O(10, following: 3) };
        Check(Authority.Pick(3, new Vec3(0, 0, 0), 9, loop, 3) == 3, "a chain back to me means I run it");
        var cycle = new System.Collections.Generic.Dictionary<byte, OtherPlayer> { [1] = O(10, following: 2), [2] = O(20, following: 1) };
        Authority.Pick(3, new Vec3(0, 0, 0), 9, cycle, 3);   // must return, not spin
    }

    static void BrowserQuery()
    {
        var r = new Relay("pw", () => 0, null, "George's game");
        var from = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5);
        Check(r.Handle(Relay.QueryMagic.Concat(new byte[20]).ToArray(), from).Count == 0, "short query ignored");
        var q = Relay.QueryMagic.Concat(new byte[Relay.QuerySize - 4]).ToArray();
        var a = r.Handle(q, from);
        Check(a.Count == 1 && a[0].to.Equals(from), "one answer, to the asker");
        var ans = a[0].data;
        Check(ans.Length <= q.Length, "answer no bigger than the question");
        Check(ans[0] == 'R' && ans[3] == 'A' && BitConverter.ToInt32(ans, 4) == Protocol.Version && ans[8] == 0 && ans[9] == 4, "header, version, 0 of 4");
        Check(System.Text.Encoding.UTF8.GetString(ans, 11, ans[10]) == "George's game", "room name");
    }

    sealed class RelayServer : IDisposable
    {
        public int Port;
        Process _p;
        RelayHost _host;
        public static RelayServer Start(bool builtIn)
        {
            int port = new Random().Next(28000, 29000);
            if (builtIn) { var h = new RelayHost(port, "pw", _ => { }); return new RelayServer { Port = h.Port, _host = h }; }
            var psi = new ProcessStartInfo("python3", $"-u server/relay.py --port {port} --password pw")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            var s = new RelayServer { Port = port, _p = Process.Start(psi) };
            s._p.StandardOutput.ReadLine();   // "relay on udp/..."
            return s;
        }
        public void Dispose() { _host?.Dispose(); try { _p?.Kill(); } catch (Exception) { } }
    }

    static ServerLink Link(int port, string name, bool preferHost = false, string password = "pw") =>
        new ServerLink(name, preferHost, password, "127.0.0.1", port);

    static void Bye(params ServerLink[] links)
    {
        foreach (var l in links) l.Dispose();
        Thread.Sleep(100);   // let the server see the Byes before the next test
    }

    static void ServerJoin(int port)
    {
        var a = Link(port, "Alice"); var b = Link(port, "Friend");
        Check(PumpUntil(() => a.State == LinkState.Connected, a), "first joined");
        Check(PumpUntil(() => b.State == LinkState.Connected && a.Roster.Count == 2 && b.Roster.Count == 2, a, b), "both in");
        Check(a.IAmHost && !b.IAmHost && b.HostId == a.MyId, "first in hosts");
        Check(b.NameOf(a.MyId) == "Alice", "names from the roster");
        Bye(a, b);
    }

    static void ServerRouting(int port)
    {
        var a = Link(port, "Host"); var b = Link(port, "G1"); var c = Link(port, "G2");
        Check(PumpUntil(() => a.Roster.Count == 3 && b.State == LinkState.Connected && c.State == LinkState.Connected, a, b, c), "all in");
        var states = new System.Collections.Generic.List<(string, byte)>();
        var worlds = new System.Collections.Generic.List<(string, byte)>();
        var hits = new System.Collections.Generic.List<(string, byte, Hit)>();
        foreach (var (l, n) in new[] { (a, "a"), (b, "b"), (c, "c") })
        {
            l.StateReceived += (from, s, t) => states.Add((n, from));
            l.WorldReceived += (from, w, t) => worlds.Add((n, from));
            l.HitReceived += (from, h) => hits.Add((n, from, h));
        }
        b.SendState(At(1, 5));
        var big = new System.Collections.Generic.List<EntityState>();
        for (ushort i = 1; i <= 48; i++) big.Add(Ent(i, i));
        c.SendWorld(new WorldSnapshot { Seq = 1, Entities = big });   // two packets through the real server
        b.SendHit(c.MyId, 1, 40, 0);         // b shoots an NPC that c's game runs
        a.SendHit(b.MyId, 0, 10, 0);         // an NPC in a's world hits b
        Check(PumpUntil(() => states.Count >= 2 && worlds.Count >= 2 && hits.Count >= 2, a, b, c), "delivered");
        Thread.Sleep(100); foreach (var l in new[] { a, b, c }) l.Poll(Clock.ElapsedMilliseconds);
        Check(states.Count == 2 && states.TrueForAll(x => x.Item2 == b.MyId && x.Item1 != "b"), "state to the two others, from b");
        Check(worlds.Count == 2 && worlds.TrueForAll(x => x.Item2 == c.MyId && x.Item1 != "c"), "whole world from c to the others: " + worlds.Count);
        Check(hits.Count == 2, "each hit once although sent twice: " + hits.Count);
        Check(hits.Exists(x => x.Item1 == "c" && x.Item2 == b.MyId && x.Item3.EntityId == 1), "b's hit reached c");
        Check(hits.Exists(x => x.Item1 == "b" && x.Item2 == a.MyId && x.Item3.Damage == 10), "a's hit reached b");
        Bye(a, b, c);
    }

    static void ServerWrongPassword(int port)
    {
        var x = Link(port, "Intruder", password: "nope");
        string why = null;
        x.Disconnected += r => why = r;
        Check(PumpUntil(() => why != null, x), "refused");
        Check(why.Contains("wrong password") && x.State == LinkState.Closed, why);
        Bye(x);
    }

    static void ServerKeepalive(int port)
    {
        var a = Link(port, "Alone");
        string lost = null;
        a.Disconnected += r => lost = r;
        Check(PumpUntil(() => a.State == LinkState.Connected, a), "joined");
        var until = Clock.ElapsedMilliseconds + 7000;
        while (Clock.ElapsedMilliseconds < until) { a.Poll(Clock.ElapsedMilliseconds); Thread.Sleep(20); }
        Check(lost == null && a.State == LinkState.Connected, "still connected after 7 s alone: " + lost);
        Bye(a);
    }

    static void ServerHandover(int port)
    {
        var a = Link(port, "Host"); var b = Link(port, "G1"); var c = Link(port, "Alice", preferHost: true);
        Check(PumpUntil(() => a.Roster.Count == 3 && c.State == LinkState.Connected, a, b, c), "all in");
        Check(a.IAmHost, "a hosts");
        a.Dispose();
        Check(PumpUntil(() => c.IAmHost && b.HostId == c.MyId && b.Roster.Count == 2, b, c), "the player who prefers hosting took over");
        Bye(b, c);
    }

    // ---- vehicles ----

    static VehicleState Veh(ushort id, float x, float yaw = 90, VehicleFlags f = VehicleFlags.Draft) =>
        new VehicleState { Id = id, Model = 0xABCDEF01, Position = new Vec3(x, 1, 2), Rotation = new Vec3(-5.5f, 3.25f, yaw),
                           Velocity = new Vec3(4, -0.5f, 0), Flags = f, HealthPct = 90 };

    static void VehiclesRoundTrip()
    {
        var ents = new System.Collections.Generic.List<EntityState>();
        for (ushort i = 1; i <= 24; i++) { var e = Ent(i, i); e.VehicleId = 30; e.Seat = -1; ents.Add(e); }
        var vehs = new System.Collections.Generic.List<VehicleState>();
        for (ushort i = 30; i < 38; i++) vehs.Add(Veh(i, i, yaw: 270));
        var w = Wire(new WorldSnapshot { Seq = 9, Entities = ents, Vehicles = vehs }, out var packets);
        Check(packets.Count == 1, "the old 24 + 8 now fit one packet: " + packets[0].Length);
        Check(w.Vehicles.Count == 8 && w.Entities[0].VehicleId == 30 && w.Entities[0].Seat == -1, "counts + seat");
        var v = w.Vehicles[0];
        Check(v.Model == 0xABCDEF01 && v.Flags == VehicleFlags.Draft && v.HealthPct == 90, "vehicle fields");
        Near(v.Rotation.X, -5.5f, "pitch"); Near(v.Rotation.Y, 3.25f, "roll"); Near(v.Rotation.Z, -90f, "yaw wraps to -90");
        Near(v.Velocity.X, 4f, "velocity");
        Near(v.Position.X, 30, "position", 0.011f);
        var sb = Protocol.State(new PlayerState { Seq = 1, VehicleId = 33, Seat = 2 });
        var ps = Protocol.Read(sb, sb.Length).State;
        Check(ps.VehicleId == 33 && ps.Seat == 2, "player seat");
    }

    static VehicleCandidate VC(int h, float x, bool player = false, bool carrier = false) =>
        new VehicleCandidate { Handle = h, Position = new Vec3(x, 0, 0), HasPlayer = player, HasPickedPed = carrier };

    static void VehicleSelectorOrder()
    {
        var pick = VehicleSelector.Pick(new[] { VC(1, 10), VC(2, 100, carrier: true), VC(3, 900, player: true), VC(4, 200), VC(5, 5) },
                                        new[] { new Vec3(0, 0, 0) }, 3);
        Check(string.Join(",", pick) == "3,2,5", "got " + string.Join(",", pick));
    }

    static void WorldBufferVehicles()
    {
        var b = new WorldBuffer(delayMs: 50);
        b.Push(new WorldSnapshot { Seq = 1, Vehicles = new System.Collections.Generic.List<VehicleState> { Veh(30, 0, yaw: 170) } }, 1000);
        b.Push(new WorldSnapshot { Seq = 2, Vehicles = new System.Collections.Generic.List<VehicleState> { Veh(30, 10, yaw: -170) } }, 1050);
        var v = b.SampleVehicles(1075)[0];
        Near(v.Position.X, 5, "halfway");
        Check(Math.Abs(Math.Abs(v.Rotation.Z) - 180) < 0.5f, "yaw blends across 180: " + v.Rotation.Z);
    }

    static void TrainsRoundTrip()
    {
        var w = Wire(new WorldSnapshot { Seq = 3, Entities = new System.Collections.Generic.List<EntityState> { Ent(1, 10) },
            Vehicles = new System.Collections.Generic.List<VehicleState> { Veh(30, 20) },
            Trains = new System.Collections.Generic.List<TrainState> {
                new TrainState { Id = 40, Config = 0x515E31ED, Position = new Vec3(100, 5, 3), Speed = -12.34f, Direction = true, Cars = 7 } } }, out var packets);
        Check(packets.Count == 1 && w.Trains.Count == 1 && w.Entities.Count == 1 && w.Vehicles.Count == 1, "all three kinds");
        var t = w.Trains[0];
        Check(t.Config == 0x515E31ED && t.Direction && t.Cars == 7 && t.Id == 40, "train fields");
        Near(t.Speed, -12.34f, "signed speed"); Near(t.Position.X, 100, "position", 0.011f);
    }

    static void PartyRoundTrip()
    {
        var b = Protocol.PartyPacket(new PartyMsg { Kind = PartyKind.Members, PartyId = 77, Members = new System.Collections.Generic.List<byte> { 2, 4 } });
        var p = Protocol.Read(b, b.Length).Party;
        Check(p.Kind == PartyKind.Members && p.PartyId == 77 && p.Members.Count == 2 && p.Members[1] == 4, "party fields");
        var i = Protocol.PartyPacket(new PartyMsg { Kind = PartyKind.Invite, PartyId = 5 });
        Check(Protocol.Read(i, i.Length).Party.Members.Count == 0, "invite without members");
        var r = Protocol.PartyPacket(new PartyMsg { Kind = PartyKind.Reward, PartyId = 5, Amount = 12550 });
        Check(Protocol.Read(r, r.Length).Party.Amount == 12550, "reward amount");
    }

    static void AppearanceRoundTrip()
    {
        var tags = new System.Collections.Generic.List<MetaTag>();
        for (uint i = 0; i < 40; i++) tags.Add(new MetaTag { Drawable = i, Albedo = i + 1, Normal = i + 2, Material = i + 3, Palette = i + 4, Tint0 = 1, Tint1 = 2, Tint2 = (byte)i });
        var a = new Appearance { Model = 0x0D7114C9, Tags = tags };
        var b = Protocol.AppearancePacket(a);
        Check(b.Length < 1200, "fits a packet: " + b.Length);
        var r = Protocol.Read(b, b.Length).Appearance;
        Check(r.Model == a.Model && r.Tags.Count == 40 && r.Tags[39].Tint2 == 39 && r.Tags[5].Palette == 9, "fields");
        Check(r.Signature() == a.Signature(), "same look, same signature");
        var changed = new System.Collections.Generic.List<MetaTag>(tags);
        changed[3] = new MetaTag { Drawable = 999 };
        Check(new Appearance { Model = a.Model, Tags = changed }.Signature() != a.Signature(), "a changed piece changes it");
    }
}
