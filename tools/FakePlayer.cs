// A stand-in second player for solo tests through the relay server.
//   mono build/FakePlayer.exe <server> <port> <password> partner [walk|run|ride|park|park-stay|shoot|shoot-people|passenger|stance|aim|train|double|carry] [model]
//   mono build/FakePlayer.exe <server> <port> <password> owner
//   mono build/FakePlayer.exe <server> <port> <password> partner passenger record <file>
//   mono build/FakePlayer.exe <server> <port> <password> owner replay <file>
//   mono build/FakePlayer.exe <server> <port> <password> owner party
//   mono build/FakePlayer.exe <server> <port> <password> owner down
//   mono build/FakePlayer.exe <server> <port> <password> partner downed
//   mono build/FakePlayer.exe <server> <port> <password> partner activity
//   mono build/FakePlayer.exe <server> <port> <password> partner nemeses   (asks the host's enRage, like F11)
//   mono build/FakePlayer.exe <server> <port> <password> partner partylist
//   mono build/FakePlayer.exe <server> <port> <password> partner score   (its stats every 5 s; prints yours and the kills your game credits it)
// partylist: a 40 s round for the party list: walks around you with its health
//   falling (100% to 20%) for 10 s, is down for 10 s (the list pops up), in a
//   cutscene for 10 s, then 600 m north ("far away") for 10 s; round again.
//   mono build/FakePlayer.exe <server> <port> <password> owner|partner waypoint
// waypoint: sets a map waypoint 360 m from where you first stood, moves it after
//   15 s and clears it for the last 10 s of every 40 s; it prints
//   the waypoints your game sends. As owner (the host) it's also your GPS route
//   while you have no waypoint of your own; as partner you only see its marker.
//   mono build/FakePlayer.exe <server> <port> <password> partner wear|emote
// wear: stands 3 m from you as you, in your clothes (your look is sent back as its own, so a hat you
//   lose or change goes from it too), and is dry, soaked, soaked and filthy, then bathed, 10 s each.
//   emote: the same twin smokes, drinks, leans, sits by a campfire and sleeps on the ground, 10 s each
//   with 4 s standing between, then walks round you still "smoking" (its stand-in should just walk).
//   Both print what your own game sends: how wet and dirty you are, or the scenario you play.
//   mono build/FakePlayer.exe <server> <port> <password> partner emotes
// emotes: an emote from the mod's emote menu every 6 s, in turn (tools/EmoteMode.cs): one round standing
//   3 m from you, the next walking round you. It prints each, and the emotes your game sends.
//   mono build/FakePlayer.exe <server> <port> <password> partner|owner blast
// blast: every 5 s sets off dynamite (every third a fire bottle) at one spot, 15 m east
//   of where you stand when the first goes off. As partner (it walks round you; your
//   game runs the area) your game makes them owned by its stand-in, with no damage of
//   their own (a real partner's game sends the hurt as hits; this one sends none): NPCs
//   there react and blame it (they should still be thrown), and a fire bottle's fire
//   burns them. As owner your game only shows them: its NPC copies
//   fly but aren't hurt (no "hit NPC" lines here). Walk within 15 m of the spot: they
//   mustn't hurt you, your horse or the stand-in (no "an NPC ... hit me" lines). Your
//   own dynamite shows up here as an Explosion notice.
//   mono build/FakePlayer.exe <server> <port> <password> partner crime     (shoots a townsperson dead every 20 s)
//   mono build/FakePlayer.exe <server> <port> <password> owner law         (says you were seen committing crimes)
// crime: in your area, a headshot every 20 s on the nearest calm person (no animals,
//   nobody fighting). Seen by anyone: it prints the Law notice your game sends it, the
//   lawmen who saw it go after its stand-in (it prints their hits), and YOU stay unwanted.
// law: as host, 15 s after you appear and every 45 s after, tells your game you were
//   seen killing someone / attacking a lawman / killing a lawman where you stand: you
//   should get the wanted level and bounty in your own game.
//   mono build/FakePlayer.exe <server> <port> <password> partner chat      (talks in the co-op chat, see ChatMode)
//   mono build/FakePlayer.exe <server> <port> <password> partner contest [race|duel|hunting|shooting]   (joins the contest you start and plays a bot: tools/ContestBots.cs)
//   mono build/FakePlayer.exe <server> <port> <password> partner ping|money   (pings near you every 5 s | gives you $1 every 10 s and takes your gifts: tools/PingMoneyMode.cs)
//   mono build/FakePlayer.exe <server> <port> <password> partner table <game>   (highcard, poker, blackjack, dice or dominoes: sits at the mod table you open and plays a bot: tools/TableBots.cs)
//   mono build/FakePlayer.exe <server> <port> <password> partner markers
//   mono build/FakePlayer.exe <server> <port> <password> owner markers
//   mono build/FakePlayer.exe <server> <port> <password> owner doors
//   mono build/FakePlayer.exe <server> <port> <password> partner doors
// doors (stand outside by a door, e.g. a shop's): as owner it takes the door on the list nearest you and,
//   10 s each, locks it wide open, locks it shut, then lets it go (your game's own again). As
//   partner, every 12 s it steps up to the nearest shut door your game says it manages and opens
//   it like a partner would: an unlocked one swings open in your game, a locked breakable one is
//   kicked in (not during your story mission). Either way it prints the door lists it gets, and
//   as partner the things your game says broke near it (B lines: smash a crate by it).
//   mono build/FakePlayer.exe <server> <port> <password> partner|owner pause [seconds]
// pause (default 7 s): plays its normal mode (partner: walks round you; owner: hosts and sends NPCs) and,
//   10 s after joining and every 40 s, stops calling Poll and sending anything for that long: exactly
//   what a game in the pause menu does (the link's own thread keeps its seat). Then it carries on with
//   the same link and prints "paused"/"resumed" with its id and roster; a "disconnected" or roster line
//   in between means the server dropped it. Try 7, 15 and 60.
//   mono build/FakePlayer.exe <server> <port> <password> partner table
//   mono build/FakePlayer.exe <server> <port> <password> owner table
// table (partner): stands 2 m from you and plays poker, blackjack, dominoes and five finger fillet in
//   turn, 20 s at a table and 10 s up: its stand-in sits down for each (on nothing, unless you stand
//   it by a chair) and stands up between, and F10 says "(playing poker)" by its name. It prints the
//   game your own game says you play (Activity notices) when you sit at a real table.
// table (owner): a quiet host (no NPCs, no shots) that invites you to a mission party (press Y) and
//   runs party's 40 s cycle. Then sit at a real card table: while you play you stay put, your screen
//   stays on, the table's players stay and co-op's subtitles wait; once you're up the party works again.
// down: the hostile hits you for 40 every 2 s until you're down, keeps
//   "shooting" for 12 s more, then stops (you should get up ~10 s later).
// downed: lies down 3 m from you until you revive it (F7 next to it).
//   Spectate (no mode of its own): "owner down" (joined before your game, so it hosts) and "partner walk" in a
//   second window; once you're down, F9 watches one, F9 again the other, Esc stops; getting up ends it.
// activity: stands 3 m from you and fishes 20 s of every 30: a fishing rod in its hand,
//   its own area meanwhile (as the mod does) and an Activity notice (your game should say
//   "Co-op: FakePartner is fishing"), then puts the rod away. Its stand-in just holds the
//   rod: no aiming it, no gunshots. Your own activities (draw your rod, sit down to
//   fillet) print here as Activity notices.
// party: as host, invites everyone to a mission party; once you accept (Y) it
//   repeats a 40 s cycle: an 8 s "cutscene" at 10 s, a 300 m jump at 30 s.
// partner markers: prints the mission markers your game (the host) shares, then
//   stands on the nearest one at least 30 m from you and asks you over every 15 s
//   (a real guest asks once per arrival), like a guest who got there first: press Y
//   and your game takes you to it.
// owner markers: hosts, invites you to a mission party (accept with Y) and shares
//   three mission markers around where you start: 45 m north, 80 m east, 150 m
//   west. Your map shows them; on one, 30 m or more from it, your game asks it
//   over once each time you get there (it prints AtObjective).
// record: also saves the real player's states and the world they send, with
//   timings; replay: plays such a recording back as the owner (positions as
//   recorded), looping, so you can watch copies follow real motion.
// partner: joins after you and circles you 6 m away ("ride" copies your own
//   horse once you ride; as host it leads and sends 03:00 + a thunderstorm); your game runs the area
//   (you are near and joined first), so this tests your game serving NPCs to
//   another player. "shoot" lands a 40-damage hit every 3 s on the nearest
//   living animal (never your horse); "shoot-people" on anyone; "passenger"
//   sits in your passenger seat whenever you drive a wagon or coach; "driver"
//   takes the reins of the nearest wagon/coach in your world (spawn one with
//   F4, stay out of it) and drives it in a 20 m circle at 5 m/s. "train" drives
//   the train nearest you once it has seen it move (stand on the rails, press
//   your SpawnTrain key: that train rolls at 8 m/s): its stand-in sits in the
//   engine and your train keeps its speed, 10 s ahead at 6 m/s, 5 s stopped,
//   10 s back, 5 s stopped, never past 150 m from you. Every partner mode lists
//   the trains your game sends.
//   "stance" goes round a 22 s cycle of poses, each named in the console as it starts: it walks
//   crouched, jumps twice, climbs 2.5 m up an imaginary ledge (it tries to climb what's next to it,
//   and a second later is put up beside where they are; your world has no ledge there, so it drops),
//   falls back off it (goes limp once, gets up) and walks. Stand at a lake or river: where its
//   circle crosses the water it should swim, not drown or jump about.
//   "aim" holds a revolver and repeats 16 s: 6 s standing still aiming at you, 6 s walking the
//   circle aiming at a point 30 m north of you, a shot every 1.5 s through both
//   (flash and bang only: no bullets), then 4 s with the gun down.
// park: tests their horse waiting where they leave it (ParkTest below); park-stay
//   gets off once for good (its horse should go after 10 minutes).
// double: ride double. On foot, you find it waiting on its horse 3 m from you:
//   press F next to the horse (or your RideDouble key) to sit behind it; it
//   walks a 10 m circle with you for 30 s, gets off (you should be put down
//   too) and is back on its horse 10 s later. On your own horse, it sits behind
//   you for 20 s, gets off for 10 s, and so on.
// carry: walks round you with a new load every 10 s (the console says which): a body, a deer, a
//   bear pelt on the shoulder; then riding with a body, then a deer on the horse (it gets off for
//   10 s: the deer stays on the waiting horse), a bear pelt, a wolf pelt, and junk (should show
//   nothing) on the horse; then nothing.
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
        if (mode == "table" && role == "partner" && args.Length > 5)   // partner table <game>: the mod's tables; plain "table" is the game's own card tables
        {
            if (TableBots.For(args[5]) != null) { TableMode.Game = args[5]; mode = "tables"; }
            else Console.WriteLine($"'{args[5]}' has no table bot (tools/TableBots.cs): the game's own tables, with it as the model");
        }
        int pauseS = mode == "pause" ? (args.Length > 5 ? int.Parse(args[5]) : 7) : 0;
        uint pedModel = ToolUtil.Joaat(args.Length > 5 && args[5] != "record" && mode != "contest" && mode != "tables" && pauseS == 0 ? args[5] : "cs_charlessmith");
        if (mode == "contest" && args.Length > 5) ContestMode.Kind = args[5];   // contest: the fifth argument is the kind, not a model
        bool owner = role == "owner";
        bool markers = args.Length > 4 && args[4] == "markers";
        bool quiet = owner && mode == "table";   // owner table: party's cycle without NPCs
        bool party = owner && args.Length > 4 && (args[4] == "party" || markers || quiet);   // markers: the party without the cycle
        List<Objective> hostMarkers = null;
        Vec3? onMarker = null; bool joinedSaid = false;
        long lastMarkers = -3000, lastAsk = -15000;
        bool down = owner && args.Length > 4 && args[4] == "down";
        // owner nemesis: acts as a host whose enRage sends a far-away ambush plan
        // to the other player (who must be a guest: press Insert after this joins).
        bool nemesis = owner && args.Length > 4 && args[4] == "nemesis";
        bool law = owner && args.Length > 4 && args[4] == "law";
        long nextLaw = -1; int lawCount = 0;
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
        List<TrainState> worldTrains = new List<TrainState>();
        ushort driveVehicle = 0; Vec3 driveCentre = default; double driveAngle = 0;
        // train: the one it drives, its front (learnt from how it moves) and our speed.
        ushort trainId = 0; Vec3? trainFwd = null; Vec3 trainSeenAt = default; long trainSeenMs = 0, lastTrainReport = 0; float trainSpeed = 0;
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
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} roster: " + string.Join(", ", link.Roster.Select(e => $"{e.Id} {e.Name}{(e.Role == PlayerRole.Host ? " (host)" : "")}")));
            if (party && partyId != 0 && partyStart < 0) { invited = false; other = 0; otherState = null; }   // ask again
        };
        link.Disconnected += r => Console.WriteLine("disconnected: " + r);
        string heardWaypoint = null;   // the game repeats its waypoint every 10 s: print changes only
        link.NoticeReceived += (from, n) =>
        {
            if (n.Kind == NoticeKind.Waypoint)
            {
                if (n.Text != heardWaypoint) Console.WriteLine($"{DateTime.Now:HH:mm:ss} player {from}'s waypoint: " + (string.IsNullOrEmpty(n.Text) ? "cleared" : n.Text));
                heardWaypoint = n.Text;
                return;
            }
            if (n.Kind == NoticeKind.MissionStep)   // the host's mission markers, as the mod reads them
            {
                var list = MissionMarkers.Parse(n.Text, 8f);
                if (hostMarkers != null && MissionMarkers.Same(hostMarkers, list)) return;   // the host repeats them every few seconds
                hostMarkers = list;
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} mission markers from player {from}: {list.Count}" +
                                  string.Concat(list.Select(o => $" ({o.Position.X:0},{o.Position.Y:0})")));
                return;
            }
            if (n.Kind == NoticeKind.Emote) { Console.WriteLine($"{DateTime.Now:HH:mm:ss} player {from} emotes: {EmoteList.NameOf(n.Text)}"); return; }
            if (n.Kind == NoticeKind.Contest && mode == "contest") { ContestMode.Heard(link, from, n.Text, clock.ElapsedMilliseconds); return; }
            if (n.Kind == NoticeKind.Table && mode == "tables") { TableMode.Heard(link, from, n.Text, clock.ElapsedMilliseconds); return; }
            if (n.Kind == NoticeKind.Door && mode != "doors") return;   // the area owner's door list comes every 3 s, near a door or not
            Console.WriteLine($"notice from player {from} ({n.Kind}):\n  " + (n.Text ?? "").Replace("\n", "\n  "));
        };
        link.NoticeReceived += (from, n) => DoorsHeard(n);
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
        bool twin = !owner && (mode == "wear" || mode == "emote");
        string lastStep = "", lastYours = "";
        link.AppearanceReceived += (from, a) =>
        {
            Console.WriteLine($"player {from}'s look: {a.Tags.Count} pieces");
            if (twin) link.SendAppearance(a);   // their clothes on us
            Record(recorder, clock.ElapsedMilliseconds, Protocol.AppearancePacket(a));
        };
        link.WorldReceived += (from, w, t) =>
        {
            if (owner) return;
            world = w.Entities;
            worldVehicles = w.Vehicles ?? new List<VehicleState>();
            worldTrains = w.Trains ?? new List<TrainState>();
            snapshots++;
            foreach (var pk in Protocol.World(w)) Record(recorder, t, pk);
        };
        link.PartyReceived += (from, m) =>
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} party from player {from}: {m.Kind}");
            if (m.Kind == PartyKind.Revive && m.Members != null && m.Members.Contains(link.MyId))
            {
                revived = true;
                Console.WriteLine($"revived by player {from}");
            }
            // partner: accept the host's mission party invites, like a guest pressing Y.
            if (!owner && m.Kind == PartyKind.Invite && from == link.HostId)
            {
                link.SendParty(new PartyMsg { Kind = PartyKind.Accept, PartyId = m.PartyId });
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} accepted the mission party invite");
            }
            if (party && m.Kind == PartyKind.Accept && m.PartyId == partyId)
            {
                link.SendParty(new PartyMsg { Kind = PartyKind.Members, PartyId = partyId, Members = new List<byte> { from } });
                partyStart = clock.ElapsedMilliseconds;
                Console.WriteLine(markers ? "you're in the party: stand on a marker 30 m or more from me"
                                          : "cycle starts: cutscene at 10-18 s, 300 m jump at 30-40 s");
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

        float speed = mode == "ride" ? 7f : mode == "run" ? 4f : mode == "tables" ? 0f : 1.4f;   // at a table it stands still
        uint horseModel = ToolUtil.Joaat("a_c_horse_morgan_bay");
        var dbl = mode == "double" ? new DoubleRide() : null;
        int fishing = -1;   // activity: 1 rod in hand, 0 put away (as last announced)
        // ride: copy the real player's own horse (breed, saddle, coat) once they send its look,
        // and send that look back as ours, so they see their own horse under us.
        Appearance? theirHorse = null;
        long lastHorseLook = 0, lastClock = 0;
        link.EntityLookReceived += (from, id, a) =>
        {
            if (id != 0 || theirHorse.HasValue) return;
            theirHorse = a;
            horseModel = a.Model;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} their horse's look: {a.Tags.Count} pieces (copying it)");
        };
        uint seq = 0, worldSeq = 0;
        double angle = 0, npcAngle = 0;
        long lastReport = 0, lastShot = 0, lastWorld = 0, lastHitOnOther = 0, lastNpcShot = 0;
        byte npcShots = 0, aimShots = 0;
        long lastAimShot = 0; int aimPhase = -1;
        Vec3? anchor = null, waypointBase = null;
        var park = mode.StartsWith("park") ? new ParkTest(mode == "park-stay") : null;
        string stancePhase = "", waypoint = null;
        long lastWaypointSent = 0;
        long lastBlast = 0; int blasts = 0; Vec3? blastAt = null;
        long lastScore = 0; int scoreRounds = 0;
        string tableSent = null, lastTable = ""; long tableSentMs = 0;
        int carryLoad = -1;
        long pausedAt = -1, lastPauseEnd = -30000;
        bool justResumed = false;

        while (true)
        {
            long now = clock.ElapsedMilliseconds;
            if (pauseS > 0 && link.State == LinkState.Connected)
            {
                if (pausedAt < 0 && now - lastPauseEnd > 40000)
                {
                    pausedAt = now;
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss} paused {pauseS} s: no Poll, nothing sent");
                }
                if (pausedAt >= 0 && now - pausedAt < pauseS * 1000L) { Thread.Sleep(33); continue; }
                if (pausedAt >= 0)
                {
                    pausedAt = -1;
                    lastPauseEnd = now;
                    justResumed = true;
                }
            }
            link.Poll(now);
            if (justResumed)   // after that Poll: a drop or a host change only shows once the backlog is read
            {
                justResumed = false;
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} resumed: id {link.MyId}, host {link.HostId}, {link.Roster.Count} in");
            }
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
                    if (partyStart >= 0 && !markers)
                    {
                        long t = (now - partyStart) % 40000;
                        if (t >= 10000 && t < 18000) s.Flags |= StateFlags.InCutscene;
                        if (t >= 30000) s.Position = new Vec3(a.X + 300, a.Y, a.Z);
                    }
                }
                else
                {
                    long aimT = now % 16000;   // aim: 0-6 s stands aiming at you, 6-12 s walks aiming north, then gun down
                    if (mode != "downed" && !(mode == "aim" && aimT < 6000)) angle += speed / 6.0 * 0.033;
                    s.Position = new Vec3(centre.X + 6f * (float)Math.Cos(angle), centre.Y + 6f * (float)Math.Sin(angle), centre.Z);
                    s.Velocity = new Vec3(-speed * (float)Math.Sin(angle), speed * (float)Math.Cos(angle), 0);
                    s.Heading = (float)((Math.Atan2(s.Velocity.Y, s.Velocity.X) * 180 / Math.PI - 90 + 360) % 360);
                    // As host we lead (our own "area": the real game then shows our clock and weather).
                    s.Following = link.IAmHost ? (byte)0 : other;   // else we share the real game's area
                    if (mode == "activity" && otherState != null)
                    {
                        if (downAt == null) downAt = new Vec3(centre.X + 3, centre.Y, centre.Z);
                        s.Position = downAt.Value;   // standing still, facing you
                        s.Velocity = new Vec3(0, 0, 0);
                        s.Heading = 90;
                        int rod = now % 30000 < 20000 ? 1 : 0;
                        if (rod == 1) { s.Weapon = ToolUtil.Joaat("weapon_fishingrod"); s.Following = 0; }   // busy: its own area, as the mod does
                        if (rod != fishing)
                        {
                            fishing = rod;
                            link.SendNotice(Protocol.Broadcast, NoticeKind.Activity, fishing == 1 ? "is fishing" : "");
                            Console.WriteLine($"{DateTime.Now:HH:mm:ss} " + (fishing == 1 ? "fishing: rod in hand" : "rod put away"));
                        }
                    }
                    if (mode == "emotes" && otherState != null) EmoteMode.Tick(link, ref s, centre, now);
                    if (mode == "downed")
                    {
                        if (downAt == null && otherState != null) downAt = new Vec3(centre.X + 3, centre.Y, centre.Z);
                        s.Position = downAt ?? centre;
                        if (!revived) s.Flags |= StateFlags.Downed;
                        s.Velocity = new Vec3(0, 0, 0);
                    }
                    if (mode == "partylist")
                    {
                        long t = now % 40000;
                        s.Health = (byte)(255 - t * 204 / 40000);
                        if (t >= 10000 && t < 20000)
                        {
                            downAt = downAt ?? s.Position;   // lies where it went down
                            s.Position = downAt.Value;
                            s.Velocity = new Vec3(0, 0, 0);
                            s.Flags |= StateFlags.Downed;
                        }
                        else downAt = null;
                        if (t >= 20000 && t < 30000) s.Flags |= StateFlags.InCutscene;
                        if (t >= 30000) { s.Position = new Vec3(centre.X, centre.Y + 600f, centre.Z); s.Following = 0; }   // its own area out there
                    }
                    if (mode == "stance")
                    {
                        var (phase, pose, walk, up) = StanceAt(now % 22000);
                        speed = walk;
                        s.Pose = pose;
                        s.Position = s.Position + new Vec3(0, 0, up);
                        if (phase != stancePhase) { stancePhase = phase; Console.WriteLine($"{DateTime.Now:HH:mm:ss} stance: {phase}"); }
                    }
                    if (mode == "aim")
                    {
                        int phase = aimT < 6000 ? 0 : aimT < 12000 ? 1 : 2;
                        if (phase != aimPhase)
                        {
                            aimPhase = phase;
                            Console.WriteLine($"{DateTime.Now:HH:mm:ss} " + new[] { "standing, aiming at you", "walking, aiming 30 m north of you", "gun down" }[phase]);
                        }
                        s.Weapon = ToolUtil.Joaat("weapon_revolver_cattleman");
                        if (phase == 0) s.Velocity = new Vec3(0, 0, 0);
                        if (phase < 2)
                        {
                            s.Pose = PoseFlags.Aiming;
                            s.AimAt = phase == 0 ? new Vec3(centre.X, centre.Y, centre.Z + 0.5f) : new Vec3(centre.X, centre.Y + 30, centre.Z + 1);
                            if (now - lastAimShot > 1500) { lastAimShot = now; aimShots++; }
                        }
                        s.Shots = aimShots;
                    }
                    if (twin && otherState != null)
                    {
                        var o = otherState.Value;
                        s.PedModel = o.PedModel;   // your model, so your look fits
                        if (downAt == null) downAt = new Vec3(centre.X + 3, centre.Y, centre.Z);
                        string step;
                        bool walk = false;
                        if (mode == "wear")
                        {
                            var w = WearSteps[(int)(now / 10000 % WearSteps.Length)];
                            s.Wet = w.wet; s.Dirt = w.dirt; step = w.name;
                        }
                        else
                        {
                            int i = (int)(now / 14000 % (EmoteSteps.Length + 1));
                            bool on = now % 14000 < 10000;
                            walk = i == EmoteSteps.Length;
                            s.Scenario = on ? ToolUtil.Joaat(walk ? "WORLD_HUMAN_SMOKE" : EmoteSteps[i]) : 0;
                            step = !on ? "standing" : walk ? "walking round you 'smoking' (it should just walk)" : EmoteSteps[i];
                        }
                        if (!walk) { s.Position = downAt.Value; s.Velocity = new Vec3(0, 0, 0); s.Heading = 90; }   // facing you
                        if (step != lastStep) { lastStep = step; Console.WriteLine($"{DateTime.Now:HH:mm:ss} stand-in: {step}"); }
                        string yours = mode == "wear" ? $"wet {o.Wet * 10 / 255 * 10}%, dirt {o.Dirt * 10 / 255 * 10}%" : $"scenario {o.Scenario:X8}";
                        if (yours != lastYours) { lastYours = yours; Console.WriteLine($"{DateTime.Now:HH:mm:ss} your game sends: {yours}"); }
                    }
                    if (mode == "table" && otherState != null)
                    {
                        if (downAt == null) downAt = new Vec3(centre.X + 2, centre.Y, centre.Z);
                        s.Position = downAt.Value; s.Velocity = new Vec3(0, 0, 0); s.Heading = 90;   // facing you
                        string game = now % 30000 < 20000 ? TableRules.Games[(int)(now / 30000 % TableRules.Games.Length)] : null;
                        if (game != null) s.Following = 0;   // at a table: its own area, as the mod does (Activities.Busy)
                        string say = TableRules.Due(game, ref tableSent, ref tableSentMs, now);
                        if (say != null) link.SendNotice(Protocol.Broadcast, NoticeKind.Activity, say);
                        if (game != lastTable)
                        {
                            lastTable = game;
                            Console.WriteLine($"{DateTime.Now:HH:mm:ss} " + (game != null ? $"at the {game} table: its stand-in should sit, F10 shows it" : "up: its stand-in should stand"));
                        }
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
                    // markers: waits on the host's nearest marker 30 m or more away (a guest's game only asks from there).
                    if (mode == "markers" && onMarker == null && hostMarkers != null)
                        foreach (var o in hostMarkers.Where(o => Vec3.Distance(o.Position, centre) >= 30f).OrderBy(o => Vec3.Distance(o.Position, centre)).Take(1))
                            onMarker = o.Position;
                    if (onMarker != null)
                    {
                        bool hostAway = Vec3.Distance(onMarker.Value, centre) >= 30f;
                        s.Position = onMarker.Value;
                        s.Velocity = new Vec3(0, 0, 0);
                        if (hostAway) s.Following = 0;   // that far off it runs its own area
                        if (hostAway && now - lastAsk >= 15000)
                        {
                            lastAsk = now;
                            link.SendParty(new PartyMsg { Kind = PartyKind.AtObjective });
                            Console.WriteLine($"{DateTime.Now:HH:mm:ss} on your mission marker at ({onMarker.Value.X:0},{onMarker.Value.Y:0}): asked you over (press Y)");
                        }
                        if (!hostAway && !joinedSaid) { joinedSaid = true; Console.WriteLine($"{DateTime.Now:HH:mm:ss} you're here with me at the marker"); }
                    }
                    else if (mode == "passenger" && otherState?.VehicleId > 0)
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
                    else if (park != null && otherState != null) park.Step(ref s, centre, now);
                    else if (mode == "train")
                    {
                        var tr = worldTrains.Where(x => trainId == 0 || x.Id == trainId)
                                            .OrderBy(x => Vec3.Distance(x.Position, centre)).FirstOrDefault();
                        if (tr.Id == 0 && trainId != 0) { Console.WriteLine($"train {trainId} is gone"); trainId = 0; trainFwd = null; trainSeenMs = 0; }
                        if (tr.Id != 0 && now - trainSeenMs >= 500)
                        {
                            // Its front: the way it moves at + speed.
                            var moved = new Vec3(tr.Position.X - trainSeenAt.X, tr.Position.Y - trainSeenAt.Y, tr.Position.Z - trainSeenAt.Z);
                            if (trainSeenMs > 0 && Math.Abs(tr.Speed) > 1f && moved.Length > 0.3f && moved.Length < 50f)
                                trainFwd = moved * (Math.Sign(tr.Speed) / moved.Length);
                            trainSeenAt = tr.Position; trainSeenMs = now;
                        }
                        if (tr.Id != 0 && trainFwd != null)
                        {
                            if (trainId == 0)
                            {
                                trainId = tr.Id; trainSpeed = tr.Speed;   // take over at the speed it goes
                                Console.WriteLine($"{DateTime.Now:HH:mm:ss} driving train {tr.Id} ({tr.Config:X8}, {tr.Cars} cars)");
                            }
                            var f = trainFwd.Value;
                            var off = new Vec3(tr.Position.X - centre.X, tr.Position.Y - centre.Y, 0);
                            long phase = now / 1000 % 30;
                            float want = phase < 10 ? 6f : phase < 15 ? 0f : phase < 25 ? -6f : 0f;
                            if (off.Length > 150f && (off.X * f.X + off.Y * f.Y) * want > 0) want = 0;   // no further than 150 m from you
                            trainSpeed += Math.Max(-0.066f, Math.Min(0.066f, want - trainSpeed));   // ~2 m/s², like an engine
                            s.MountPosition = tr.Position + f * (trainSpeed * 0.2f);   // where our copy would be by now
                            s.Position = s.MountPosition;
                            s.Velocity = f * trainSpeed;
                            s.VehicleId = tr.Id;
                            s.Seat = -1;   // the driving seat; no Driving flag for a train (older hosts would push it like a wagon)
                            if (now - lastTrainReport > 2000)
                            {
                                lastTrainReport = now;
                                Console.WriteLine($"train {tr.Id}: told {trainSpeed:0.0} m/s, goes {tr.Speed:0.0} m/s, {off.Length:0} m from you");
                            }
                        }
                        else if (now - lastTrainReport > 5000)
                        {
                            lastTrainReport = now;
                            Console.WriteLine(tr.Id == 0 ? "no train in your world yet: stand on the rails and press your SpawnTrain key"
                                                         : $"train {tr.Id} ({tr.Config:X8}) seen; waiting for it to move to tell its front");
                        }
                    }
                    else if (dbl != null && otherState != null) s = dbl.Next(s, otherState.Value, other, link.MyId, horseModel, now);
                    else if (mode == "carry")
                    {
                        int n = (int)(now / 10000 % CarryLoads.Length);
                        var load = CarryLoads[n];
                        if (n != carryLoad) { carryLoad = n; Console.WriteLine($"{DateTime.Now:HH:mm:ss} carrying: {load.Name}"); }
                        s.Carrying = load.What;
                        s.CarryKind = load.Kind;
                        if (load.Riding)
                        {
                            s.Flags |= StateFlags.Mounted;
                            s.MountModel = horseModel;
                            s.MountPosition = s.Position;
                            s.MountHeading = s.Heading;
                        }
                    }
                }
                if (mode == "doors" && otherState != null) DoorsDemo(link, owner, other, otherState.Value, ref s, now);
                link.SendState(s);
                if (mode == "waypoint" && otherState != null)
                {
                    if (waypointBase == null) waypointBase = otherState.Value.Position;
                    var b = waypointBase.Value;
                    long t = now % 40000;
                    string wp = t < 15000 ? WaypointNotice.Encode(new Vec3(b.X + 300, b.Y + 200, b.Z))
                              : t < 30000 ? WaypointNotice.Encode(new Vec3(b.X - 250, b.Y + 350, b.Z)) : "";
                    if (wp != waypoint || now - lastWaypointSent > 5000)
                    {
                        if (wp != waypoint) Console.WriteLine($"{DateTime.Now:HH:mm:ss} our waypoint: " + (wp.Length > 0 ? wp : "cleared"));
                        waypoint = wp;
                        lastWaypointSent = now;
                        link.SendNotice(Protocol.Broadcast, NoticeKind.Waypoint, wp);
                    }
                }
                if (mode == "blast" && otherState != null && now - lastBlast > 5000)
                {
                    lastBlast = now;
                    var o = otherState.Value.Position;
                    if (blastAt == null) blastAt = new Vec3(o.X + 15, o.Y, o.Z);
                    int kind = ++blasts % 3 == 0 ? 2 : 25;   // EXP_TAG_MOLOTOV, EXP_TAG_DYNAMITE
                    link.SendNotice(Protocol.Broadcast, NoticeKind.Explosion, Blast.Write(kind, blastAt.Value));
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss} set off {Blast.Name(kind)} at {blastAt.Value}");
                }
                if (mode == "score" && now - lastScore >= 5000)
                {
                    // score: counters that grow every 5 s, so the scoreboard's row for us visibly changes.
                    lastScore = now;
                    scoreRounds++;
                    link.SendNotice(Protocol.Broadcast, NoticeKind.Score, Score.StatsLine(new Dictionary<string, int> {
                        { "kills.animal", scoreRounds }, { "kills.human", scoreRounds / 2 }, { "downs", scoreRounds / 6 }, { "revives", scoreRounds / 4 },
                        { "money.won", scoreRounds * 250 }, { "money.lost", scoreRounds * 100 }, { "race.wins", scoreRounds / 3 }, { "hunt.wins", scoreRounds / 5 } }));
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss} score: sent our stats, round {scoreRounds}");
                }
                if (mode == "ride" && theirHorse.HasValue && now - lastHorseLook > 5000)
                {
                    lastHorseLook = now;
                    link.SendEntityLook(0, theirHorse.Value);
                }
                // As host: 03:00 and a thunderstorm, so the real game (our guest) visibly follows.
                if (!owner && link.IAmHost && now - lastClock > 5000)
                {
                    lastClock = now;
                    uint storm = ToolUtil.Joaat("THUNDERSTORM");
                    link.SendNotice(Protocol.Broadcast, NoticeKind.Clock, $"3 0 0 {storm} {storm} 0");
                }

                if (owner && markers && anchor != null && now - lastMarkers >= 3000)
                {
                    var a = anchor.Value;
                    if (lastMarkers < 0) Console.WriteLine("sharing 3 mission markers: 45 m north, 80 m east, 150 m west of where you started");
                    lastMarkers = now;
                    link.SendNotice(Protocol.Broadcast, NoticeKind.MissionStep, MissionMarkers.Write(new List<Objective>
                    {
                        new Objective { Position = new Vec3(a.X, a.Y + 45, a.Z), Radius = 8 },
                        new Objective { Position = new Vec3(a.X + 80, a.Y, a.Z), Radius = 8 },
                        new Objective { Position = new Vec3(a.X - 150, a.Y, a.Z), Radius = 8 },
                    }));
                }

                if (owner && !quiet && anchor != null && otherState != null && now - lastWorld >= 50)
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

                if (law && otherState != null && link.IAmHost)
                {
                    if (nextLaw < 0) nextLaw = now + 15000;
                    else if (now >= nextLaw)
                    {
                        uint crime = new[] { LawRules.Murder, LawRules.AssaultLaw, LawRules.MurderLaw }[lawCount++ % 3];
                        link.SendNotice(other, NoticeKind.Law, LawNotice.Format(crime, otherState.Value.Position));
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss} told player {other} they were seen {LawRules.Name(crime)}: their game should make them wanted");
                        nextLaw = now + 45000;
                    }
                }

                if (!owner && world != null && now - lastReport > 5000)
                {
                    lastReport = now;
                    Console.WriteLine($"world from player {other}: {snapshots} snapshots, {world.Count} NPCs, {world.Count(e => e.Dead)} dead, " +
                        $"vehicles: {string.Join(" ", worldVehicles.Select(v => $"{v.Id}:{v.Model:X8}:{v.Flags}"))}, " +
                        $"trains: {string.Join(" ", worldTrains.Select(t => $"{t.Id}:{t.Config:X8}:{t.Cars} cars:{t.Speed:0.0} m/s"))}");
                }
                bool crimes = mode == "crime";
                if (!owner && (mode.StartsWith("shoot") || crimes) && world != null && now - lastShot > (crimes ? 20000 : 3000))
                {
                    lastShot = now;
                    bool people = mode == "shoot-people";
                    var target = world.Where(e => !e.Dead && (e.Flags & EntityFlags.PlayerHorse) == 0
                                                  && (crimes ? (e.Flags & (EntityFlags.Animal | EntityFlags.Hostile)) == 0 : people || (e.Flags & EntityFlags.Animal) != 0))
                                      .OrderBy(e => Vec3.Distance(e.Position, centre)).FirstOrDefault();
                    if (target.Id != 0)
                    {
                        link.SendHit(other, target.Id, 40, crimes ? Hit.HeadBone : 0);
                        Console.WriteLine($"shot NPC {target.Id} ({Vec3.Distance(target.Position, centre):0} m away, health {target.HealthPct}%)");
                    }
                }
                if (!owner && mode == "chat" && otherState != null) ChatMode(link, now);
                if (!owner && mode == "contest") ContestMode.Tick(link, now);
                if (!owner && (mode == "ping" || mode == "money")) PingMoneyMode.Tick(link, mode, other, otherState?.Position, now);
                if (!owner && mode == "tables") TableMode.Tick(link, now);
            }
            Thread.Sleep(33);
        }
    }

    // stance mode, t ms into its cycle: what to expect, the pose, walking speed (m/s), metres up.
    static (string, PoseFlags, float, float) StanceAt(long t)
    {
        if (t < 6000) return ("crouch (its character walks crouched)", PoseFlags.Crouching, 1f, 0f);
        if (t < 10000) return ("jump (it jumps twice)", t % 2000 >= 500 && t % 2000 < 1200 ? PoseFlags.Jumping : PoseFlags.None, 1.4f, 0f);
        if (t < 14000) return ("climb (it tries to climb what's next to it; a second after, it's put 2.5 m up beside them and drops)",
                               t >= 10500 && t < 12000 ? PoseFlags.Climbing : PoseFlags.None, 0f, t < 10500 ? 0f : Math.Min(2.5f, (t - 10500) / 600f));
        if (t < 18000) return ("fall (it goes limp once, then gets up)",
                               t < 15000 ? PoseFlags.Falling : t < 16500 ? PoseFlags.Ragdoll : PoseFlags.None, 0f, t < 15000 ? 2.5f * (15000 - t) / 1000f : 0f);
        return ("walk (as normal)", PoseFlags.None, 1.4f, 0f);
    }

    static readonly (string name, byte wet, byte dirt)[] WearSteps =
        { ("dry and clean", 0, 0), ("soaked", 255, 0), ("soaked and filthy", 255, 255), ("bathed: clean, drying by itself", 0, 0) };
    static readonly string[] EmoteSteps =
        { "WORLD_HUMAN_SMOKE", "WORLD_HUMAN_DRINKING", "WORLD_HUMAN_LEAN_BACK_WALL", "WORLD_PLAYER_CAMP_FIRE_SIT", "WORLD_PLAYER_SLEEP_GROUND" };

    // partner chat: answers what you say in the co-op chat (T in game, type, Enter), and every 20 s says the
    // next of these, each a test of what your game should show: a hello; a long line (wraps, cut at 200
    // characters); one full of ~codes~ with a line break and a tab (shown plain, on one line); a burst of 5 at
    // once (only the first 3 show: a flood is capped). Then round again.
    static readonly string[][] ChatTests =
    {
        new[] { "Hello! Press T, type something and press Enter, and I'll answer." },
        new[] { "A long line to show wrapping: " + string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog, ", 6)) },
        new[] { "~COLOR_RED~Not red~s~, and\nthis line break\tand tab are plain spaces." },
        Enumerable.Range(1, 5).Select(i => $"Burst {i} of 5 (only 1 to 3 show)").ToArray(),
    };
    static string _heard;
    static long _lastChat;
    static int _chatStep = -1;

    static void ChatMode(ServerLink link, long now)
    {
        if (_chatStep < 0)
        {
            _chatStep = 0;
            _lastChat = now - 17000;   // the hello in 3 s
            link.NoticeReceived += (from, n) => { if (n.Kind == NoticeKind.Chat) _heard = n.Text; };
        }
        if (_heard != null && now - _lastChat >= 1000)
        {
            link.SendNotice(Protocol.Broadcast, NoticeKind.Chat, "You said: " + _heard);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} chat: answered '{_heard}'");
            _heard = null;
            _lastChat = now;
        }
        else if (now - _lastChat >= 20000)
        {
            foreach (var line in ChatTests[_chatStep % ChatTests.Length]) link.SendNotice(Protocol.Broadcast, NoticeKind.Chat, line);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} chat: sent test {_chatStep % ChatTests.Length + 1} of {ChatTests.Length}");
            _chatStep++;
            _lastChat = now;
        }
    }

    // doors: the real game's latest door list (partner), and the door being shown or opened.
    static List<DoorLine> _doorsHeard = new List<DoorLine>();
    static uint _door;
    static Vec3 _doorAt;
    static long _doorMs = -12000, _doorPhase = -1;
    static bool _doorSent;

    static void DoorsHeard(NoticeMsg n)
    {
        var doors = new List<DoorLine>();
        if (n.Kind == NoticeKind.Door) { Doors.Parse(n.Text, out bool picture, doors, new List<BreakLine>()); if (picture) _doorsHeard = doors; }
    }

    static void DoorsDemo(ServerLink link, bool owner, byte other, PlayerState them, ref PlayerState s, long now)
    {
        if (owner)
        {
            if (now - _doorMs < 1000) return;
            _doorMs = now;
            var near = Doors.Near(new[] { them.Position }, 30f, 1);
            long phase = now / 10000 % 3;
            var lines = new List<string>();
            if (near.Count > 0 && phase < 2) lines.Add(Doors.Line(new DoorLine { Hash = near[0], State = 1, Ratio = phase == 0 ? 1f : 0f, Open = phase == 0 }));
            link.SendNotice(Protocol.Broadcast, NoticeKind.Door, Doors.Join("P", lines));
            if (phase == _doorPhase) return;
            _doorPhase = phase;
            if (near.Count == 0) { Console.WriteLine("no door on the list within 30 m of you: stand by one (a shop's door)"); return; }
            Doors.TryGet(near[0], out var at);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} door {near[0]:X8}, {Vec3.Distance(at, them.Position):0} m from you: " +
                              (phase == 0 ? "locked wide open" : phase == 1 ? "locked shut" : "let go (your game's own again)"));
            return;
        }
        if (_door == 0 && now - _doorMs > 12000)
        {
            _doorMs = now;
            var pick = _doorsHeard.Where(d => !d.Open && (d.State == 0 || d.State == 2))
                                  .OrderBy(d => { Doors.TryGet(d.Hash, out var at); return Vec3.Distance(at, them.Position); }).ToList();
            if (pick.Count == 0) { Console.WriteLine($"{DateTime.Now:HH:mm:ss} no shut door in your game's list ({_doorsHeard.Count} doors) yet"); return; }
            _door = pick[0].Hash;
            Doors.TryGet(_door, out _doorAt);
            _doorSent = false;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} stepping up to door {_door:X8} ({(pick[0].State == 2 ? "locked, breakable: kicking it in" : "unlocked: opening it")}), " +
                              $"{Vec3.Distance(_doorAt, them.Position):0} m from you");
        }
        if (_door == 0) return;
        s.Position = new Vec3(_doorAt.X + 1f, _doorAt.Y, _doorAt.Z);   // where the real game checks for us
        s.Velocity = new Vec3(0, 0, 0);
        if (!_doorSent && now - _doorMs > 1000)
        {
            _doorSent = true;
            link.SendNotice(other, NoticeKind.Door, Doors.Line(new DoorLine { Hash = _door, State = 0, Ratio = 1f, Open = true }));
        }
        if (now - _doorMs > 6000) _door = 0;
    }

    // "carry" mode's loads, 10 s each. A big pelt is a thing (its model); a small or medium pelt on a
    // horse travels as its item hash, as the game keeps it.
    static readonly (string Name, uint What, byte Kind, bool Riding)[] CarryLoads =
    {
        ("a body on the shoulder", ToolUtil.Joaat("g_m_m_uniduster_01"), Carry.Shoulder, false),
        ("a deer on the shoulder", ToolUtil.Joaat("a_c_deer_01"), Carry.Shoulder, false),
        ("a bear pelt on the shoulder", ToolUtil.Joaat("p_cs_pelt_xlarge_bear"), Carry.Shoulder, false),
        ("riding, a body on the horse", ToolUtil.Joaat("g_m_m_uniduster_01"), Carry.Horse, true),
        ("riding, a deer on the horse", ToolUtil.Joaat("a_c_deer_01"), Carry.Horse, true),
        ("got off: the deer stays on the horse while it waits ([Visuals] Horses)", ToolUtil.Joaat("a_c_deer_01"), Carry.Horse, false),
        ("riding, a bear pelt on the horse", ToolUtil.Joaat("p_cs_pelt_xlarge_bear"), Carry.Horse, true),
        ("riding, a wolf pelt on the horse", ToolUtil.Joaat("provision_wolf_fur"), Carry.Horse, true),
        ("riding, junk on the horse (should show nothing)", 0xDEADBEEF, Carry.Horse, true),
        ("nothing", 0, Carry.Nothing, false),
    };

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

