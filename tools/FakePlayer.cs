// A stand-in second player for solo tests through the relay server.
//   mono build/FakePlayer.exe <server> <port> <password> partner [walk|run|ride|shoot|shoot-people|passenger] [model]
//   mono build/FakePlayer.exe <server> <port> <password> owner
//   mono build/FakePlayer.exe <server> <port> <password> partner passenger record <file>
//   mono build/FakePlayer.exe <server> <port> <password> owner replay <file>
//   mono build/FakePlayer.exe <server> <port> <password> owner party
//   mono build/FakePlayer.exe <server> <port> <password> owner down
//   mono build/FakePlayer.exe <server> <port> <password> partner downed
//   mono build/FakePlayer.exe <server> <port> <password> partner nemeses   (asks the host's enRage, like F11)
// down: the hostile hits you for 40 every 2 s until you're down, keeps
//   "shooting" for 12 s more, then stops (you should get up ~10 s later).
// downed: lies down 3 m from you until you revive it (F7 next to it).
// party: as host, invites everyone to a mission party; once you accept (Y) it
//   repeats a 40 s cycle: an 8 s "cutscene" at 10 s, a 300 m jump at 30 s.
// record: also saves the real player's states and the world they send, with
//   timings; replay: plays such a recording back as the owner (positions as
//   recorded), looping, so you can watch copies follow real motion.
// partner: joins after you and circles you 6 m away; your game runs the area
//   (you are near and joined first), so this tests your game serving NPCs to
//   another player. "shoot" lands a 40-damage hit every 3 s on the nearest
//   living animal (never your horse); "shoot-people" on anyone; "passenger"
//   sits in your passenger seat whenever you drive a wagon or coach; "driver"
//   takes the reins of the nearest wagon/coach in your world (spawn one with
//   F4, stay out of it) and drives it in a 20 m circle at 5 m/s.
// owner: join this BEFORE starting the game. It hosts, stands 8 m from you and
//   runs a fake area: four NPCs walking a circle and a hostile that shoots over
//   your head every 1.5 s and hits you for 10 every 15 s. Your game shows them
//   as copies; shoot them and they die after 100 damage. A stagecoach with a
//   driver and a passenger drives a 25 m circle around you.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using RDR2Coop.Net;

static class FakePlayer
{
    static int Main(string[] args)
    {
        if (args.Length < 4) { Console.WriteLine("usage: FakePlayer <server> <port> <password> partner|owner [mode] [model]"); return 1; }
        string server = args[0], password = args[2], role = args[3];
        int port = int.Parse(args[1]);
        string mode = args.Length > 4 ? args[4] : "walk";
        uint pedModel = ToolUtil.Joaat(args.Length > 5 && args[5] != "record" ? args[5] : "cs_charlessmith");
        bool owner = role == "owner";
        bool party = owner && args.Length > 4 && args[4] == "party";
        bool down = owner && args.Length > 4 && args[4] == "down";
        // owner nemesis: acts as a host whose enRage sends a far-away ambush plan
        // to the other player (who must be a guest: press Insert after this joins).
        bool nemesis = owner && args.Length > 4 && args[4] == "nemesis";
        bool planSent = false;
        long otherDownSince = -1; bool revived = false; Vec3? downAt = null;
        uint partyId = 0; long partyStart = -1; bool invited = false;
        string recordFile = args.Length > 6 && args[5] == "record" ? args[6] : null;
        string replayFile = args.Length > 5 && args[4] == "replay" ? args[5] : null;
        if (replayFile != null) return Replay(server, port, password, replayFile);
        var recorder = recordFile != null ? new System.IO.BinaryWriter(System.IO.File.Create(recordFile)) : null;

        var clock = Stopwatch.StartNew();
        var link = new ServerLink(owner ? "FakeOwner" : "FakePartner", owner, password, server, port);
        byte other = 0;                         // the real game we stand next to
        PlayerState? otherState = null;
        List<EntityState> world = null;
        List<VehicleState> worldVehicles = new List<VehicleState>();
        ushort driveVehicle = 0; Vec3 driveCentre = default; double driveAngle = 0;
        int snapshots = 0;
        var damage = new Dictionary<ushort, int>();

        link.Joined += () =>
        {
            Console.WriteLine($"joined as player {link.MyId}, host {link.HostId}");
            // partner nemeses: ask the host's enRage for our nemeses, like a guest's F11.
            if (!owner && mode == "nemeses") link.SendNotice(link.HostId, NoticeKind.NemesesRequest, "");
        };
        link.RosterChanged += () =>
        {
            Console.WriteLine("roster: " + string.Join(", ", link.Roster.Select(e => $"{e.Id} {e.Name}{(e.Role == PlayerRole.Host ? " (host)" : "")}")));
            if (party && partyId != 0 && partyStart < 0) { invited = false; other = 0; otherState = null; }   // ask again
        };
        link.Disconnected += r => Console.WriteLine("disconnected: " + r);
        link.NoticeReceived += (from, n) => Console.WriteLine($"notice from player {from} ({n.Kind}):\n  " + (n.Text ?? "").Replace("\n", "\n  "));
        link.RosterChanged += () =>
        {
            if (!nemesis || planSent || !link.IAmHost) return;
            var guest = link.Roster.FirstOrDefault(e => e.Id != link.MyId);
            if (guest.Id == 0) return;
            planSent = true;
            link.SendNotice(guest.Id, NoticeKind.Encounter, string.Join("\n",
                "id=fake-nemesis-1", "name=Bill Test Dunn", "model=" + ToolUtil.Joaat("g_m_m_uniduster_01"), "outfit=0",
                "rank=3", "health=600", "accuracy=40", "hand=" + ToolUtil.Joaat("weapon_revolver_cattleman"),
                "long=" + ToolUtil.Joaat("weapon_repeater_carbine"), "crew=2"));
            Console.WriteLine($"sent a far-away ambush plan to {guest.Name}; waiting for the outcome");
        };
        link.StateReceived += (from, s, t) =>
        {
            if (other == 0) Console.WriteLine($"player {from} is at {s.Position}");
            other = from;
            otherState = s;
            if (down && (s.Flags & StateFlags.Downed) != 0 && otherDownSince < 0)
            {
                otherDownSince = t;
                Console.WriteLine("the player is down; the hostile keeps shooting for 12 s, then stops");
            }
            if (down && (s.Flags & StateFlags.Downed) == 0 && otherDownSince >= 0)
            {
                Console.WriteLine($"the player is back up after {(t - otherDownSince) / 1000.0:0.0} s");
                otherDownSince = -1;
            }
            Record(recorder, t, Protocol.State(s));
        };
        link.AppearanceReceived += (from, a) =>
        {
            Console.WriteLine($"player {from}'s look: {a.Tags.Count} pieces");
            Record(recorder, clock.ElapsedMilliseconds, Protocol.AppearancePacket(a));
        };
        link.WorldReceived += (from, w, t) =>
        {
            if (owner) return;
            world = w.Entities;
            worldVehicles = w.Vehicles ?? new List<VehicleState>();
            snapshots++;
            foreach (var pk in Protocol.World(w)) Record(recorder, t, pk);
        };
        link.PartyReceived += (from, m) =>
        {
            Console.WriteLine($"party from player {from}: {m.Kind}");
            if (m.Kind == PartyKind.Revive && m.Members != null && m.Members.Contains(link.MyId))
            {
                revived = true;
                Console.WriteLine($"revived by player {from}");
            }
            if (party && m.Kind == PartyKind.Accept && m.PartyId == partyId)
            {
                link.SendParty(new PartyMsg { Kind = PartyKind.Members, PartyId = partyId, Members = new List<byte> { from } });
                partyStart = clock.ElapsedMilliseconds;
                Console.WriteLine("cycle starts: cutscene at 10-18 s, 300 m jump at 30-40 s");
            }
        };
        link.HitReceived += (from, h) =>
        {
            if (h.EntityId == 0) { Console.WriteLine($"an NPC in player {from}'s game hit me: {h.Damage}"); return; }
            if (!owner) return;
            damage.TryGetValue(h.EntityId, out int d);
            damage[h.EntityId] = d + (h.Bone == Hit.HeadBone ? 1000 : h.Damage);
            Console.WriteLine($"player {from} hit NPC {h.EntityId}: {h.Damage}, bone {h.Bone}" +
                              (damage[h.EntityId] >= 100 ? " -> dead" : $" (total {damage[h.EntityId]})"));
        };
        Console.CancelKeyPress += (o, e) => { e.Cancel = true; recorder?.Dispose(); link.Dispose(); Environment.Exit(0); };

        float speed = mode == "ride" ? 7f : mode == "run" ? 4f : 1.4f;
        uint horseModel = ToolUtil.Joaat("a_c_horse_morgan_bay");
        uint seq = 0, worldSeq = 0;
        double angle = 0, npcAngle = 0;
        long lastReport = 0, lastShot = 0, lastWorld = 0, lastHitOnOther = 0, lastNpcShot = 0;
        byte npcShots = 0;
        Vec3? anchor = null;

        while (true)
        {
            long now = clock.ElapsedMilliseconds;
            link.Poll(now);
            if (link.State == LinkState.Connected)
            {
                var centre = otherState?.Position ?? new Vec3(0, 0, 0);
                var s = new PlayerState { Seq = ++seq, TimeMs = (int)now, PedModel = pedModel };
                if (owner)
                {
                    // Stand still 8 m from where the real player first appeared; run our own area.
                    if (anchor == null && otherState != null) anchor = otherState.Value.Position;
                    var a = anchor ?? centre;
                    s.Position = new Vec3(a.X + 8, a.Y, a.Z);
                    s.Heading = 270;
                    if (party && otherState != null && !invited && link.IAmHost)
                    {
                        invited = true;
                        if (partyId == 0) partyId = (uint)new Random().Next(1, int.MaxValue);
                        link.SendParty(new PartyMsg { Kind = PartyKind.Invite, PartyId = partyId });
                        Console.WriteLine("invited everyone to a mission party");
                    }
                    if (partyStart >= 0)
                    {
                        long t = (now - partyStart) % 40000;
                        if (t >= 10000 && t < 18000) s.Flags |= StateFlags.InCutscene;
                        if (t >= 30000) s.Position = new Vec3(a.X + 300, a.Y, a.Z);
                    }
                }
                else
                {
                    if (mode != "downed") angle += speed / 6.0 * 0.033;
                    s.Position = new Vec3(centre.X + 6f * (float)Math.Cos(angle), centre.Y + 6f * (float)Math.Sin(angle), centre.Z);
                    s.Velocity = new Vec3(-speed * (float)Math.Sin(angle), speed * (float)Math.Cos(angle), 0);
                    s.Heading = (float)((Math.Atan2(s.Velocity.Y, s.Velocity.X) * 180 / Math.PI - 90 + 360) % 360);
                    s.Following = other;   // we share the real game's area
                    if (mode == "downed")
                    {
                        if (downAt == null && otherState != null) downAt = new Vec3(centre.X + 3, centre.Y, centre.Z);
                        s.Position = downAt ?? centre;
                        if (!revived) s.Flags |= StateFlags.Downed;
                        s.Velocity = new Vec3(0, 0, 0);
                    }
                    if (mode == "driver" && world != null && (driveVehicle != 0 || worldVehicles.Count > 0))
                    {
                        if (driveVehicle == 0)
                        {
                            var v = worldVehicles.OrderBy(x => (x.Flags & VehicleFlags.Draft) != 0 ? 0 : 1)
                                                 .ThenBy(x => Vec3.Distance(x.Position, centre)).FirstOrDefault();
                            if (v.Id == 0) goto skip;
                            driveVehicle = v.Id; driveCentre = v.Position; driveAngle = 0;
                            Console.WriteLine($"driving vehicle {v.Id} at {v.Position}");
                        }
                        driveAngle += 5.0 / 20.0 * 0.033;
                        var dc = driveCentre;
                        var pos = new Vec3(dc.X + 20f * (float)Math.Cos(driveAngle) - 20f, dc.Y + 20f * (float)Math.Sin(driveAngle), dc.Z);
                        var vel = new Vec3(-5f * (float)Math.Sin(driveAngle), 5f * (float)Math.Cos(driveAngle), 0);
                        s.Position = pos;
                        s.VehicleId = driveVehicle;
                        s.Seat = -1;
                        s.Flags = StateFlags.Driving;
                        s.MountPosition = pos;
                        s.MountHeading = (float)(Math.Atan2(vel.Y, vel.X) * 180 / Math.PI - 90);
                        s.Velocity = vel;
                    }
                    skip:
                    if (mode == "passenger" && otherState?.VehicleId > 0)
                    {
                        s.VehicleId = otherState.Value.VehicleId;   // the real player's own vehicle
                        s.Seat = 0;
                        s.Position = centre;
                    }
                    else if (mode == "ride")
                    {
                        s.Flags = StateFlags.Mounted;
                        s.MountModel = horseModel;
                        s.MountPosition = s.Position;
                        s.MountHeading = s.Heading;
                    }
                }
                link.SendState(s);

                if (owner && anchor != null && otherState != null && now - lastWorld >= 50)
                {
                    lastWorld = now;
                    var a = anchor.Value;
                    npcAngle += 1.4 / 10.0 * 0.05;
                    var list = new List<EntityState>();
                    for (ushort i = 1; i <= 4; i++)
                    {
                        double t = npcAngle + i * Math.PI / 2;
                        var vel = new Vec3(-1.4f * (float)Math.Sin(t), 1.4f * (float)Math.Cos(t), 0);
                        list.Add(Npc(i, otherState.Value.PedModel, new Vec3(a.X + 10f * (float)Math.Cos(t), a.Y + 10f * (float)Math.Sin(t), a.Z),
                            (float)((Math.Atan2(vel.Y, vel.X) * 180 / Math.PI - 90 + 360) % 360), vel, damage, EntityFlags.None, 0, 0, 0));
                    }
                    bool hostileAlive = (damage.TryGetValue(5, out int hd) ? hd : 0) < 100;
                    bool calm = down && otherDownSince >= 0 && now - otherDownSince > 12000;
                    if (hostileAlive && now - lastNpcShot > 1500) { lastNpcShot = now; npcShots++; }
                    list.Add(Npc(5, otherState.Value.PedModel, new Vec3(a.X, a.Y + 15, a.Z), 180, new Vec3(0, 0, 0), damage,
                        calm ? EntityFlags.Hostile : EntityFlags.Hostile | EntityFlags.Shooting, calm ? (byte)0 : other,
                        ToolUtil.Joaat("weapon_revolver_cattleman"), npcShots));
                    // A stagecoach circling at 4 m/s, NPC 6 driving and NPC 7 riding.
                    double c = npcAngle * 4.0 / 1.4 * 10.0 / 25.0;
                    var cvel = new Vec3(-4f * (float)Math.Sin(c), 4f * (float)Math.Cos(c), 0);
                    float cyaw = (float)(Math.Atan2(cvel.Y, cvel.X) * 180 / Math.PI - 90);
                    var coach = new VehicleState
                    {
                        Id = 100, Model = ToolUtil.Joaat("stagecoach001x"), Flags = VehicleFlags.Draft, HealthPct = 100,
                        Position = new Vec3(a.X + 25f * (float)Math.Cos(c), a.Y + 25f * (float)Math.Sin(c), a.Z),
                        Rotation = new Vec3(0, 0, cyaw), Velocity = cvel,
                    };
                    foreach (var (id, seat) in new[] { ((ushort)6, (sbyte)-1), ((ushort)7, (sbyte)0) })
                    {
                        var p = Npc(id, otherState.Value.PedModel, coach.Position, cyaw, cvel, damage, EntityFlags.None, 0, 0, 0);
                        p.VehicleId = 100;
                        p.Seat = seat;
                        list.Add(p);
                    }
                    link.SendWorld(new WorldSnapshot { Seq = ++worldSeq, Entities = list,
                        Vehicles = new List<VehicleState> { coach } });

                    int every = down ? 2000 : 15000, dmg = down ? 40 : 10;
                    if (hostileAlive && !(down && otherDownSince >= 0) && now - lastHitOnOther > every)
                    {
                        lastHitOnOther = now;
                        link.SendHit(other, 0, (short)dmg, 0);
                        Console.WriteLine($"hostile hit the player for {dmg}");
                    }
                }

                if (!owner && world != null && now - lastReport > 5000)
                {
                    lastReport = now;
                    Console.WriteLine($"world from player {other}: {snapshots} snapshots, {world.Count} NPCs, {world.Count(e => e.Dead)} dead, " +
                        $"vehicles: {string.Join(" ", worldVehicles.Select(v => $"{v.Id}:{v.Model:X8}:{v.Flags}"))}");
                }
                if (!owner && mode.StartsWith("shoot") && world != null && now - lastShot > 3000)
                {
                    lastShot = now;
                    bool people = mode == "shoot-people";
                    var target = world.Where(e => !e.Dead && (e.Flags & EntityFlags.PlayerHorse) == 0 && (people || (e.Flags & EntityFlags.Animal) != 0))
                                      .OrderBy(e => Vec3.Distance(e.Position, centre)).FirstOrDefault();
                    if (target.Id != 0)
                    {
                        link.SendHit(other, target.Id, 40, 0);
                        Console.WriteLine($"shot NPC {target.Id} ({Vec3.Distance(target.Position, centre):0} m away, health {target.HealthPct}%)");
                    }
                }
            }
            Thread.Sleep(33);
        }
    }