// partner park: their horse waits where they leave it. Rides a 20 m circle whose near
// side passes 5 m from where you stood (the far side is 45 m off, past the 40 m name
// range), and every 45 s gets off for 30 s: walks 10 m and back and gets on the same
// horse again; the next time it gets on another breed instead, and the time after it
// runs across the circle and gets on a horse there, 40 m from the one waiting.
sealed class ParkTest
{
    const float R = 20f, RideSpeed = 5f;
    readonly bool _stay;
    readonly uint[] _breeds = { ToolUtil.Joaat("a_c_horse_morgan_bay"), ToolUtil.Joaat("a_c_horse_tennesseewalker_chestnut") };
    Vec3 _centre;
    double _angle;
    int _round = -1, _breed;
    long _roundStart, _lastMs;
    bool _riding = true;
    float _heading;

    public ParkTest(bool stay) { _stay = stay; }

    public void Step(ref PlayerState s, Vec3 you, long now)
    {
        if (_round < 0)
        {
            _round = 0;
            _roundStart = _lastMs = now;
            _centre = new Vec3(you.X + R + 5f, you.Y, you.Z);
            Console.WriteLine("park test: riding a 20 m circle beside you; my name should show over me, and go on the far side (45 m)");
        }
        float dt = (now - _lastMs) / 1000f;
        _lastMs = now;
        long t = now - _roundStart;
        int kind = _round % 3;   // how it gets on next: 0 the same horse, 1 another breed, 2 a horse 40 m away
        if (_riding && t >= 15000)
        {
            _riding = false;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} got off: my horse should stay standing where it stopped" +
                              (_stay ? $" until about {DateTime.Now.AddMinutes(10):HH:mm:ss}, then go" : ""));
        }
        else if (!_riding && !_stay && t >= 45000)
        {
            _riding = true;
            _round++;
            _roundStart = now;
            if (kind == 1) _breed ^= 1;
            if (kind == 2) _angle += Math.PI;
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss ") + (kind == 0 ? "back on: your game should put me on the horse that waited"
                : kind == 1 ? "on another breed: the waiting horse should go and a new one appear under me"
                : "on a horse 40 m from the waiting one: that one should go and a new one appear under me"));
        }

        var d = Dir(_angle);
        if (_riding)
        {
            _angle += RideSpeed / R * dt;
            d = Dir(_angle);
            var along = new Vec3(-d.Y, d.X, 0);
            s.Flags = StateFlags.Mounted;
            s.MountModel = _breeds[_breed];
            s.Position = s.MountPosition = _centre + d * R;
            s.Velocity = along * RideSpeed;
            s.Heading = s.MountHeading = _heading = Heading(along);
            return;
        }
        // On foot beside the horse: 10 m in towards the middle and back, or (before
        // getting on across the circle) over to the far side.
        bool across = kind == 2 && !_stay;
        float span = across ? 2 * R - 3f : 10f, f = 0f;
        bool back = false;
        if (t >= 17000 && t < 27000) f = (t - 17000) / 10000f;
        else if (t >= 27000 && (across || _stay || t < 33000)) f = 1f;
        else if (t >= 33000 && t < 43000) { f = (43000 - t) / 10000f; back = true; }
        bool moving = (t >= 17000 && t < 27000) || back;
        var inward = d * -1f;
        var dir = back ? d : inward;
        s.Position = _centre + d * (R - 1.5f) + inward * (span * f);
        s.Velocity = moving ? dir * (span / 10f) : new Vec3(0, 0, 0);
        if (moving) _heading = Heading(dir);
        s.Heading = _heading;
    }

    static Vec3 Dir(double a) => new Vec3((float)Math.Cos(a), (float)Math.Sin(a), 0);
    static float Heading(Vec3 v) => (float)((Math.Atan2(v.Y, v.X) * 180 / Math.PI - 90 + 360) % 360);
}