    // Recording format: [long ms][int length][packet bytes], repeated.
    static void Record(System.IO.BinaryWriter w, long ms, byte[] packet)
    {
        if (w == null) return;
        w.Write(ms); w.Write(packet.Length); w.Write(packet);
        w.Flush();
    }

    static int Replay(string server, int port, string password, string file)
    {
        var items = new List<(long ms, Packet p)>();
        using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(file)))
            while (r.BaseStream.Position < r.BaseStream.Length)
            {
                long ms = r.ReadInt64(); var bytes = r.ReadBytes(r.ReadInt32());
                var p = Protocol.Read(bytes, bytes.Length);
                if (p != null) items.Add((ms, p));
            }
        if (items.Count == 0) { Console.WriteLine("empty recording"); return 1; }
        long span = items[items.Count - 1].ms - items[0].ms;
        Console.WriteLine($"replaying {items.Count} packets, {span / 1000.0:0.0} s, looping; Ctrl+C to stop");

        var clock = Stopwatch.StartNew();
        var link = new ServerLink("FakeOwner", true, password, server, port);
        link.Joined += () => Console.WriteLine($"joined as player {link.MyId}, host {link.HostId}");
        link.RosterChanged += () => Console.WriteLine("roster: " + string.Join(", ", link.Roster.Select(e => $"{e.Id} {e.Name}{(e.Role == PlayerRole.Host ? " (host)" : "")}")));
        Console.CancelKeyPress += (o, e) => { e.Cancel = true; link.Dispose(); Environment.Exit(0); };
        uint seq = 0, worldSeq = 0;
        int i = 0; long loopStart = 0;
        var parts = new List<WorldSnapshot>();
        while (true)
        {
            long now = clock.ElapsedMilliseconds;
            link.Poll(now);
            if (link.State == LinkState.Connected)
            {
                if (i >= items.Count) { i = 0; loopStart = now; Console.WriteLine("loop"); }
                if (i == 0 && loopStart == 0) loopStart = now;
                while (i < items.Count && items[i].ms - items[0].ms <= now - loopStart)
                {
                    var p = items[i++].p;
                    if (p.Type == PacketType.State)
                    {
                        var st = p.State; st.Seq = ++seq; st.Following = 0;
                        link.SendState(st);
                    }
                    else if (p.Type == PacketType.Appearance) link.SendAppearance(p.Appearance);
                    else if (p.Type == PacketType.World)
                    {
                        // Recorded snapshots were split; send each whole one again.
                        if (p.World.Part == 0) parts.Clear();
                        parts.Add(p.World);
                        if (parts.Count == p.World.PartCount)
                        {
                            var whole = new WorldSnapshot { Seq = ++worldSeq, Entities = new List<EntityState>(),
                                                            Vehicles = new List<VehicleState>(), Trains = new List<TrainState>() };
                            foreach (var x in parts) { whole.Entities.AddRange(x.Entities); whole.Vehicles.AddRange(x.Vehicles); whole.Trains.AddRange(x.Trains); }
                            link.SendWorld(whole);
                        }
                    }
                }
            }
            Thread.Sleep(10);
        }
    }

    static EntityState Npc(ushort id, uint model, Vec3 pos, float heading, Vec3 vel, Dictionary<ushort, int> damage,
                           EntityFlags flags, byte aim, uint weapon, byte shots)
    {
        int d = damage.TryGetValue(id, out int v) ? v : 0;
        bool dead = d >= 100;
        return new EntityState
        {
            Id = id, Model = model, Position = pos, Heading = heading, Velocity = dead ? new Vec3(0, 0, 0) : vel,
            HealthPct = (byte)Math.Max(0, 100 - d), Weapon = weapon, Shots = shots,
            Flags = dead ? (flags & ~EntityFlags.Shooting) | EntityFlags.Dead : flags,
            Aim = dead ? (byte)0 : aim,
        };
    }
}