// "double" mode: rides double with the real player, both ways round (see the top of this file).
sealed class DoubleRide
{
    enum Phase { Wait, Carry, Off, Behind, Down }
    Phase _phase;
    long _since = -1;
    Vec3 _at, _centre;        // where it stands or waits on its horse; the circle it carries you round
    double _angle;

    public PlayerState Next(PlayerState s, PlayerState you, byte youId, byte me, uint horse, long now)
    {
        long t = now - _since;
        bool onMe = you.RidingWith == me;
        if (_since < 0) Go(Phase.Wait, Beside(you), now, "waiting on my horse: walk up to it and press F (or your RideDouble key)");
        else if (you.Mounted && (_phase == Phase.Wait || _phase == Phase.Off || (_phase == Phase.Down && t > 10000)))
            Go(Phase.Behind, _at, now, "you ride: sitting behind you for 20 s");
        else if (_phase == Phase.Behind && (!you.Mounted || t > 20000)) Go(Phase.Down, Beside(you), now, "off your horse for 10 s");
        else if (_phase == Phase.Down && t > 10000) Go(Phase.Wait, Beside(you), now, "back on my horse: walk up to it and press F");
        else if (_phase == Phase.Wait && onMe)
        {
            _centre = new Vec3(_at.X, _at.Y + 10f, _at.Z);
            _angle = -Math.PI / 2;   // the circle starts where the horse stands
            Go(Phase.Carry, _at, now, "you're behind me: a slow circle for 30 s");
        }
        else if (_phase == Phase.Carry && (!onMe || t > 30000)) Go(Phase.Off, _at, now, "getting off (you should be put down too); back on my horse in 10 s");
        else if (_phase == Phase.Off && t > 10000) Go(Phase.Wait, _at, now, "back on my horse: walk up to it and press F");

        s.Flags = StateFlags.None;
        s.Velocity = new Vec3(0, 0, 0);
        s.Heading = 90;
        if (_phase == Phase.Behind)
        {
            s.Position = you.Position; s.Velocity = you.Velocity; s.Heading = you.Heading;
            return Riding.AsPassenger(s, youId);
        }
        if (_phase == Phase.Carry)
        {
            _angle += 2.0 / 10.0 * 0.033;   // 2 m/s round 10 m, at ~30 states a second
            _at = new Vec3(_centre.X + 10f * (float)Math.Cos(_angle), _centre.Y + 10f * (float)Math.Sin(_angle), _centre.Z);
            s.Velocity = new Vec3(-2f * (float)Math.Sin(_angle), 2f * (float)Math.Cos(_angle), 0);
            s.Heading = (float)((Math.Atan2(s.Velocity.Y, s.Velocity.X) * 180 / Math.PI - 90 + 360) % 360);
        }
        s.Position = _at;
        if (_phase == Phase.Wait || _phase == Phase.Carry)
        {
            s.Flags = StateFlags.Mounted;
            s.MountModel = horse;
            s.MountPosition = _at;
            s.MountHeading = s.Heading;
        }
        return s;
    }

    void Go(Phase p, Vec3 at, long now, string say)
    {
        _phase = p; _at = at; _since = now;
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} {say}");
    }

    static Vec3 Beside(PlayerState you) => new Vec3(you.Position.X + 3f, you.Position.Y, you.Position.Z);
}
