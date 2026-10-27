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
        Run("party list: compass, distance, health, states, hostile names", PartyListLines);
        Run("name tags: ~codes~ out, long names cut, smaller further away", NameTagText);
        Run("interpolates between snapshots", InterpolatesMidpoint);
        Run("heading blends the short way round", HeadingWrap);
        Run("stale and duplicate snapshots are dropped", DropsStale);
        Run("extrapolation is capped", ExtrapolationCapped);
        Run("mounting snaps instead of blending", MountSnaps);
        Run("ride double: passengers are sent on foot, only riders carry", RideDoubleRules);
        Run("carrying: where a partner's load goes, odd values show nothing", CarryWhere);
        Run("carrying: a horse's biggest load", CarryBiggest);
        Run("ini reader handles the shipped settings file", IniShippedFile);
        Run("world snapshot round trip, compact encoding precision", WorldRoundTrip);
        Run("oversized world snapshot is cut to 48", WorldCapped);
        Run("48 NPCs + 16 vehicles split into packets under the budget", WorldSplitsAcrossPackets);
        Run("far-apart areas split by origin range", WorldFarApartSplits);
        Run("trains round trip with NPCs and vehicles", TrainsRoundTrip);
        Run("train match: exact wins, else same engine with most cars in place, loose cars none", TrainMatchPicks);
        Run("party messages round trip", PartyRoundTrip);
        Run("an online room is a room, not a feature row", OnlineRoomIsNotExtra);
        Run("party: going to the host asks first, N holds it off, Y goes now", FollowPromptRules);
        Run("appearance round trip, signature notices changes", AppearanceRoundTrip);
        Run("wear: water wets, it dries, stand-ins dressed again only on real changes", WearRules);
        Run("emotes: a stand-in plays the scenario only while its player stands by it", EmoteWant);
        Run("look: same pieces in another order are the same look; a flickering look is sent once", LookStability);
        Run("assembler: lost parts drop the snapshot, order and repeats handled", AssemblerDropsBrokenSnapshots);
        Run("hit round trip", HitRoundTrip);
        Run("objectives + guest actions round trip", ObjectivesActionsRoundTrip);
        Run("mission markers: shown ones from the story table, nearest 8 in table order, sent as a notice", MissionMarkersFromTable);
        Run("bad numbers and oversized counts make a packet invalid", RejectsHostileValues);
        Run("notices round trip, long text cut, oversize rejected", NoticeRoundTrip);
        Run("waypoints: text round trip, clear, bad numbers ignored; the host's followed only without your own", WaypointNotices);
        Run("explosion notices: round trip, foreign kinds and bad numbers refused, bursts capped", ExplosionNotices);
        Run("chat: text cleaned and wrapped, lines shown 10 s, floods capped", ChatRules);
        Run("entity look round trip", EntityLookRoundTrip);
        Run("entity ids stay stable and are forgotten", EntityIdsStable);
        Run("selector: nearest first, riders bring horses, cap holds", SelectorPicks);
        Run("selector: hostiles win ties, far dead bodies dropped", SelectorPriorities);
        Run("world buffer blends positions per entity", WorldBufferBlends);
        Run("world buffer drops entities gone from the newest snapshot", WorldBufferDrops);
        Run("shot counter: new shots across wrap, capped burst", ShotCounter);
        Run("aim: a point along the camera, re-aim only when it moves", AimPoints);
        Run("stance: one jump per jump, near them, one move at a time, then a level check", StanceJumps);
        Run("stance: climbs wait (8 s at most), falls go limp once, breaks start nothing", StanceClimbsAndFalls);
        Run("v3 join packets round trip", JoinPackets);
        Run("vehicles + seats round trip, a full snapshot fits one packet", VehiclesRoundTrip);
        Run("vehicle selector: players' first, then carriers, then nearest", VehicleSelectorOrder);
        Run("world buffer blends vehicles incl. rotation", WorldBufferVehicles);
        Run("authority: alone, host first, lowest id, hysteresis", AuthorityBasics);
        Run("authority: chains end on the real owner, never loop", AuthorityChains);
        Run("authority: a paused (silent) player never owns; a chain ending on one gives you your own area", AuthorityAway);
        Run("stalled tick: positions, snapshots, challenges and hits on us dropped; NPC hits, actions and session packets kept", StalledFilter);
        Run("relay: a repeated Hello gets the same challenge, a later login a new one", RelayRepeatHello);
        Run("relay: Hello-only traffic (a paused game) keeps a player, 11 s of silence drops it", RelayHelloOnly);
        Run("relay: the join log names each player's mod version and build", RelayJoinLogsBuild);
        Run("room list: a refusal shows as its sentence, other replies as they came", ListRefusalText);
        Run("room list: ListServer= that isn't a web address falls back to the default, off turns it off", ListServerSetting);
        Run("authority: why a guest runs its own area is named for the log", AuthorityWhyOwn);
        Run("beta logs: install ids the log store accepts, and new ones that are", LogsInstallIds);
        Run("party down: the game's death only once every partner is down", PartyDownAll);
        Run("copies: a model the game refused is tried again after the wait, logged once a minute", RetryBookWaits);
        Run("hits: a gun's damage and the kill marker pass the owner's check, nothing else", HitDamageChecked);
        Run("relay: a dropped host who prefers hosting gets the role back on rejoin, not on a first join", RelayPreferHostRejoin);
        Run("tables: our game goes out at the start, every 5 s, and as \"\" once at the end", TableNotices);
        Run("tables: partners' games from known names only, forgotten after 15 s or when they leave", TablePartners);
        Run("tables: a stand-in sits once it reaches them, stays put, stands when they get up", TableSits);
        Run("activities: held through a restart, then over; partners' line cleaned", ActivityRules);
        Run("activities: a fishing rod's casts aren't aims or shots", RodShotsLeftOut);
        Run("update check: versions compare figure by figure", UpdateVersions);
        Run("update check: latest.txt read, odd versions refused", UpdateLatestText);
        Run("update check: only the key's own signature passes", UpdateSignature);
        Run("beta logs: consent parsed, addresses and folders scrubbed, tail cut, uploads timed", BetaLogs);
        Run("ini: keys written into any section", IniSet);
        Run("built-in server answers padded browser queries only", BrowserQuery);
        Run("law: crime and lawmen hashes are the game's own names", LawHashes);
        Run("law: who gets charged with what", LawCharges);
        Run("law: notice text round trip, bad text refused", LawNoticeText);
        Run("law: at most 8 crimes a minute taken in", LawGateCaps);
        Run("door list: the doors the game's scripts lock, all on the map", DoorList);
        Run("door notices: round trip, fit one notice, hostile lines dropped", DoorNotices);
        Run("upnp: answers parsed, gateways told apart, only http locations", UpnpReplies);
        Run("browser search: any case, menu rows kept, typed text cleaned", BrowserSearch);
        Run("stats: counters add, share and parse, bad parts dropped", StatsRoundTrip);
        Run("score: kill and stats notices round trip, odd lines dropped, only the area's owner credits", ScoreRules);
        Run("contests: messages parsed, ids checked, odd text refused", ContestMessages);
        Run("contests: the host's run: in, countdown, GO, standings, end, results window, nobody joined", ContestHostRun);
        Run("contests: a player's run: in until GO, finish on a horse, done until seen, win counted, cancel, host leaving", ContestPlayerRun);
        Run("contests: the host takes in only from near, a quit is out (and stays out), cancels go the right way, wins counted under the scoreboard's keys", ContestJoinQuit);
        Run("race: ranked by arrival, distances clamped, bad and forged reports dropped, a minute's grace, 10 minute limit, finish armed, winner named", RaceRules);
        Run("hunting: points by model (never horses, dogs, cats or people), lengths, lists that only grow, forged and odd reports dropped, the time and its grace, winner and biggest", HuntingRules);
        Run("hunting: a hunt through the contest run: kills reported at once and until seen, out of time, the win counted", HuntingRun);
        Run("shooting: params, the same bottles from the same seed, scores clamped and only upward, ties to the first there, over at the time or when all are broken, panel, winner", ShootingRules);
        Run("shooting: a whole contest through the framework: open, in, GO, our own score, a partner's, standings, the end, the win counted", ShootingRun);
        Run("duel: params checked, the faster shot wins (each timed from its own DRAW), early is out, bogus and stranger reports dropped, time, walk-away, no-show", DuelRules);
        Run("duel: our side: cues, early or timed shot said every second, onlookers quiet, verdicts, the fall, a whole run host and player", DuelPlayer);
        Run("spectate: next partner up, round again, far ones skipped, nobody left", SpectateNext);
        Run("ping: round trip, bad and far positions refused, one per player every 2 s, names plain", PingRules);
        Run("give money: give and ack formats, nonce answered once, refunds after 10 s or on leaving, duplicates, floods and odd amounts dropped", GiftRules);
        Run("emotes: list fixed and sane, notices round trip, anything else refused", EmoteNotices);
        Run("tables: messages parsed and written, bad lines, opens, seats and pays refused, settlements checked", TableMessages);
        Run("tables: open, join (range, 4 seats, asked again), cash for the next hand, leave, no seat, host leaving, too few", TableSeats);
        Run("tables: a whole high card hand for 3: cash, deal, secrets to their owner only, turns, a settlement that adds up, paid once", TableHighCardHand);
        Run("tables: a turn runs out after 45 s and the table moves for the seat", TableTurnTimeout);
        Run("tables: a player leaving mid-hand forfeits it and still pays; one leaving the session forfeits too", TableLeaveForfeits);
        Run("tables: hostile lines dropped: forged senders, pays that don't add up, over what was brought, for others, unknown hands", TableHostile);
        Run("liar's dice: bids must raise, ones wild unless bid, the bid list, the lowest raise by default, dice private", LiarsDiceBids);
        Run("liar's dice: Liar! and Spot on, right and wrong, the loser starts, elimination, the winner takes the pot, leavers lose their dice", LiarsDiceCalls);
        Run("liar's dice: the panel, the options, stale dice hidden, hostile states and dice dropped", LiarsDiceView);
        Run("liar's dice: a whole game for 3 at a table, dice to their owner only, a settlement that adds up", TableLiarsDiceGame);
        Run("high card: deal, turns, show only, ties split to the cent, forfeits, view parses defensively", HighCardRules);
        Run("dominoes board: matching ends, the first double spins, arms only once both its sides are taken, doubles count both halves", DominoBoardRules);
        Run("dominoes: deals, legal plays, scoring, draw and pass, blocked, going out rounded, next round, 100 wins, forfeits, settlement adds up", DominoesRules);
        Run("dominoes: whole random games for 2-4 agree with the views, end at 100 and add up to zero", DominoesRandomGames);
        Run("dominoes: the view's moves and panel, stale tiles held back, hostile state refused", DominoesView);
        Run("dominoes: a whole game at a table: both play from their own view, paid once at the end", DominoesAtTable);
        Run("blackjack: totals with aces (soft ones), blackjack beats 21, a bust loses even to a dealer's bust", BlackjackHands);
        Run("blackjack: bets, the deal with the hole card down, turns, hit, stand, double, the dealer stands on 17, 1:1 payouts that add up to zero", BlackjackRound);
        Run("blackjack: 3:2, sitting out, the dealer's blackjack, soft 17 stands, 16 draws, the house's cap, leaving stands, the timer, the view", BlackjackRules);
        Run("blackjack: the shoe reshuffles under a quarter; 300 random rounds all add up to zero within what each brought", BlackjackShoe);
        Run("blackjack: at a table: each seat's cash reaches it alone, only the bets it covers, a round paid", BlackjackAtTable);
        Run("poker: all nine hand ranks, best five of seven, kickers and ties", PokerRanks);
        Run("poker: blinds, the button, heads-up order, check/call/bet/raise, the min raise, short all-ins, the stack cap", PokerBetting);
        Run("poker: side pots for 3 and 4, ties split to the cent, settlements add up to zero", PokerPots);
        Run("poker: a player leaving folds, the board runs out, the timer checks or folds", PokerLeaving);
        Run("poker: a hand at a table: secrets to their owner, panel, moves, all in, paid", PokerTable);
        Run("poker: the view drops hostile state and secrets, counts each hand once", PokerView);
        Run("poker: 2000 random hands of random offered moves end and add up", PokerFuzz);
        // The same server tests against relay.py and the server built into the mod.
        foreach (bool builtIn in new[] { false, true })
            using (var server = RelayServer.Start(builtIn))
            {
                string s = builtIn ? "built-in server" : "relay.py";
                Run(s + ": join, roster, first in hosts", () => ServerJoin(server.Port));
                Run(s + ": positions, NPC updates and hits reach the right game", () => ServerRouting(server.Port));
                Run(s + ": wrong password refused", () => ServerWrongPassword(server.Port));
                Run(s + ": alone you stay connected (keepalive); a server that forgot you is rejoined cleanly", () => ServerKeepalive(server));
                Run(s + ": host leaves, a guest becomes host", () => ServerHandover(server.Port));
                Run(s + ": a game whose tick stalls 12 s keeps its seat, id and host role; its backlog stays small", () => ServerPause(server.Port));
            }

        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("  ok   " + name); }
        catch (Exception e) { _failed++; Console.WriteLine("  FAIL " + name + ": " + e.Message); }
    }

    static void UpnpReplies()
    {
        var gw = Upnp.ParseReply("HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=1800\r\nST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n" +
                                 "USN: uuid:1::urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\nLocation: http://10.0.0.1:5000/rootDesc.xml\r\n\r\n");
        Check(gw.location == "http://10.0.0.1:5000/rootDesc.xml" && gw.gateway, "a gateway's answer");
        var tv = Upnp.ParseReply("HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\nUSN: uuid:2::upnp:rootdevice\r\nLOCATION: http://10.0.0.20:8080/desc.xml\r\n\r\n");
        Check(tv.location == "http://10.0.0.20:8080/desc.xml" && !tv.gateway, "another device's answer");
        Check(Upnp.ParseReply("HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\nLOCATION: file:///etc/passwd\r\n\r\n").location == null, "only http locations");
        Check(Upnp.ParseReply("garbage").location == null, "garbage");
    }

    static void BrowserSearch()
    {
        var rooms = new System.Collections.Generic.List<Room> { new Room { Name = "Host your own game here", HostHere = true }, new Room { Name = "Arthur's Posse" }, new Room { Name = "ONB1 (dedicated)" } };
        Check(RoomList.Filter(rooms, "").Count == 3, "empty shows all");
        var found = RoomList.Filter(rooms, "posse");
        Check(found.Count == 2 && found[0].HostHere && found[1].Name == "Arthur's Posse", "any case, menu rows kept");
        Check(RoomList.Filter(rooms, "zzz").Count == 1, "no match leaves the menu rows");
        rooms.Add(new Room { Name = "Play cards here", Extra = 0 });
        Check(RoomList.Filter(rooms, "zzz").Count == 2, "a feature's row stays too");
        Check(RoomList.CleanSearch("  ~r~Posse\n ", 30) == "rPosse", "game codes and control characters out");
        Check(RoomList.CleanSearch(new string('a', 40), 30).Length == 30, "capped");
        Check(RoomList.CleanSearch(null, 30) == "", "nothing typed");
    }

    static void EmoteNotices()
    {
        // Indexes travel between versions: the list only ever grows at the end.
        string order = string.Join(",", EmoteList.All.Select(e => e.Label));
        Check(order == "Wave,Tip your hat,Point,Come here,Clap,Laugh,Yes (nod),No (wave it off)", "the list's order: " + order);
        Check(EmoteList.All.Select(e => e.Label).Distinct().Count() == EmoteList.All.Length, "labels unique");
        foreach (var e in EmoteList.All)
            Check(e.Label.Length > 0 && e.Label.Length < 40 && e.Dict.Length > 0 && e.Clip.Length > 0 && (e.Ms == -1 || e.Ms > 0)
                  && (e.Dict + e.Clip).All(c => char.IsLetterOrDigit(c) || c == '_' || c == '@'), "a sane entry: " + e.Label);
        Check(EmoteList.Flags == 24, "upper body + secondary");
        for (int i = 0; i < EmoteList.All.Length; i++) Check(EmoteList.Parse(EmoteList.Line(i)) == i, "round trip " + i);
        Check(EmoteList.Line(3) == "play 3" && EmoteList.Parse("  play   3 ") == 3, "the wire form");
        foreach (var bad in new[] { null, "", "play", "play -1", "play +1", "play " + EmoteList.All.Length, "play 99999999999", "play 1 2",
                                    "play x", "PLAY 1", "stop 1", "play 1.0", "play script_common@wave@male@unapproved" })
            Check(EmoteList.Parse(bad) == -1, "refused: " + (bad ?? "null"));
        Check(EmoteList.NameOf("play 0") == "Wave" && EmoteList.NameOf("dance").StartsWith("(not an emote"), "names for the console");
    }

    static void StatsRoundTrip()
    {
        Stats.Clear();
        Stats.Add("kills.animal", 2); Stats.Add("kills.animal"); Stats.Add("race.wins"); Stats.Add("", 5); Stats.Add("downs", 0);
        Check(Stats.Get("kills.animal") == 3 && Stats.Get("race.wins") == 1 && Stats.Get("downs") == 0, "counts");
        string wire = Stats.Write(Stats.All());
        Check(wire == "kills.animal=3 race.wins=1", "shared as " + wire);
        var back = Stats.Parse(wire + " bad-key=1 x=-4 y=abc money.won=1500");
        Check(back.Count == 3 && back["kills.animal"] == 3 && back["money.won"] == 1500 && !back.ContainsKey("x"), "parsed, bad parts dropped");
        Stats.Clear();
        Check(Stats.All().Count == 0, "cleared");
    }

    static void ScoreRules()
    {
        Check(Score.KillLine(true, 0x1234u) == "kill human 4660" && Score.KillLine(false, 7) == "kill animal 7", "kill lines");
        var kills = Score.ParseKills("kill human 4660\nkill animal 7\nkill robot 1\nkill human x\nkill human 1 2\n\nstats a=1");
        Check(kills.Count == 2 && kills[0].human && kills[0].model == 4660 && !kills[1].human && kills[1].model == 7, "kills parsed, odd lines dropped");
        Check(Score.ParseKills(string.Join("\n", Enumerable.Repeat("kill animal 1", 30))).Count == Score.MaxKills, "one notice credits 20 at most");
        Check(Score.ParseKills(null).Count == 0 && Score.ParseKills("").Count == 0, "nothing");
        var stats = Stats.Parse("kills.animal=3 money.won=1500");
        string line = Score.StatsLine(stats);
        Check(line == "stats kills.animal=3 money.won=1500", "stats line: " + line);
        var back = Score.ParseStats(line + " bad-key=1");
        Check(back != null && back.Count == 2 && back["money.won"] == 1500, "stats parsed, bad part dropped");
        Check(Score.ParseStats("kill human 1") == null && Score.ParseStats(null) == null && Score.ParseStats("statsx a=1") == null, "not stats");
        // Only the game that runs the area we're in may credit us: never another player, never ourselves, never before an area has an owner.
        Check(Score.MayCredit(2, 2, 1), "the area's owner");
        Check(!Score.MayCredit(3, 2, 1) && !Score.MayCredit(1, 1, 1) && !Score.MayCredit(0, 0, 1), "nobody else");
        var lines = Score.Lines(new[] { ((byte)2, "Dutch"), ((byte)1, "Arthur") }, 1, id => id == 1 ? stats : null);
        Check(lines.Count == 4 && lines[2].text == "Arthur: 0/3 | 0 | 0 | $15.00/$0.00 | 0/0/0/0" && lines[2].tone == 1
              && lines[3].text == "Dutch: no stats yet" && lines[3].tone == 0, "panel lines, ours first: " + lines[2].text);
    }

    // ---- pings and giving money ----
    static void PingRules()
    {
        var me = new Vec3(100, 200, 50);
        string text = PingMsg.Write(new Vec3(120.04f, -350.56f, 61f));
        Check(text == "at 120.0 -350.6 61.0", "written: " + text);
        Check(PingMsg.Read(text, me, out var at) && Math.Abs(at.X - 120f) < 0.01f && Math.Abs(at.Y + 350.6f) < 0.01f && at.Z == 61f, "read back");
        Check(PingMsg.Read("at 1 2 3 newer fields", me, out at) && at.X == 1f, "fields after z are left for newer versions");
        foreach (var bad in new[] { null, "", "at", "at 1 2", "to 1 2 3", "at x 2 3", "at NaN 2 3", "at Infinity 2 3", "at 1 2 30000", "at 1e9 2 3", " at 1 2 3" })
            Check(!PingMsg.Read(bad, me, out _), "refused: " + bad);
        Check(!PingMsg.Read("at 19000 19000 0", new Vec3(-19000, -19000, 0), out _), "more than 20 km from us refused");
        Check(PingMsg.Line("Ar~r~thur<b>", 119.6f) == "Arrthurb: look here (120 m)" && PingMsg.Line(null, 0) == "?: look here (0 m)", "subtitle, markup out");
        var gate = new PingGate();
        Check(gate.Allow(2, 0) && !gate.Allow(2, 1999) && gate.Allow(3, 1999) && gate.Allow(2, 2000) && !gate.Allow(2, 3000), "one ping per player every 2 s");
        gate.Clear();
        Check(gate.Allow(2, 2500), "cleared");
    }

    static void GiftRules()
    {
        var giver = new GiftLedger(new Random(1));
        var taker = new GiftLedger(new Random(2));
        string give = giver.Give(2, 500, 0);
        var f = give.Split(' ');
        Check(f.Length == 3 && f[0] == "give" && f[1] == "500" && f[2].Length >= 1 && f[2].Length <= 16 && giver.Waiting == 1, "give <cents> <nonce>: " + give);
        Check(giver.Give(2, 0, 0) == null && giver.Give(2, -1, 0) == null && giver.Give(2, GiftLedger.MaxCents + 1, 0) == null && giver.Waiting == 1, "no gift of nothing or over $10,000");
        Check(giver.Give(2, GiftLedger.MaxCents, 0) != null && giver.Waiting == 2, "$10,000 is fine");
        giver.Clear();
        give = giver.Give(2, 500, 0);
        var heard = taker.Hear(1, give, 100);
        Check(heard.pay == 500 && heard.reply == "ack " + give.Split(' ')[2] && heard.settled == 0, "the taker pays it and answers");
        var again = taker.Hear(1, give, 200);
        Check(again.pay == 0 && again.reply == heard.reply, "the same nonce again: not paid twice, but answered again (our answer got lost)");
        Check(giver.Hear(3, heard.reply, 300).settled == 0 && giver.Waiting == 1, "an answer from someone else doesn't settle it");
        Check(giver.Hear(2, heard.reply, 300).settled == 500 && giver.Waiting == 0, "the answer settles it");
        Check(giver.Hear(2, heard.reply, 400).settled == 0, "settled once");
        Check(giver.Hear(2, "ack nobody", 400).settled == 0 && giver.Hear(2, "ack", 400).settled == 0, "unknown answers ignored");
        // No answer: back to the giver after 10 s, and a late answer then counts for nothing.
        give = giver.Give(4, 750, 1000);
        string nonce = give.Split(' ')[2];
        Check(giver.Overdue(10999).Count == 0, "not yet");
        var back = giver.Overdue(11000);
        Check(back.Count == 1 && back[0].to == 4 && back[0].cents == 750 && giver.Waiting == 0 && giver.Overdue(20000).Count == 0, "back after 10 s, once");
        Check(giver.Hear(4, "ack " + nonce, 11500).settled == 0, "too late");
        // Not answered yet: sent again every 2 s until 10 s; a refused take answers nothing, and a repeat may be taken.
        giver.Clear();
        give = giver.Give(2, 300, 50000);
        Check(giver.Resend(51999).Count == 0 && giver.Resend(52000).SequenceEqual(new[] { ((byte)2, give) }) && giver.Resend(53000).Count == 0
              && giver.Resend(54000).Count == 1 && giver.Resend(60000).Count == 0, "again every 2 s, never past 10 s: " + give);
        var t5 = new GiftLedger();
        var no = t5.Hear(1, give, 50100, c => false);
        var yes = t5.Hear(1, give, 52100, c => c == 300);
        Check(no.pay == 0 && no.reply == null && yes.pay == 300 && yes.reply == "ack " + give.Split(' ')[2], "busy: no answer; its repeat taken later");
        Check(giver.Hear(2, yes.reply, 52200).settled == 300 && giver.Resend(56000).Count == 0, "answered: no more repeats");
        // The session ends: everything still waiting comes back.
        giver.Give(2, 100, 30000); giver.Give(3, 200, 30000);
        back = giver.Clear();
        Check(back.Count == 2 && back.Sum(b => b.cents) == 300 && giver.Waiting == 0, "the session over: all back");
        // Odd gifts are dropped unanswered.
        var t2 = new GiftLedger();
        int k = 0;   // a second apart: the flood rule isn't what refuses them
        foreach (var bad in new[] { null, "", "give", "give 100", "give x n1", "give -5 n1", "give +5 n1", "give 0 n1", "give 1000001 n1", "give 99999999999 n1",
                                    "give 5.5 n1", "give 5 " + new string('a', 17), "give 5 n~1", "give 5 né1", "take 5 n1", " give 5 n1" })
            Check(t2.Hear(1, bad, 1000 * k++).pay == 0, "refused: " + bad);
        Check(t2.Hear(1, "give 5 n1", 1000 * k).pay == 5, "a good one after them is taken");
        // At most 5 gifts a minute from one sender; more than 5 notices a second are dropped first.
        var t3 = new GiftLedger();
        int paid = 0;
        for (int i = 0; i < 7; i++) paid += t3.Hear(1, "give 100 g" + i, i * 1000).pay;
        Check(paid == 500, "5 gifts a minute: " + paid);
        Check(t3.Hear(2, "give 100 g0", 6000).pay == 100, "another sender's are their own");
        Check(t3.Hear(1, "give 100 g9", 61000).pay == 100, "a minute on, gifts are taken again");
        var t4 = new GiftLedger();
        for (int i = 0; i < 5; i++) t4.Hear(1, "junk", 100);
        Check(t4.Hear(1, "give 100 a1", 500).pay == 0 && t4.Hear(1, "give 100 a1", 1100).pay == 100, "a flood: the rest of that second dropped");
    }

    // ---- contests and the race ----
    static string Sent(ContestRun run) { string s = string.Join("|", run.Out.Select(m => m.to + ":" + m.text)); run.Out.Clear(); return s; }

    static void ContestMessages()
    {
        Check(ContestMsg.Parse("open 3-1 race 1 2 3 h", out string v, out string id, out string rest) && v == "open" && id == "3-1" && rest == "race 1 2 3 h", "open parsed");
        Check(ContestMsg.Parse("in 3-1", out v, out id, out rest) && v == "in" && id == "3-1" && rest == "", "a verb and an id alone");
        Check(ContestMsg.HostOf("3-1") == 3 && ContestMsg.HostOf("254-99") == 254, "the host is the id's first part");
        foreach (var bad in new[] { null, "", "in", "in 3", "in -1", "in 3-", "in 0-1", "in 255-1", "in x-1", "in 3-x", "in 3-1-2", "in 123-123456", " in 3-1" })
            Check(!ContestMsg.Parse(bad, out _, out _, out _), "refused: " + bad);
        Check(!ContestMsg.Parse("in 3-1 " + new string('x', 1200), out _, out _, out _), "oversize refused");
    }

    static void ContestHostRun()
    {
        Stats.Clear();
        var race = new Race();
        race.PosOf = id => new Vec3(10, 0, 0);   // the partner as we last saw them: 10 m from the finish
        var run = ContestRun.Open(1, 7, race, "0 0 0 -", 0);
        Check(run.Id == "1-7" && run.IAmHost && run.Players.SequenceEqual(new byte[] { 1 }) && Sent(run) == "255:open 1-7 race 0 0 0 -", "opened: the open goes to everyone");
        Check(run.Tick(0) == "Co-op: Race in 10" && run.Tick(500) == null && run.Tick(1000) == "Co-op: Race in 9", "a subtitle each second");
        Check(run.OnNotice(2, "in", "", 1100) == "Co-op: 2 is in" && run.OnNotice(2, "in", "", 1200) == null && run.OnNotice(1, "in", "", 1300) == null
              && run.Players.SequenceEqual(new byte[] { 1, 2 }), "2 is in, once; the host is in already");
        run.OnNotice(2, "report", "done 1", 1400);   // before GO: nothing to report to
        Check(run.Tick(5000) == "Co-op: Race in 5" && Sent(run) == "255:open 1-7 race 0 0 0 -", "the open goes again halfway, for a lost packet");
        Check(run.Tick(9000) == "Co-op: Race in 1" && Sent(run) == "", "the last second");
        Check(run.Tick(10000) == "GO!" && run.Started && Sent(run) == "255:start 1-7|255:standings 1-7 r1:-1 r2:-1", "GO: the start and the first standings go out, nobody done");
        var lines = run.Lines(10000);
        Check(lines.Count == 3 && lines[0].text == "Race" && lines[1].text == "   1  racing" && lines[1].tone == 1 && lines[2].text == "   2  racing" && lines[2].tone == 0, "panel: everyone racing");
        run.OnNotice(3, "report", "done 5000", 11000);   // not in: ignored
        run.OnNotice(2, "report", "dist 120", 11000);
        Check(run.Tick(12000) == null && Sent(run) == "255:standings 1-7 r1:-1 r2:120", "standings every 2 s, with their distance");
        run.OnNotice(2, "report", "done 99000", 12500);   // a slow claimed time, but it came first
        race.Observe(new Vec3(100, 0, 0), false, 12400);   // we rode out...
        race.Observe(new Vec3(3, 4, 0), false, 12600);   // ...and we're 5 m from the finish, on foot, any way allowed
        Check(race.Finished && run.Tick(12600) == null && Sent(run) == "", "our own done needs no notice");
        Check(run.Tick(13000) == null && run.Ended && run.Results == "f2:99000 f1:2600" && Sent(run) == "255:end 1-7 f2:99000 f1:2600",
              "over once everyone's in: the order the reports came, the times as claimed");
        Check(Stats.Get("race.wins") == 0, "second: no win for us");
        Check(run.Tick(15000) == null && Sent(run) == "255:end 1-7 f2:99000 f1:2600", "the end goes again every 2 s");
        lines = run.Lines(15000);
        Check(lines.Count == 4 && lines[0].text == "Race over" && lines[1].text == "Winner: 2" && lines[1].tone == 4 && lines[2].text == "1. 2  1:39.0"
              && lines[3].text == "2. 1  0:02.6" && lines[3].tone == 1, "results: " + string.Join(" / ", lines.Select(l => l.text)));
        run.Tick(27999); Check(!run.Done, "the results stay 15 s"); run.Tick(28000); Check(run.Done && run.Why == "over", "then it's done");
        var solo = ContestRun.Open(1, 8, new Race(), "0 0 0 -", 0);
        Check(solo.Tick(10000) == "Co-op: nobody joined the race" && solo.Done && solo.Why == "nobody joined" && Sent(solo) == "255:open 1-8 race 0 0 0 -|255:cancel 1-8", "nobody joined: called off at GO");
    }

    static void ContestPlayerRun()
    {
        Stats.Clear();
        var race = new Race();
        var run = ContestRun.Join(2, 1, "1-7", race, "0 0 0 h", 0);
        Check(!run.IAmHost && run.Host == 1 && Sent(run) == "1:in 1-7", "in goes to the host");
        Check(run.Tick(0) == "Co-op: Race in 10" && run.Tick(2000) == "Co-op: Race in 8" && Sent(run) == "1:in 1-7", "in again every 2 s until GO");
        var lines = run.Lines(2000);
        Check(lines.Count == 2 && lines[0].text == "Race: starting in 8" && lines[1].text == "You're in: wait for GO", "the countdown panel");
        Check(run.OnNotice(3, "start", "", 3000) == null && !run.Started, "only the host starts it");
        Check(run.OnNotice(1, "standings", "r1:-1 r2:-1", 3000) == "GO!" && run.Started && run.Standings == "r1:-1 r2:-1", "standings stand in for a lost start");
        Check(run.Tick(3500) == null && Sent(run) == "", "nothing to report before we know where we are");
        race.Observe(new Vec3(100, 0, 0), false, 3600);
        Check(run.Tick(4000) == null && Sent(run) == "1:report 1-7 dist 100", "our distance goes to the host");
        race.Observe(new Vec3(5, 0, 0), false, 4100);
        Check(!race.Finished, "within 15 m on foot isn't a finish when it's on horseback only");
        race.Observe(new Vec3(5, 0, 0), true, 4600);
        Check(race.Finished && run.Tick(4700) == null && Sent(run) == "1:report 1-7 done 1600", "on a horse it is: done at once, with our time, not behind the 2 s dist gate");
        Check(run.Tick(6000) == null && Sent(run) == "" && run.Tick(6700) == null && Sent(run) == "1:report 1-7 done 1600", "done again every 2 s until the host shows it");
        run.OnNotice(1, "standings", "f2:1600 r1:40", 8500);
        Check(run.Tick(10000) == null && Sent(run) == "", "shown finished: quiet");
        lines = run.Lines(10000);
        Check(lines.Count == 3 && lines[0].text == "Race (on horseback)" && lines[1].text == "1. 2  0:01.6" && lines[1].tone == 1
              && lines[2].text == "   1  racing, 40 m to go" && lines[2].tone == 0, "panel: " + string.Join(" / ", lines.Select(l => l.text)));
        Check(run.OnNotice(1, "end", "f2:1600 d1", 11000) == null && run.Ended && Sent(run) == "" && Stats.Get("race.wins") == 1, "we won: a race win, nothing to send");
        lines = run.Lines(11000);
        Check(lines.Count == 4 && lines[0].text == "Race over" && lines[1].text == "Winner: 2" && lines[1].tone == 4 && lines[3].text == "   1  did not finish" && lines[3].tone == 2,
              "results: " + string.Join(" / ", lines.Select(l => l.text)));
        Check(run.Tick(25999) == null && !run.Done && run.Tick(26000) == null && run.Done, "the results stay 15 s, then it's done");
        var c = ContestRun.Join(2, 1, "1-8", new Race(), "0 0 0 -", 0);
        Check(c.OnNotice(1, "cancel", "", 100) == "Co-op: 1 called the race off" && c.Done && c.Why == "cancelled", "the host called it off");
        var lh = ContestRun.Join(2, 1, "1-9", new Race(), "0 0 0 -", 0);
        Check(lh.PlayerLeft(3, 100) == null && !lh.Done && lh.PlayerLeft(1, 200) == "Co-op: Race off: 1 left" && lh.Done && lh.Why == "host left", "the host leaving ends it; anyone else leaving doesn't");
        var g = ContestRun.Join(2, 1, "1-10", new Race(), "0 0 0 -", 0);
        Check(g.Tick(19999) == null && !g.Done && g.Tick(20000) != null && g.Done && g.Why == "never started", "no GO within 10 s of the countdown's end: given up");
    }

    static void ContestJoinQuit()
    {
        Stats.Clear();
        Check(Contest.WinStat("race") == "race.wins" && Contest.WinStat("duel") == "duel.wins" && Contest.WinStat("shooting") == "shoot.wins"
              && Contest.WinStat("hunting") == "hunt.wins", "the win stats are the scoreboard's keys");
        var where = new System.Collections.Generic.Dictionary<byte, Vec3> { { 1, new Vec3(0, 0, 0) }, { 2, new Vec3(50, 0, 0) }, { 3, new Vec3(0, 79, 0) }, { 4, new Vec3(81, 0, 0) } };
        var race = new Race { PosOf = id => where.TryGetValue(id, out var v) ? v : (Vec3?)null };
        var run = ContestRun.Open(1, 1, race, "0 0 0 -", 0);
        Sent(run);
        Check(run.OnNotice(2, "in", "", 100) == "Co-op: 2 is in" && run.OnNotice(3, "in", "", 100) == "Co-op: 3 is in" && run.OnNotice(4, "in", "", 100) == null
              && run.OnNotice(5, "in", "", 100) == null && run.Players.SequenceEqual(new byte[] { 1, 2, 3 }), "in only from a player the host sees within 80 m: 81 m and nowhere refused");
        Check(run.OnNotice(5, "cancel", "", 150) == null && run.OnNotice(1, "cancel", "", 150) == null && !run.Done && run.Players.Count == 3, "a cancel from a stranger, or our own echo, changes nothing");
        where[4] = new Vec3(70, 0, 0);
        Check(run.OnNotice(4, "in", "", 200) == "Co-op: 4 is in" && run.OnNotice(4, "cancel", "", 300) == "Co-op: 4 is out of the race"
              && run.OnNotice(4, "cancel", "", 350) == null && run.OnNotice(4, "in", "", 400) == null && run.Players.SequenceEqual(new byte[] { 1, 2, 3 }),
              "come closer: in; then out, once, and a late in doesn't put them back");
        run.Tick(10000);
        Check(Sent(run) == "255:start 1-1|255:standings 1-1 r1:-1 r2:-1 r3:-1", "GO with those who stayed");
        Check(run.OnNotice(3, "cancel", "", 11000) == "Co-op: 3 is out of the race" && !run.Players.Contains(3), "quitting after GO");
        run.Tick(12000);
        Check(Sent(run) == "255:standings 1-1 r1:-1 r2:-1", "the race goes on without them: " + run.Standings);
        where[2] = new Vec3(10, 0, 0);   // at the finish, as the race wants a done from
        run.OnNotice(2, "report", "done 2000", 12500); race.Observe(new Vec3(100, 0, 0), false, 12600); race.Observe(new Vec3(0, 0, 0), false, 12700);
        run.Tick(12800); run.Tick(13000);   // our own done goes in on one tick, the end on the next
        Check(run.Ended && run.Results == "f2:2000 f1:2700" && Stats.Get("race.wins") == 0 && Sent(run) == "255:end 1-1 f2:2000 f1:2700", "over: second, no win for us: " + run.Results);
        run.Cancel();
        Check(run.Done && Sent(run) == "", "calling it off once it's over sends nothing");
        var won = ContestRun.Join(2, 1, "1-2", new Race(), "0 0 0 -", 0);
        won.OnNotice(1, "end", "f2:900 d1", 1000);
        Check(Stats.Get("race.wins") == 1 && Stats.All().Count == 1, "the framework counts our win, under the kind's key only");
        Check(new Race().Winner("f3:5 f2:9") == 3 && new Race().Winner("d1 d2") == 0 && new Race().Winner("r3:5") == 0 && new Race().Winner("") == 0 && new Race().Winner(null) == 0,
              "the race's winner: the first finisher, nobody when nobody finished");
        var me = ContestRun.Join(2, 1, "1-3", new Race(), "0 0 0 -", 0);
        Sent(me);
        me.Cancel();
        Check(me.Done && me.Why == "quit" && Sent(me) == "1:cancel 1-3", "quitting tells the host");
        var host = ContestRun.Open(1, 4, new Race(), "0 0 0 -", 0);
        Sent(host);
        host.Cancel();
        Check(host.Done && host.Why == "cancelled" && Sent(host) == "255:cancel 1-4", "the host calling it off tells everyone");
    }

    static void DuelRules()
    {
        Check(Duel.TryParse("1 2 1500", out byte a, out byte b, out int pause) && a == 1 && b == 2 && pause == 1500 && Duel.Params(1, 2, 1500) == "1 2 1500", "params round trip");
        foreach (var bad in new[] { null, "", "1 2", "1 1 1500", "0 2 1500", "1 255 1500", "1 2 999", "1 2 3001", "1 2 x", "1 2 1500 9", "1  2 1500", "300 2 1500" })
            Check(!Duel.TryParse(bad, out _, out _, out _), "refused: " + bad);
        var d = new Duel { Me = 1 };
        d.Begin(new byte[] { 1, 2, 3 }, "1 2 1000", 0);   // DRAW at 3000
        Check(!d.Over && d.Standings() == "1 2 on", "on: " + d.Standings());
        d.OnReport(2, "shot 0", 2000);   // before our DRAW: no game's DRAW comes before the host's
        d.OnReport(3, "shot 1", 4000); d.OnReport(9, "shot 1", 4000);   // an onlooker, a stranger
        d.OnReport(2, "shot 5000", 4000);   // more than the time since DRAW
        foreach (var bad in new[] { "shot", "shot -1", "shot x", "shot 1 2", "", null, "hit 1", "Early" }) d.OnReport(2, bad, 4000);
        d.Tick(9000); Check(!d.Over, "bogus, early-timed, onlooker and stranger reports dropped");
        d.OnReport(1, "shot 400", 9000); d.OnReport(1, "shot 1", 9100);
        d.Tick(10_499); Check(!d.Over, "the first shot waits 1.5 s for the other's; our own second report ignored");
        d.OnReport(2, "shot 300", 10_400);
        Check(d.Over && d.Results() == "1 2 won 2 300" && d.Winner(d.Results()) == 2, "the faster shot wins though it came later: " + d.Results());
        var one = new Duel { Me = 1 }; one.Begin(new byte[] { 1, 2 }, "1 2 3000", 0); one.OnReport(2, "shot 900", 6000);
        one.Tick(7499); Check(!one.Over, "within the wait");
        one.Tick(7500); Check(one.Results() == "1 2 won 2 900", "a lone shot wins once the wait is up");
        var e = new Duel { Me = 1 }; e.Begin(new byte[] { 1, 2 }, "1 2 1000", 0); e.OnReport(1, "shot 200", 3200); e.OnReport(2, "early", 3300);
        Check(e.Results() == "1 2 early 2" && e.Winner(e.Results()) == 1, "drawing too soon is out, even with the other's shot on its way");
        var t = new Duel { Me = 1 }; t.Begin(new byte[] { 1, 2 }, "1 2 1000", 0);
        t.Tick(32_999); Check(!t.Over, "30 s to hit"); t.Tick(33_000); Check(t.Results() == "1 2 time" && t.Winner(t.Results()) == 0, "nobody hit: nobody wins");
        var l = new Duel { Me = 1 }; l.Begin(new byte[] { 1, 2, 3 }, "1 2 1000", 0); l.Left(3, 100); Check(!l.Over, "an onlooker leaving changes nothing");
        l.Left(2, 200); Check(l.Results() == "1 2 left 2" && l.Winner(l.Results()) == 0, "the opponent walking away: no win for anyone");
        var n = new Duel { Me = 1 }; n.Begin(new byte[] { 1, 3 }, "1 2 1000", 0); Check(n.Over && n.Results() == "1 2 none", "the opponent never came in: off");
        var f = new Duel { Me = 1 }; f.Begin(new byte[] { 1, 2 }, "4 2 1000", 0); Check(f.Over && f.Results() == "4 2 none", "params naming someone else as the challenger: off");
        foreach (var bad in new[] { "", null, "1 2 won 7 5", "1 1 won 1 5", "1 2 time", "1 2 left 2", "x 2 won 2 5", "1 2 won" }) Check(d.Winner(bad) == 0, "no winner: " + bad);
        Check(d.Winner("1 2 early 1") == 2 && d.Winner("1 2 won 1 5") == 1, "winners named");
    }

    static void DuelPlayer()
    {
        Func<byte, string> names = id => id == 1 ? "Arthur" : id == 2 ? "John" : "Sadie";
        var c = new Duel { Me = 2, NameOf = names };
        c.Start("1 2 1500", 1000);   // DRAW at 4500
        Check(c.Dueling && c.Foe == 1 && c.Cue(1000) == "Holster and face Arthur" && c.Cue(1100) == null, "facing Arthur, said once");
        Check(c.Lines("1 2 on", 1000)[0].text == "DUEL vs Arthur: holster and face them" && c.Lines("1 2 on", 3500)[0].text == "DUEL vs Arthur: steady..." && c.Lines("", 3500)[0].tone == 3, "the panel line");
        Check(c.Report(3600) == null && !c.Drawn(4499) && c.Drawn(4500) && c.Cue(4500) == Duel.DrawCue && c.Cue(4600) == null, "DRAW! once, at 4.5 s");
        c.Shot(4920); c.Shot(5000);
        Check(c.Report(5000) == "shot 420" && c.Report(5500) == null && c.Report(6000) == "shot 420" && c.Lines("1 2 on", 6000)[0].text == "DUEL vs Arthur: you fired in 0.42 s",
              "our first shot, timed from our DRAW, said every second");
        c.End("1 2 won 1 380");
        Check(!c.Dueling && c.Report(8000) == null && c.Cue(8000) == "Co-op: duel over: Arthur beat you in 0.38 s" && c.Cue(8100) == null, "the verdict, once");
        var line = c.Lines("1 2 won 1 380", 8000)[0];
        Check(line.text == "DUEL vs Arthur: Arthur beat you in 0.38 s" && line.tone == 2 && c.TakeFall() && !c.TakeFall(), "lost to a shot: grey, and we fall, once");
        var w = new Duel { Me = 2, NameOf = names }; w.Start("1 2 1000", 0); w.Shot(3300); w.End("1 2 won 2 300");
        Check(w.Lines("1 2 won 2 300", 4000)[0].text == "DUEL vs Arthur: you won in 0.30 s" && w.Lines("1 2 won 2 300", 4000)[0].tone == 4 && !w.TakeFall(), "we won: green, no fall");
        var early = new Duel { Me = 2, NameOf = names }; early.Start("1 2 1000", 0); early.Shot(2999);
        Check(early.Report(3000) == "early" && early.Lines("", 3000)[0].text == "DUEL vs Arthur: you drew too soon", "a shot before DRAW is early");
        early.Shot(3500); Check(early.Report(4000) == "early", "and stays early");
        early.End("1 2 early 2"); Check(early.Lines("1 2 early 2", 4000)[0].text == "DUEL vs Arthur: you drew too soon: Arthur wins" && !early.TakeFall(), "out, no fall");
        var o = new Duel { Me = 3, NameOf = names }; o.Start("1 2 1000", 0);
        Check(!o.Dueling && o.Cue(0) == "Co-op: Arthur and John face off" && o.Cue(3000) == null && o.Lines("", 3000)[0].text == "DUEL: Arthur vs John: DRAW!", "an onlooker watches");
        o.Shot(3100); Check(o.Report(3200) == null, "and never reports");
        o.End("1 2 early 1"); Check(o.Lines("1 2 early 1", 4000)[0].text == "DUEL: Arthur vs John: Arthur drew too soon: John wins" && o.Lines("1 2 early 1", 4000)[0].tone == 4 && !o.TakeFall(), "the onlooker's verdict");
        foreach (var (text, said) in new[] { ("1 2 time", "nobody hit: a draw"), ("1 2 left 1", "Arthur walked away"), ("1 2 none", "John didn't accept"), ("~r~garbage <b>", "it's off"), ("1 2 won 1", "Arthur won") })
        {
            var v = new Duel { Me = 3, NameOf = names }; v.Start("1 2 1000", 0); v.End(text);
            Check(v.Lines(text, 5000)[0].text == "DUEL: Arthur vs John: " + said, "verdict for '" + text + "': " + v.Lines(text, 5000)[0].text);
        }

        Stats.Clear();
        var where = new System.Collections.Generic.Dictionary<byte, Vec3> { { 1, new Vec3(0, 0, 0) }, { 2, new Vec3(5, 0, 0) } };
        var hd = new Duel { NameOf = names, PosOf = id => where.TryGetValue(id, out var v) ? v : (Vec3?)null };
        var host = ContestRun.Open(1, 1, hd, "1 2 1000", 0);
        Check(Sent(host) == "255:open 1-1 duel 1 2 1000" && host.OnNotice(2, "in", "", 100) == "Co-op: John is in", "opened, John's in");
        Check(host.Tick(10_000) == "GO!" && Sent(host) == "255:start 1-1|255:standings 1-1 1 2 on", "GO: DRAW at 13 s");
        hd.Shot(13_400);
        Check(host.Tick(13_500) == null && Sent(host) == "255:standings 1-1 1 2 on" && !host.Ended, "our own shot goes straight in (no notice); the wait for John's");
        host.OnNotice(2, "report", "shot 350", 14_000);
        Check(host.Tick(14_100) == null && host.Ended && host.Results == "1 2 won 2 350" && Sent(host) == "255:end 1-1 1 2 won 2 350" && Stats.Get("duel.wins") == 0 && hd.TakeFall(),
              "John was faster: ended, no win for us, we fall: " + host.Results);
        var pd = new Duel { NameOf = names };
        var player = ContestRun.Join(2, 1, "1-1", pd, "1 2 1000", 0);
        Check(Sent(player) == "1:in 1-1" && player.OnNotice(1, "start", "", 10_050) == "GO!" && pd.Dueling, "John's game: in, GO");
        pd.Shot(13_400);
        Check(player.Tick(13_500) == null && Sent(player) == "1:report 1-1 shot 350", "John's shot, timed from his own DRAW (13.05 s)");
        player.OnNotice(1, "end", "1 2 won 2 350", 14_200);
        Check(player.Ended && Stats.Get("duel.wins") == 1, "the framework counts John's win in John's game");
    }

    static void RaceRules()
    {
        Check(Race.TryParse("1.5 -2 3 h", out var f, out bool h) && f.X == 1.5f && f.Y == -2f && f.Z == 3f && h, "params parsed");
        Check(Race.TryParse("1 2 3", out _, out h) && !h && Race.TryParse("1 2 3 -", out _, out h) && !h, "any way you like");
        foreach (var bad in new[] { null, "", "1 2", "x 2 3 h", "NaN 2 3", "1 2 30000 h" }) Check(!Race.TryParse(bad, out _, out _), "refused: " + bad);
        Check(Race.Params(new Vec3(1.5f, -2f, 3f), true) == "1.5 -2 3 h" && Race.Params(new Vec3(0, 0, 0), false) == "0 0 0 -", "params written");
        var r = new Race();
        r.PosOf = id => new Vec3(0, 30, 0);   // everyone as we last saw them: 30 m from the finish
        Check(r.Standings() == "" && !r.Over, "nothing before it begins");
        r.Begin(new byte[] { 1, 2, 3 }, "0 0 0 -", 1000);
        Check(r.Standings() == "r1:-1 r2:-1 r3:-1" && !r.Over, "all racing, no distances yet");
        r.OnReport(2, "dist 250", 2000); r.OnReport(3, "dist 999999999", 2000); r.OnReport(9, "dist 1", 2000);
        foreach (var bad in new[] { "dist -5", "dist abc", "dist 1.5", "", null, "done", "done 1 2", "sprint 5" }) r.OnReport(1, bad, 2000);
        Check(r.Standings() == "r1:-1 r2:250 r3:99999", "distances clamped, bad reports and strangers dropped: " + r.Standings());
        r.OnReport(3, "done 60000", 3000); r.OnReport(2, "done 100", 3100); r.OnReport(3, "done 1", 3200); r.OnReport(2, "dist 5", 3300);
        Check(r.Standings() == "f3:60000 f2:100 r1:-1", "ranked by arrival, not by the claimed times; a finisher's later reports are ignored: " + r.Standings());
        r.Tick(4000); Check(!r.Over, "one still racing");
        r.Left(1, 4500); r.Tick(4600);
        Check(r.Over && r.Results() == "f3:60000 f2:100", "a racer who leaves is out: over when the rest are in");
        var f2 = new Race { Me = 1, PosOf = id => id == 2 ? (Vec3?)new Vec3(400, 0, 0) : null };
        f2.Begin(new byte[] { 1, 2, 3 }, "0 0 0 -", 0);
        f2.OnReport(2, "done 1", 100); f2.OnReport(3, "done 1", 100); f2.OnReport(1, "done 1", 100);
        Check(f2.Standings() == "f1:1 r2:-1 r3:-1", "a done from a racer last seen 400 m from the finish, or nowhere, is dropped; our own is taken: " + f2.Standings());
        var t = new Race { Me = 1 }; t.Begin(new byte[] { 1, 2 }, "0 0 0 -", 0); t.OnReport(1, "done 5000", 5000);
        t.Tick(64_999); Check(!t.Over, "within the minute after the first finisher");
        t.Tick(65_000); Check(t.Over && t.Results() == "f1:5000 d2", "a minute after the first finisher: the rest didn't finish");
        var a = new Race(); a.Start("0 0 0 -", 0);
        a.Observe(new Vec3(1, 0, 0), false, 100); Check(!a.Finished, "standing at the finish at GO is no finish");
        a.Observe(new Vec3(20, 0, 0), false, 200); a.Observe(new Vec3(1, 0, 0), false, 300); Check(a.Finished, "out of the zone and back in is");
        var n = new Race(); n.Begin(new byte[] { 1, 2 }, "0 0 0 -", 0); n.Tick(600_000); n.End(n.Results());
        var lines = n.Lines(n.Results(), 0);
        Check(n.Results() == "d1 d2" && lines.Count == 4 && lines[1].text == "Nobody finished" && lines[1].tone == 2 && lines[2].text == "   1  did not finish", "nobody finished: no winner named");
        Check(new Race().Lines("f9:100 x r2 r:3 f:4 junk", 0).Count == 3, "odd standings tokens are skipped");
    }

    static void HuntingRules()
    {
        uint rabbit = Hunting.Hash("a_c_rabbit_01"), deer = Hunting.Hash("a_c_deer_01"), bear = Hunting.Hash("a_c_bear_01"), wolf = Hunting.Hash("a_c_wolf");
        Check(rabbit == 0xDFB55C81 && deer == 0x423417A7 && bear == 0xBCFD0E7F && wolf == 0xBBD91DDA && Hunting.Hash("A_C_Deer_01") == deer, "the game's model hashes (any case)");
        Check(Hunting.Points(rabbit) == 1 && Hunting.Points(Hunting.Hash("a_c_fox_01")) == 2 && Hunting.Points(deer) == 3 && Hunting.Points(wolf) == 3
              && Hunting.Points(Hunting.Hash("a_c_elk_01")) == 5 && Hunting.Points(Hunting.Hash("a_c_cougar_01")) == 5 && Hunting.Points(Hunting.Hash("a_c_alligator_01")) == 5
              && Hunting.Points(bear) == 8 && Hunting.Points(Hunting.Hash("a_c_buffalo_01")) == 8 && Hunting.Points(Hunting.Hash("a_c_moose_01")) == 8
              && Hunting.Points(Hunting.Hash("a_c_buffalo_tatanka_01")) == 15 && Hunting.Points(Hunting.Hash("a_c_alligator_02")) == 15, "points by size, legendary models 15");
        foreach (var no in new[] { "a_c_horse_morgan_bay", "a_c_donkey_01", "a_c_doghusky_01", "a_c_dogrufus_01", "a_c_cat_01", "a_c_cow", "a_c_chicken_01", "a_c_sheep_01",
                                   "a_c_fishperch_01_sm", "player_zero", "cs_charlessmith", "" })
            Check(Hunting.Points(Hunting.Hash(no)) == 0, "doesn't score: " + no);
        Check(Hunting.AnimalName(bear) == "grizzly bear" && Hunting.AnimalName(12345) == "animal", "names");
        Check(Hunting.TryParse("3", out int ms) && ms == 180_000 && Hunting.TryParse("10", out ms) && ms == 600_000 && Hunting.Params(5) == "5", "params");
        foreach (var bad in new[] { null, "", "4", "0", "-3", "+5", " 5", "5 ", "abc", "99999999999" }) Check(!Hunting.TryParse(bad, out _), "refused: " + bad);

        var h = new Hunting { Me = 1 };
        Check(h.Standings() == "" && !h.Over, "nothing before it begins");
        h.Begin(new byte[] { 1, 2, 3 }, "3", 1000);
        Check(h.Standings() == "1:0:0:0 2:0:0:0 3:0:0:0", "everyone on nothing: " + h.Standings());
        h.OnReport(2, $"kill {rabbit}", 2000);
        h.OnReport(2, $"kill {rabbit} {deer}", 2500);
        h.OnReport(2, $"kill {rabbit}", 2600);   // an older report, come late
        h.OnReport(3, $"kill {bear}", 2700);
        h.OnReport(9, $"kill {bear} {bear}", 2700);   // not in the hunt
        Check(h.Standings() == $"3:8:1:{bear} 2:4:2:{deer} 1:0:0:0", "most points first; the longest list kept; strangers dropped: " + h.Standings());
        uint horse = Hunting.Hash("a_c_horse_morgan_bay"), dog = Hunting.Hash("a_c_doghusky_01");
        foreach (var bad in new[] { null, "", "kill", "kill ", $"kill {bear}x", $"kill {bear} {horse}", $"kill {bear} {dog}", $"kill {bear} -1", $"kill {bear} +5",
                                    $"kill {bear}  {bear}", $"done {bear} {bear}", $"kill {deer} {bear}", "kill " + string.Join(" ", Enumerable.Repeat(rabbit, 61)) })
            h.OnReport(3, bad, 3000);
        Check(h.Standings() == $"3:8:1:{bear} 2:4:2:{deer} 1:0:0:0", "odd, forged and rewritten reports dropped, horses and dogs never count, 60 at most: " + h.Standings());
        h.OnReport(1, "kill " + string.Join(" ", Enumerable.Repeat(rabbit, 60)), 3000);
        Check(h.Standings().StartsWith("1:60:60:" + rabbit), "60 is fine: " + h.Standings());
        h.Tick(181_000 + 1999); Check(!h.Over, "the last reports have 2 s to come");
        h.OnReport(2, $"kill {rabbit} {deer} {bear} {bear} {bear} {bear} {bear} {bear} {bear}", 182_500);
        h.Tick(183_000); Check(h.Over, "over 3 minutes and 2 s after GO");
        h.OnReport(3, $"kill {bear} {bear} {bear} {bear} {bear} {bear} {bear} {bear} {bear}", 183_100);
        Check(h.Results() == $"1:60:60:{rabbit} 2:60:9:{bear} 3:8:1:{bear}" && h.Winner(h.Results()) == 0, "a report after the end is too late; level on points is a draw, in the order they joined: " + h.Results());
        h.End(h.Results());
        var lines = h.Lines(h.Results(), 0);
        Check(lines.Count == 6 && lines[0].text == "Hunt over" && lines[1].text == "A draw" && lines[1].tone == 2 && lines[2].text == "Biggest: 2's grizzly bear (8 points)"
              && lines[3].text == "1. 1  60 points, 60 animals" && lines[3].tone == 1 && lines[4].text == "2. 2  60 points, 9 animals" && lines[5].text == "3. 3  8 points, 1 animal",
              "results: a draw, the biggest animal, everyone's points: " + string.Join("|", lines.Select(l => l.text)));
        Check(h.Winner($"2:4:2:{deer} 3:8:1:{bear}") == 3 && h.Winner("1:0:0:0 2:0:0:0") == 0 && h.Winner("") == 0 && h.Winner(null) == 0 && h.Winner("x 2:5 9:9:9:9:9") == 0,
              "the winner has the most points alone, whatever the order");
        var w = new Hunting { Me = 2 }; w.End("");
        lines = w.Lines($"3:8:1:{bear} 2:1:1:{rabbit} junk 0:5:1:1", 0);
        Check(lines.Count == 5 && lines[1].text == "Winner: 3" && lines[1].tone == 4 && lines[3].text == "1. 3  8 points, 1 animal" && lines[4].text == "2. 2  1 point, 1 animal" && lines[4].tone == 1,
              "a winner named, odd tokens skipped: " + string.Join("|", lines.Select(l => l.text)));
        var g = new Hunting(); g.Begin(new byte[] { 1, 2 }, "7", 0);
        Check(g.Over, "a hunt with no proper length is over at once");
        var q = new Hunting(); q.Begin(new byte[] { 1, 2, 3 }, "5", 0);
        q.OnReport(2, $"kill {bear}", 100); q.Left(2, 200); q.OnReport(2, $"kill {bear} {bear}", 300);
        Check(q.Standings() == "1:0:0:0 3:0:0:0", "a hunter who leaves is off the standings: " + q.Standings());
    }

    static void HuntingRun()
    {
        Stats.Clear();
        uint rabbit = Hunting.Hash("a_c_rabbit_01"), deer = Hunting.Hash("a_c_deer_01"), horse = Hunting.Hash("a_c_horse_morgan_bay");
        var host = new Hunting { PosOf = id => new Vec3(10, 0, 0) };
        var hr = ContestRun.Open(1, 3, host, "3", 0);
        Check(Sent(hr) == "255:open 1-3 hunting 3", "the open: kind and minutes");
        var mine = new Hunting();
        var pr = ContestRun.Join(2, 1, "1-3", mine, "3", 100);
        Check(Sent(pr) == "1:in 1-3" && hr.OnNotice(2, "in", "", 200) == "Co-op: 2 is in", "a hunter in");
        Check(mine.Killed(deer, 5000) == 0 && !mine.Running, "nothing counts before GO");
        Check(hr.Tick(10_000) == "GO!" && Sent(hr) == "255:start 1-3|255:standings 1-3 1:0:0:0 2:0:0:0", "GO");
        Check(pr.OnNotice(1, "start", "", 10_100) == "GO!" && mine.Running && mine.LeftMs(10_100) == 180_000, "our hunt starts, 3 minutes left");
        Check(mine.Killed(horse, 11_000) == 0 && mine.Killed(rabbit, 11_000) == 1 && mine.Killed(deer, 11_100) == 3, "a horse doesn't count; a rabbit and a deer do");
        pr.Tick(11_200);
        Check(Sent(pr) == $"1:report 1-3 kill {rabbit} {deer}", "reported at once: " + Sent(pr));
        pr.Tick(11_800); Check(Sent(pr) == "", "not again within 2 s");
        hr.OnNotice(2, "report", $"kill {rabbit} {deer}", 11_300);
        hr.Tick(12_000);
        Check(Sent(hr) == $"255:standings 1-3 2:4:2:{deer} 1:0:0:0", "the host's standings: " + hr.Standings);
        pr.Tick(13_300); Check(Sent(pr) == $"1:report 1-3 kill {rabbit} {deer}", "unseen yet: said again after 2 s");
        pr.OnNotice(1, "standings", $"2:4:2:{deer} 1:0:0:0", 13_400);
        pr.Tick(15_500); Check(Sent(pr) == "", "seen: quiet");
        var lines = pr.Lines(16_100);
        Check(lines[0].text == "Hunting: 2:54 left" && lines[0].tone == 3 && lines[1].text == "1. 2  4 points, 2 animals" && lines[1].tone == 1 && lines[2].text == "2. 1  0 points, 0 animals",
              "the panel: time left, our row gold: " + string.Join("|", lines.Select(x => x.text)));
        Check(host.Killed(rabbit, 20_000) == 1, "the host hunts too");
        hr.Tick(20_500); hr.Tick(23_000);   // the report goes in after that tick's standings; the next standings show it
        Check(hr.Standings == $"2:4:2:{deer} 1:1:1:{rabbit}", "the host's own report goes straight in: " + hr.Standings);
        Sent(hr);
        Check(mine.Killed(deer, 190_100) == 0, "out of time: nothing counts");
        hr.Tick(191_999); Sent(hr); Check(!hr.Ended, "the grace for the last reports");
        hr.Tick(192_000);
        Check(hr.Ended && Sent(hr) == $"255:end 1-3 2:4:2:{deer} 1:1:1:{rabbit}" && Stats.Get("hunt.wins") == 0, "over: the host came second");
        pr.OnNotice(1, "end", hr.Results, 192_100);
        Check(pr.Ended && Stats.Get("hunt.wins") == 1, "the winner's game counts the win under hunt.wins");
        lines = pr.Lines(192_200);
        Check(lines[0].text == "Hunt over" && lines[1].text == "Winner: 2" && lines[2].text == "Biggest: 2's deer (3 points)", "the results: " + string.Join("|", lines.Select(x => x.text)));
        Check(mine.Killed(deer, 192_300) == 0 && mine.Report(192_300) == null, "nothing after the end");
    }

    static void ShootingRules()
    {
        string prms = Shooting.Params(new Vec3(100.5f, -20f, 3f), 90f, 42, 10, 30);
        Check(prms == "100.5 -20 3 90 42 10 30", "params written: " + prms);
        Check(Shooting.TryParse(prms, out var at, out float hd, out int seed, out int count, out int secs) && at.X == 100.5f && at.Y == -20f && at.Z == 3f
              && hd == 90f && seed == 42 && count == 10 && secs == 30, "params parsed");
        Check(Shooting.TryParse("0 0 0 -45.5 -7 20 60", out _, out hd, out seed, out _, out _) && hd == -45.5f && seed == -7, "a negative heading and seed");
        foreach (var bad in new[] { null, "", "0 0 0", "0 0 0 0 1 10", "0 0 0 0 1 10 30 x", "0 0 0 NaN 1 10 30", "0 0 0 9999 1 10 30", "0 0 0 0 1 0 30",
                                    "0 0 0 0 1 21 30", "0 0 0 0 1 -5 30", "0 0 0 0 1 10 5", "0 0 0 0 1 10 9999", "0 0 0 0 x 10 30", "NaN 0 0 0 1 10 30", "0 0 99999 0 1 10 30" })
            Check(!Shooting.TryParse(bad, out _, out _, out _, out _, out _), "refused: " + bad);
        var a = Shooting.Spots(new Vec3(100, 200, 5), 0f, 42, 20);
        Check(a.Length == 20 && a.SequenceEqual(Shooting.Spots(new Vec3(100, 200, 5), 0f, 42, 20)) && !a.SequenceEqual(Shooting.Spots(new Vec3(100, 200, 5), 0f, 43, 20)),
              "the same seed stands them in the same places; another seed doesn't");
        Check(a.All(p => p.Y - 200 >= 12f && p.Y - 200 <= 18f && Math.Abs(p.X - 100) <= 4f && p.Z == 5f) && a.Select(p => Math.Round(p.X, 2)).Distinct().Count() == 20,
              "facing north: 12-18 m ahead, spread over 8 m across, none in the same place across");
        Check(Shooting.Spots(new Vec3(0, 0, 0), 90f, 1, 10).All(p => -p.X >= 11.99f && -p.X <= 18.01f && Math.Abs(p.Y) <= 4.01f), "heading 90 faces west: the row is west of the spot");
        Check(Shooting.Spots(new Vec3(0, 0, 0), 0f, 1, 99).Length == 20 && Shooting.Spots(new Vec3(0, 0, 0), 0f, 1, -3).Length == 0, "never more than 20");

        var s = new Shooting { Me = 1 };
        Check(s.Standings() == "" && !s.Over, "nothing before it begins");
        s.Begin(new byte[] { 1, 2, 3 }, "0 0 0 0 1 10 30", 1000);
        Check(s.Standings() == "1:0 2:0 3:0", "all at nought, in the order they came in");
        s.OnReport(2, "score 3", 2000); s.OnReport(3, "score 99", 2100); s.OnReport(9, "score 5", 2200);
        foreach (var bad in new[] { "score -1", "score x", "score 1.5", "score +2", "", null, "score", "score 1 2", "hits 4", "score  4" }) s.OnReport(1, bad, 2300);
        Check(s.Standings() == "3:10 2:3 1:0", "scores clamped to the count, bad reports and strangers dropped: " + s.Standings());
        s.OnReport(1, "score 3", 2500); s.OnReport(2, "score 3", 2600); s.OnReport(2, "score 2", 2700);
        Check(s.Standings() == "3:10 2:3 1:3", "a tie goes to whoever got there first; a repeat or a lower score changes nothing: " + s.Standings());
        s.OnReport(1, "score 4", 2800);
        Check(s.Standings() == "3:10 1:4 2:3", "a rise moves them up: " + s.Standings());
        s.Tick(30_999); Check(!s.Over, "not before the time, while some have bottles left");
        s.Tick(31_000); Check(s.Over && s.Results() == "3:10 1:4 2:3" && s.Winner(s.Results()) == 3, "over at the time; the best score wins");
        var all = new Shooting();
        all.Begin(new byte[] { 1, 2 }, "0 0 0 0 1 10 30", 0);
        all.OnReport(1, "score 10", 500); all.Tick(600); Check(!all.Over, "one still shooting");
        all.OnReport(2, "score 10", 700); all.Tick(800); Check(all.Over && all.Results() == "1:10 2:10" && all.Winner(all.Results()) == 1, "over once everyone broke every bottle; the first there wins");
        var left = new Shooting();
        left.Begin(new byte[] { 1, 2, 3 }, "0 0 0 0 1 10 30", 0);
        left.OnReport(2, "score 5", 100); left.Left(2, 200); left.OnReport(2, "score 6", 300);
        Check(left.Standings() == "1:0 3:0", "a player who leaves is off the standings, and stays off");
        var odd = new Shooting(); odd.Begin(new byte[] { 1, 2 }, "junk", 0); odd.Tick(0);
        Check(odd.Over, "params the host can't read: over at once");
        Check(new Shooting().Winner("2:4 1:4") == 2 && new Shooting().Winner("1:0 2:0") == 0 && new Shooting().Winner("") == 0 && new Shooting().Winner(null) == 0
              && new Shooting().Winner("x:3") == 0, "the winner: the first, if they broke any");

        var me = new Shooting { Me = 2, NameOf = id => id == 2 ? "Sadie" : "Arthur" };
        var junk = new Shooting(); junk.Start("junk", 0);
        Check(!junk.Running && !me.Running, "not running before GO or with params it can't read");
        me.Start("0 0 0 0 1 10 30", 1000);
        Check(me.Running && me.Report(1000) == null, "nothing to say at nought");
        me.Observe(2, 1200);
        Check(me.Report(1200) == "score 2" && me.Report(1700) == null, "a rise is said at once, once");
        Check(me.Report(3200) == "score 2", "and again 2 s later while the standings don't show it");
        me.OnStandings("1:0 2:2");
        Check(me.Report(5200) == null, "shown in the standings: quiet");
        me.Observe(1, 5300); Check(me.Report(5300) == null, "a lower count changes nothing (a bottle can't mend)");
        me.Observe(50, 5400); Check(me.Report(5400) == "score 10", "never more than the count");
        var lines = me.Lines("1:3 2:2", 11_000);
        Check(lines.Count == 4 && lines[0].text == "Shooting: 10 bottles, 20 s left" && lines[0].tone == 3 && lines[1].text == "Shoot your bottles; partners shoot theirs" && lines[1].tone == 2
              && lines[2].text == "1. Arthur  3/10" && lines[2].tone == 0 && lines[3].text == "2. Sadie  10/10" && lines[3].tone == 1, "panel: time left, the note, our own row live and gold");
        me.End("2:10 1:3");
        Check(!me.Running && me.Report(20_000) == null, "ended: nothing more to say");
        lines = me.Lines("2:10 1:3", 20_000);
        Check(lines.Count == 4 && lines[0].text == "Shooting over" && lines[1].text == "Winner: Sadie" && lines[1].tone == 4 && lines[2].text == "1. Sadie  10/10" && lines[3].text == "2. Arthur  3/10",
              "results: the winner named, then everyone");
        var none = new Shooting(); none.Start("0 0 0 0 1 10 30", 0); none.End("1:0 2:0");
        lines = none.Lines("1:0 2:0", 0);
        Check(lines[1].text == "Nobody broke a bottle" && lines[1].tone == 2, "nobody broke one: no winner named");
        Check(new Shooting().Lines("9:1 x 2: :3 3:99 0:1 junk", 0).Count == 3, "odd standings tokens are skipped");
    }

    static void ShootingRun()
    {
        Stats.Clear();
        string prms = Shooting.Params(new Vec3(0, 0, 0), 0f, 7, 10, 30);
        var host = new Shooting { PosOf = id => new Vec3(5, 0, 0) };
        var run = ContestRun.Open(1, 3, host, prms, 0);
        Check(Sent(run) == "255:open 1-3 shooting " + prms, "opened: the open goes to everyone");
        Check(run.OnNotice(2, "in", "", 1000) == "Co-op: 2 is in", "a partner near us is in");
        run.Tick(5000); Sent(run);
        Check(run.Tick(10_000) == "GO!" && host.Running && Sent(run) == "255:start 1-3|255:standings 1-3 1:0 2:0", "GO: the start and the first standings");
        host.Observe(4, 10_200);
        Check(run.Tick(10_500) == null && Sent(run) == "", "our own score needs no notice");
        run.OnNotice(2, "report", "score 6", 11_000);
        run.OnNotice(3, "report", "score 9", 11_000);   // not in: ignored
        Check(run.Tick(12_000) == null && Sent(run) == "255:standings 1-3 2:6 1:4", "standings every 2 s, best first: " + run.Standings);
        run.Tick(39_999); Sent(run);
        Check(!run.Ended && run.Tick(40_000) == null && run.Ended && Sent(run) == "255:end 1-3 2:6 1:4" && Stats.Get("shoot.wins") == 0, "over at the time; second: no win for us");
        var mine = new Shooting();
        var p = ContestRun.Join(2, 1, "1-3", mine, prms, 0);
        Sent(p);
        Check(p.OnNotice(1, "start", "", 10_000) == "GO!" && mine.Running, "GO from the host");
        mine.Observe(3, 10_200);
        Check(p.Tick(10_200) == null && Sent(p) == "1:report 1-3 score 3", "our score goes to the host");
        p.OnNotice(1, "end", "2:3 1:1", 40_000);
        Check(p.Ended && !mine.Running && Stats.Get("shoot.wins") == 1 && Stats.All().Count == 1, "we won: a shooting win, under the scoreboard's key");
    }

    // ---- mod tables and high card ----
    // Deals a known deck: Next(i + 1) gives back i (no swap) unless the step is listed, so the deck stays in order but for those.
    sealed class RiggedRandom : Random
    {
        readonly System.Collections.Generic.Dictionary<int, int> _swap = new System.Collections.Generic.Dictionary<int, int>();
        public RiggedRandom(params (int i, int j)[] swaps) { foreach (var s in swaps) _swap[s.i] = s.j; }
        public override int Next(int maxValue) => _swap.TryGetValue(maxValue - 1, out int j) ? j : maxValue - 1;
    }

    static string Said(TableRun run) => string.Join("|", run.Notices().Select(m => m.to + ":" + m.text));

    // Every notice the runs have to send reaches the ones it's for, until nobody has more to say. Returns what went: from>to:text.
    static System.Collections.Generic.List<string> Pump(long now, params TableRun[] runs)
    {
        var went = new System.Collections.Generic.List<string>();
        for (int round = 0; round < 20; round++)
        {
            var batch = runs.SelectMany(r => r.Notices().Select(n => (from: r.Me, n.to, n.text))).ToList();
            if (batch.Count == 0) break;
            foreach (var (from, to, text) in batch)
            {
                went.Add($"{from}>{to}:{text}");
                foreach (var r in runs) if (r.Me != from && (to == Protocol.Broadcast || to == r.Me)) r.OnNotice(from, text, now);
            }
        }
        return went;
    }

    static string Name(byte id) => id == 1 ? "Arthur" : id == 2 ? "John" : id == 3 ? "Sadie" : id.ToString();

    static void TableMessages()
    {
        var l = TableMsg.Lines("open 3-1 highcard 100 1.5 -2 30\nseats 3-1 3,5 1\njunk\nact 0-1 x\n\nact 3-1 " + new string('x', 20));
        Check(l.Count == 3 && l[0].verb == "open" && l[1].rest == "3,5 1" && l[2].verb == "act", "good lines kept, bad ones dropped");
        Check(TableMsg.Lines(new string('x', 1200)).Count == 0 && TableMsg.Lines(null).Count == 0, "oversize or no notice: nothing");
        Check(TableMsg.ParseOpen(l[0].rest, out var g, out int st, out var at) && g == "highcard" && st == 100 && at.X == 1.5f && at.Y == -2f && at.Z == 30f, "open parsed");
        Check(TableMsg.Open("3-1", "highcard", 100, new Vec3(1.5f, -2, 30)) == "open 3-1 highcard 100 1.5 -2 30", "open written");
        foreach (var bad in new[] { null, "", "highcard 100 1 2", "highcard 99 1 2 3", "High 100 1 2 3", "highcard 100 NaN 2 3", "highcard 100 1 2 1e9",
                                     "highcard -100 1 2 3", "highcard 100 1 2 3 4", "x1 100 1 2 3", "highcard 100 Infinity 2 3" })
            Check(!TableMsg.ParseOpen(bad, out _, out _, out _), "open refused: " + bad);
        Check(TableMsg.ParseSeats("3,5,7,9 4", out var ids, out int next) && ids.SequenceEqual(new byte[] { 3, 5, 7, 9 }) && next == 4, "seats parsed");
        foreach (var bad in new[] { "3,5,7,9,11 1", "3,3 1", "0 1", "255 1", "3 0", "3", "3,x 1", "3,5 1 2", "-3 1", "", "3, 1" })
            Check(!TableMsg.ParseSeats(bad, out _, out _), "seats refused: " + bad);
        Check(TableMsg.ParsePay("5 -250 3", out byte p, out int c, out int h) && p == 5 && c == -250 && h == 3, "pay parsed");
        foreach (var bad in new[] { "5 -250", "0 1 1", "255 1 1", "5 2000000 1", "5 1 0", "5 1.5 1", "5 1 1 1", "x 1 1", "5 99999999999 1" })
            Check(!TableMsg.ParsePay(bad, out _, out _, out _), "pay refused: " + bad);
        Check(TableMsg.CheckSettlement(new[] { 200, -100, -100 }, new[] { 500, 100, 100 }) == null, "a good settlement");
        Check(TableMsg.CheckSettlement(new[] { 200, -100, -99 }, new[] { 500, 100, 100 }) != null, "one that doesn't add up");
        Check(TableMsg.CheckSettlement(new[] { 101, -101 }, new[] { 500, 100 }) != null, "a seat losing more than it brought");
        Check(TableMsg.CheckSettlement(new[] { 0, 0 }, new[] { 1 }) != null && TableMsg.CheckSettlement(null, new[] { 1 }) != null, "one entry per seat");
        Check(TableMsg.Dollars(250) == "$2.50" && TableMsg.Dollars(-5) == "-$0.05" && TableMsg.CashCap(100) == 5000 && TableMsg.CashCap(2500) == 125_000, "dollars and the cash cap");
        TableRegistry.Register("highcard", () => new HighCard());
        TableRegistry.Register("Bad Name", () => new HighCard());
        Check(TableRegistry.Make("highcard") is HighCard && TableRegistry.Make("nope") == null && !TableRegistry.Has("Bad Name") && !TableRegistry.Has(null), "the registry");
    }

    static void TableSeats()
    {
        var host = TableRun.Open(1, 4, () => new HighCard(), 100, new Vec3(0, 0, 0), 0);
        host.MyCash = 10_000;
        host.PosOf = id => id == 9 ? new Vec3(40, 0, 0) : id == 8 ? (Vec3?)null : new Vec3(3, 0, 0);
        Check(host.Id == "1-4" && host.IAmHost && host.Seated && Said(host) == "255:open 1-4 highcard 100 0 0 0\nseats 1-4 1 1", "opened: open and seats in one notice");
        var a = TableRun.Join(2, 1, "1-4", new HighCard(), 100, new Vec3(0, 0, 0), 0);
        a.MyCash = 3000;
        Check(Said(a) == "1:join 1-4" && !a.Seated, "the join goes to the host");
        a.Tick(1999); Check(Said(a) == "", "quiet"); a.Tick(2000); Check(Said(a) == "1:join 1-4", "again after 2 s");
        host.OnNotice(2, "join 1-4", 2100);
        Check(host.Seats.SequenceEqual(new byte[] { 1, 2 }) && Said(host) == "255:seats 1-4 1,2 1", "seated: everyone told");
        a.OnNotice(1, "seats 1-4 1,2 1", 2200);
        Check(a.Seated && Said(a) == "1:cash 1-4 3000 1", "seated: says what it brings to the next hand");
        a.Tick(5000); Check(Said(a) == "", "no more joins once seated");
        host.OnNotice(9, "join 1-4", 2300); host.OnNotice(8, "join 1-4", 2300);
        Check(!host.Seats.Contains(9) && !host.Seats.Contains(8) && Said(host) == "", "too far away, or nowhere we can see: no seat");
        host.OnNotice(2, "join 1-4", 2400); Check(Said(host) == "255:seats 1-4 1,2 1", "asking again: the seats again");
        foreach (byte id in new byte[] { 3, 4, 5 }) host.OnNotice(id, "join 1-4", 2500);
        Check(host.Seats.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "four seats at most");
        host.Notices();
        host.OnNotice(3, "leave 1-4", 2600); host.OnNotice(4, "leave 1-4", 2600); host.OnNotice(2, "leave 9-9", 2600);
        Check(host.Seats.SequenceEqual(new byte[] { 1, 2 }) && Said(host) == "255:seats 1-4 1,2,4 1\nseats 1-4 1,2 1", "leaves: seats again; another table's line ignored");
        var lines = a.Lines(Name);
        Check(lines[0].text == "Arthur's High card table, $1.00 stakes" && lines[0].tone == 3 && lines[1].text == "Dealing..." && lines.Count == 2, "panel: " + string.Join(" / ", lines.Select(x => x.text)));
        a.MyCash = 50;
        Check(a.Lines(Name).Any(x => x.text == "You need $1.00 in cash to play a hand" && x.tone == 3), "short of the stakes: told");
        // Getting up: the next hand was promised (cash 1), so it waits for that hand's pay while the host is heard (30
        // minutes at most: a whole game of dominoes for 4 takes a while), then it's done.
        a.Leave(3000);
        Check(!a.Seated && a.Settling && Said(a) == "1:leave 1-4", "the leave goes to the host");
        a.Tick(4000); Check(Said(a) == "1:leave 1-4", "and once more, for a lost packet");
        for (long t = 10_000; t < 1_803_000; t += 10_000) { a.OnNotice(1, "seats 1-4 1 1", t); a.Tick(t); }
        Check(!a.Done && a.Settling, "waits for that hand while its host is heard");
        a.Tick(1_803_000); Check(a.Done && a.Why == "left", "30 minutes at most");
        var mute = TableRun.Join(2, 1, "1-6", new HighCard(), 100, default, 0); mute.MyCash = 3000;
        mute.OnNotice(1, "seats 1-6 1,2 1", 0); mute.Leave(100);
        mute.Tick(19_999); Check(mute.Settling, "the host quiet: still waiting"); mute.Tick(20_000); Check(mute.Done && mute.Why == "left", "20 s without a word from the host: done");
        var nobody = TableRun.Join(2, 1, "1-5", new HighCard(), 100, default, 0);
        nobody.Tick(5999); Check(!nobody.Done, "waiting for a seat"); nobody.Tick(6000); Check(nobody.Done && nobody.Why == "no seat", "no seat within 6 s");
        var gone = TableRun.Join(2, 1, "1-6", new HighCard(), 100, default, 0);
        gone.PlayerLeft(3, 10); Check(!gone.Done, "someone else leaving"); gone.PlayerLeft(1, 20); Check(gone.Done && gone.Why == "host left", "the host leaving ends it");
        var quiet = TableRun.Join(2, 1, "1-7", new HighCard(), 100, default, 0);
        quiet.OnNotice(1, "seats 1-7 1,2 1", 100);
        quiet.Tick(20_099); Check(!quiet.Done, "the host was heard"); quiet.Tick(20_100); Check(quiet.Done && quiet.Why == "host gone", "20 s without a word from the host");
        var ended = TableRun.Join(2, 1, "1-8", new HighCard(), 100, default, 0);
        ended.OnNotice(1, "end 1-8 something odd", 100); Check(ended.Done && ended.Why == "closed", "an end with an odd reason reads as closed");
        var lonely = TableRun.Open(1, 9, () => new HighCard(), 100, default, 0);
        lonely.Tick(299_999); Check(!lonely.Done, "open"); lonely.Tick(300_000);
        Check(lonely.Done && lonely.Why == "too few" && Said(lonely).EndsWith("\nend 1-9 too few"), "nobody joins for 5 minutes: closed");
        var shut = TableRun.Open(1, 10, () => new HighCard(), 100, default, 0); shut.Notices();
        shut.Leave(100); Check(shut.Done && shut.Why == "closed" && Said(shut) == "255:end 1-10 closed", "the host getting up closes it");
        var again = TableRun.Open(1, 11, () => new HighCard(), 100, default, 0); again.Notices();
        again.Tick(4999); Check(Said(again) == "", "quiet"); again.Tick(5000); Check(Said(again) == "255:open 1-11 highcard 100 0 0 0\nseats 1-11 1 1", "open and seats again every 5 s");
    }

    static void TableHighCardHand()
    {
        // Seats in order 1, 2, 3 get 2C, 3C, 3D: John and Sadie tie on threes, Arthur (the host) loses.
        var host = TableRun.Open(1, 1, () => new HighCard(), 250, new Vec3(0, 0, 0), 0);
        host.Rng = new RiggedRandom((5, 2), (4, 1));
        host.MyCash = 50_00; host.PosOf = id => new Vec3(2, 0, 0);
        var a = TableRun.Join(2, 1, "1-1", new HighCard(), 250, default, 0); a.MyCash = 20_00;
        var b = TableRun.Join(3, 1, "1-1", new HighCard(), 250, default, 0); b.MyCash = 2_50;
        Pump(0, host, a, b);
        Check(host.Seats.SequenceEqual(new byte[] { 1, 2, 3 }) && a.Seated && b.Seated && a.Seats.SequenceEqual(host.Seats), "all three seated");
        host.Tick(100); var went = Pump(100, host, a, b);   // gathering: seats out, the cash back
        Check(went.Contains("2>1:cash 1-1 2000 1") && went.Contains("3>1:cash 1-1 250 1"), "each brings its cash: " + string.Join(" / ", went));
        host.Tick(200); went = Pump(200, host, a, b);   // everyone answered: dealt at once
        Check(host.LastPublic == "250 0 1:??,2:??,3:??" && a.LastPublic == host.LastPublic && b.LastPublic == host.LastPublic, "everyone sees the same hand: " + host.LastPublic);
        Check(went.Contains("1>2:priv 1-1 3C") && went.Contains("1>3:priv 1-1 3D") && !went.Any(w => w.Contains(">255:") && w.Contains("priv")), "each card goes to its owner alone");
        Check(a.Lines(Name).Any(x => x.text == "John: 3 of clubs (only you see it)" && x.tone == 1) && b.Lines(Name).Any(x => x.text == "John: face down"), "your own card shows to you only");
        Check(a.Options.Count == 0 && host.Options.Count == 1 && host.Options[0].label == "Show my card", "the host shows first");
        a.Act("show", 300); Pump(300, host, a, b); host.Tick(300);
        Check(host.LastPublic == "250 0 1:??,2:??,3:??", "out of turn: nothing happens");
        host.Act("show", 400); host.Tick(1000); Pump(1000, host, a, b);
        Check(a.LastPublic == "250 1 1:2C,2:??,3:??" && a.Options.Count == 1, "Arthur showed: John's turn");
        a.Act("show", 1100); Pump(1100, host, a, b); host.Tick(1700); Pump(1700, host, a, b);
        b.Act("show", 1800); went = Pump(1800, host, a, b); host.Tick(2400); went = Pump(2400, host, a, b);
        Check(b.LastPublic == "250 -1 1:2C,2:3C,3:3D", "over: " + b.LastPublic);
        Check(went.Any(w => w == "1>255:pay 1-1 1 -250 1\npay 1-1 2 125 1\npay 1-1 3 125 1\npub 1-1 250 -1 1:2C,2:3C,3:3D"), "the pays in one notice with the result: " + string.Join(" / ", went));
        Check(host.Paid.SequenceEqual(new[] { -250 }) && a.Paid.SequenceEqual(new[] { 125 }) && b.Paid.SequenceEqual(new[] { 125 }), "each applies its own line: the tie splits the pot");
        Check(host.Paid.Sum() + a.Paid.Sum() + b.Paid.Sum() == 0, "it adds up to zero");
        var lines = b.Lines(Name);
        Check(lines.Last().text == "John and Sadie split the $7.50" && lines.Last().tone == 4 && lines.Any(x => x.text == "Arthur: 2 of clubs" && x.tone == 0), "panel: " + string.Join(" / ", lines.Select(x => x.text)));
        host.Paid.Clear(); a.Paid.Clear(); b.Paid.Clear();
        host.Tick(4400); went = Pump(4400, host, a, b); host.Tick(6400); went.AddRange(Pump(6400, host, a, b));
        Check(went.Count(w => w.Contains("pay 1-1")) == 2 && host.Paid.Count == 0 && a.Paid.Count == 0 && b.Paid.Count == 0, "the pays go twice more and nobody is paid again");
        host.MyCash = 100;   // the host is short now: it sits the next hand out
        host.Tick(7400); went = Pump(7400, host, a, b);
        Check(went.Contains("1>255:seats 1-1 1,2,3 2") && went.Contains("2>1:cash 1-1 2000 2") && went.Contains("3>1:cash 1-1 250 2"), "5 s later: the next hand's cash");
        host.Tick(7500);
        Check(host.LastPublic.StartsWith("250 0 2:??,3:??") && host.Options.Count == 0, "dealt to the two who brought the stakes: " + host.LastPublic);
    }

    static void TableTurnTimeout()
    {
        var host = TableRun.Open(1, 1, () => new HighCard(), 100, default, 0);
        host.Rng = new RiggedRandom((51, 1));   // a fixed deal (2C vs AS): an unseeded one tied the two cards about 1 run in 17 (no pay, test failed)
        host.MyCash = 1000; host.PosOf = id => new Vec3(1, 0, 0);
        var a = TableRun.Join(2, 1, "1-1", new HighCard(), 100, default, 0); a.MyCash = 1000;
        Pump(0, host, a); host.Tick(0); Pump(0, host, a); host.Tick(10); Pump(10, host, a);
        host.Act("show", 20); host.Tick(1000); Pump(1000, host, a);
        Check(a.Options.Count == 1, "John's turn");
        host.Tick(45_999); Pump(45_999, host, a); Check(a.Options.Count == 1, "44.999 s: still his");
        host.Tick(46_000); host.Tick(46_700); Pump(46_700, host, a);
        Check(a.Options.Count == 0 && a.LastPublic.StartsWith("100 -1 ") && host.Paid.Sum() + a.Paid.Sum() == 0 && host.Paid.Count + a.Paid.Count == 2,
              "45 s after his turn came the table showed for him and settled: " + a.LastPublic);
        // A game whose default move is refused forfeits the seat instead.
        var g = new HighCard();
        g.Begin(new byte[] { 1, 2, 3 }, 100, new[] { 100, 100, 100 }, new Random(1));
        g.Tick(0); g.Tick(45_000);
        Check(g.Turn == 1 && g.Public().Split(' ')[2].Split(',')[0] != "1:??", "seat 0 timed out: shown for it");
        g.Tick(45_001); g.Tick(90_000); Check(g.Turn == 1, "a fresh 45 s for the next seat"); g.Tick(90_001); Check(g.Turn == 2, "then it moves too");
    }

    static void TableLeaveForfeits()
    {
        var host = TableRun.Open(1, 1, () => new HighCard(), 100, default, 0);
        host.Rng = new RiggedRandom((51, 1), (6, 2));   // seats 1, 2, 3 get 2C, AS, 3H: John would win
        host.MyCash = 1000; host.PosOf = id => new Vec3(1, 0, 0);
        var a = TableRun.Join(2, 1, "1-1", new HighCard(), 100, default, 0); a.MyCash = 1000;
        var b = TableRun.Join(3, 1, "1-1", new HighCard(), 100, default, 0); b.MyCash = 1000;
        Pump(0, host, a, b); host.Tick(0); Pump(0, host, a, b); host.Tick(10); Pump(10, host, a, b);
        host.Act("show", 20); host.Tick(700); Pump(700, host, a, b);
        Check(a.Options.Count == 1 && a.Lines(Name).Any(x => x.text == "John: Ace of spades (only you see it)"), "John's turn, holding the ace");
        a.Leave(800); Pump(800, host, a, b);
        Check(a.Settling && !a.Seated && host.Seats.SequenceEqual(new byte[] { 1, 3 }), "John got up mid-hand: unseated, waiting for the pay");
        host.Tick(1400); Pump(1400, host, a, b);
        Check(b.LastPublic == "100 2 1:2C,2:--,3:??" && b.Options.Count == 1, "his hand is forfeit: Sadie's turn");
        b.Act("show", 1500); Pump(1500, host, a, b); host.Tick(2100); Pump(2100, host, a, b);
        Check(host.Paid.SequenceEqual(new[] { -100 }) && b.Paid.SequenceEqual(new[] { 200 }) && a.Paid.SequenceEqual(new[] { -100 }) && a.Done && a.Why == "left",
              "the ace couldn't win: Sadie takes the pot, John pays his ante though he left");
        Check(!a.Rejoin(2200), "a table we're done with can't be rejoined");
        // Up mid-hand and straight back: the same run sits down again, and the forfeited hand is paid once.
        var r = TableRun.Join(2, 1, "1-9", new HighCard(), 100, default, 0); r.MyCash = 1000;
        r.OnNotice(1, "seats 1-9 1,2 1", 0); r.Notices(); r.Leave(10); r.Notices();
        Check(r.Settling && r.Rejoin(20) && !r.Settling && Said(r) == "1:join 1-9", "back: asks for its seat again");
        r.OnNotice(1, "seats 1-9 1,2 2", 30); r.Notices();
        r.OnNotice(1, "pay 1-9 2 -100 1\npay 1-9 1 100 1", 40); r.OnNotice(1, "pay 1-9 2 -100 1\npay 1-9 1 100 1", 50);
        Check(r.Seated && r.Paid.SequenceEqual(new[] { -100 }), "the hand it left is paid, once");
        // The same when a player leaves the session mid-hand (the roster says so).
        var h2 = TableRun.Open(1, 2, () => new HighCard(), 100, default, 0);
        h2.MyCash = 1000; h2.PosOf = id => new Vec3(1, 0, 0);
        var c = TableRun.Join(2, 1, "1-2", new HighCard(), 100, default, 0); c.MyCash = 1000;
        Pump(0, h2, c); h2.Tick(0); Pump(0, h2, c); h2.Tick(10); Pump(10, h2, c);
        h2.PlayerLeft(2, 20); h2.Tick(700);
        Check(h2.LastPublic.StartsWith("100 -1 ") && h2.Paid.SequenceEqual(new[] { 100 }) && h2.Seats.SequenceEqual(new byte[] { 1 }), "gone mid-hand: forfeit, the host wins it: " + h2.LastPublic);
        // Walking away: the host unseats a player it sees 20 m off.
        var h3 = TableRun.Open(1, 3, () => new HighCard(), 100, default, 0);
        Vec3 johnAt = new Vec3(1, 0, 0);
        h3.MyCash = 1000; h3.PosOf = id => johnAt;
        h3.OnNotice(2, "join 1-3", 0);
        johnAt = new Vec3(19, 0, 0); h3.Tick(1000); Check(h3.Seats.Contains(2), "19 m: still seated");
        johnAt = new Vec3(21, 0, 0); h3.Tick(2000); Check(!h3.Seats.Contains(2), "21 m: walked away");
        var d = TableRun.Join(2, 1, "1-3", new HighCard(), 100, default, 0); d.MyCash = 1000;
        d.OnNotice(1, "seats 1-3 1,2 1", 0); d.OnNotice(1, "seats 1-3 1 1", 100);
        Check(!d.Seated && d.Settling, "a player the host unseated knows it");
        // The host getting up mid-hand forfeits it: heads-up the other wins and is paid with the end; with more in, void.
        var h4 = TableRun.Open(1, 4, () => new HighCard(), 100, default, 0);
        h4.MyCash = 1000; h4.PosOf = id => new Vec3(1, 0, 0);
        var e = TableRun.Join(2, 1, "1-4", new HighCard(), 100, default, 0); e.MyCash = 1000;
        Pump(0, h4, e); h4.Tick(0); Pump(0, h4, e); h4.Tick(10); Pump(10, h4, e);
        h4.Leave(20, "host left"); Pump(20, h4, e);
        Check(h4.Done && e.Done && e.Why == "host left" && h4.Paid.SequenceEqual(new[] { -100 }) && e.Paid.SequenceEqual(new[] { 100 }), "host left heads-up: it loses, John is paid with the end");
        var h5 = TableRun.Open(1, 5, () => new HighCard(), 100, default, 0);
        h5.MyCash = 1000; h5.PosOf = id => new Vec3(1, 0, 0);
        var f1 = TableRun.Join(2, 1, "1-5", new HighCard(), 100, default, 0); f1.MyCash = 1000;
        var f2 = TableRun.Join(3, 1, "1-5", new HighCard(), 100, default, 0); f2.MyCash = 1000;
        Pump(0, h5, f1, f2); h5.Tick(0); Pump(0, h5, f1, f2); h5.Tick(10); Pump(10, h5, f1, f2);
        Check(h5.LastPublic != null && h5.LastPublic.Split(' ')[2].Split(',').Length == 3, "three in the hand: " + h5.LastPublic);
        h5.Leave(20, "host left"); Pump(20, h5, f1, f2);
        Check(h5.Done && f1.Done && f2.Done && h5.Paid.Count == 0 && f1.Paid.Count == 0 && f2.Paid.Count == 0, "host left, two still in: void");
    }

    static void TableHostile()
    {
        var a = TableRun.Join(2, 1, "1-1", new HighCard(), 100, default, 0); a.MyCash = 3000; a.Notices();
        a.OnNotice(1, "seats 1-1 1,2 1", 0); a.Notices();
        a.OnNotice(5, "pay 1-1 2 500 1\npay 1-1 1 -500 1", 10); Check(a.Paid.Count == 0, "pays from someone not the host: ignored");
        a.OnNotice(5, "seats 1-1 1,5 1\npub 1-1 100 0 1:??,2:??\nend 1-1 closed", 10);
        Check(a.Seated && a.LastPublic == null && !a.Done, "seats, pub, end from someone not the host: ignored");
        a.OnNotice(1, "pay 1-1 2 500 1\npay 1-1 1 -400 1", 20); Check(a.Paid.Count == 0, "pays that don't add up: dropped");
        a.OnNotice(1, "pay 1-1 2 500 1\npay 1-1 1 -500 1\npay 1-1 1 0 1", 20); Check(a.Paid.Count == 0, "a player paid twice in one hand: dropped");
        a.OnNotice(1, "pay 1-1 2 500 1\npay 1-1 1 -500 2", 20); Check(a.Paid.Count == 0, "two hands mixed: dropped");
        a.OnNotice(1, "pay 1-1 2 500 1\npay 1-1 1 x 1", 20); Check(a.Paid.Count == 0, "a garbled line spoils the lot");
        a.OnNotice(1, "pay 1-1 2 -500 7\npay 1-1 1 500 7", 30); Check(a.Paid.Count == 0, "a hand we brought nothing to: ignored");
        a.OnNotice(1, "pay 1-1 2 -3001 1\npay 1-1 1 3001 1", 40); Check(a.Paid.Count == 0 && a.Note.Contains("refused"), "asked for more than we brought: refused");
        a.OnNotice(1, "pay 1-1 2 -100 1\npay 1-1 1 100 1", 50); Check(a.Paid.Count == 0, "hand 1 is settled (refused counts): no second go");
        a.OnNotice(1, "seats 1-1 1,2 2", 4000); a.Notices();
        a.OnNotice(1, "pay 1-1 3 100 2\npay 1-1 1 -100 2", 5000); Check(a.Paid.Count == 0, "pay lines for other players never move our money");
        a.OnNotice(1, "seats 1-1 1,2 3", 9000); a.Notices();
        a.OnNotice(1, "pay 1-1 2 1000000 3\npay 1-1 1 -1000000 3", 10_000); Check(a.Paid.Count == 0, "a win beyond what the others could bring: refused");
        a.OnNotice(1, "seats 1-1 1,2 4", 14_000); a.Notices();
        a.OnNotice(1, "pay 1-1 2 -100 4\npay 1-1 1 100 4", 15_000); a.OnNotice(1, "pay 1-1 2 -100 4\npay 1-1 1 100 4", 15_010);
        Check(a.Paid.SequenceEqual(new[] { -100 }), "a good pay, applied once though it came twice");
        // A host inventing hands to drain us: one settlement every 4 s at most (a real hand takes longer); a repeat later still counts.
        a.Paid.Clear();
        a.OnNotice(1, "seats 1-1 1,2 5\npay 1-1 2 -100 5\npay 1-1 1 100 5", 15_100);
        a.OnNotice(1, "seats 1-1 1,2 6\npay 1-1 2 -100 6\npay 1-1 1 100 6", 15_200); a.Notices();
        Check(a.Paid.Count == 0 && a.Note.Contains("too soon"), "hands paid 0.1 s apart: refused");
        a.OnNotice(1, "pay 1-1 2 -100 5\npay 1-1 1 100 5", 19_000); a.OnNotice(1, "pay 1-1 2 -100 6\npay 1-1 1 100 6", 19_100);
        Check(a.Paid.SequenceEqual(new[] { -100 }), "the repeat 4 s on: that hand paid, the next still too soon");
        a.OnNotice(1, "pub 1-1 garbage that HighCard can't read\npriv 1-1 ZZ", 130);
        Check(a.Options.Count == 0 && a.Lines(Name).Count >= 1, "a state the game can't read changes nothing");
        a.OnNotice(1, "pub 2-1 100 0 1:??,2:??", 140); Check(a.LastPublic.StartsWith("garbage"), "another table's lines: ignored");
        a.OnNotice(1, "pub 1-1 " + new string('x', 950), 150); Check(a.LastPublic.StartsWith("garbage"), "an oversize state: ignored");
        // The host's side.
        var host = TableRun.Open(1, 1, () => new HighCard(), 100, default, 0);
        host.MyCash = 1000; host.PosOf = id => new Vec3(1, 0, 0);
        host.OnNotice(2, "join 1-1", 0); host.OnNotice(3, "join 1-1", 0); host.Tick(0); host.Notices();
        host.OnNotice(2, "cash 1-1 -5 1\ncash 1-1 5x 1\ncash 1-1 500 9", 10); host.OnNotice(4, "cash 1-1 500 1", 10);
        host.OnNotice(2, "cash 1-1 99999999 1", 10);
        host.Tick(20);
        Check(host.LastPublic == null, "Sadie hasn't answered properly yet: waiting");
        host.OnNotice(3, "cash 1-1 50 1", 30); host.Tick(40);
        Check(host.LastPublic == "100 0 1:??,2:??", "everyone answered: dealt to those who brought the stakes (John's capped at 50 x stakes; Sadie's too short; 4 isn't seated)");
        host.OnNotice(3, "act 1-1 show", 5100); host.OnNotice(2, "act 1-1 show", 5100); host.OnNotice(2, "act 1-1 " + new string('s', 65), 5100);
        host.Tick(6000); Check(host.LastPublic == "100 0 1:??,2:??", "acts out of turn or from a seat not in the hand: nothing");
        host.OnNotice(1, "act 1-1 show", 6100); host.Tick(7000); Check(host.LastPublic == "100 0 1:??,2:??", "a notice claiming to be from us: ignored");
    }

    // ---- liar's dice ----
    // Gives back its numbers in turn, then 0s: Next(n) for the first player, then Next(6) for each die (0 is a one).
    sealed class ScriptedRandom : Random
    {
        readonly int[] _v; int _i;
        public ScriptedRandom(params int[] v) { _v = v; }
        public override int Next(int maxValue) => _i < _v.Length ? _v[_i++] % maxValue : 0;
    }

    // The first player, then each seat's dice as digits (sorted or not), for ScriptedRandom.
    static int[] Roll(int first, params string[] dice) => new[] { first }.Concat(dice.SelectMany(d => d.Select(c => c - '1'))).ToArray();

    static void LiarsDiceBids()
    {
        Check(LiarsDice.Legal(1, 1, 0, 0, 10) && LiarsDice.Legal(3, 4, 3, 3, 10) && LiarsDice.Legal(4, 1, 3, 6, 10), "raise the quantity, or the face at the same quantity");
        Check(!LiarsDice.Legal(3, 3, 3, 3, 10) && !LiarsDice.Legal(2, 6, 3, 1, 10) && !LiarsDice.Legal(11, 2, 0, 0, 10) && !LiarsDice.Legal(0, 2, 0, 0, 10)
              && !LiarsDice.Legal(2, 0, 0, 0, 10) && !LiarsDice.Legal(2, 7, 0, 0, 10), "the same bid, lower, more than the dice there are, odd faces: no");
        Check(LiarsDice.Count(new[] { "11234", "5566" }, 5) == 4 && LiarsDice.Count(new[] { "11234", "5566" }, 1) == 2 && LiarsDice.Count(new[] { "1234", "11111", "66666" }, 6) == 11,
              "ones count for any face, but only as ones when ones are bid");
        var open = LiarsDice.Bids(0, 0, 10);
        Check(open.Count == 10 && open[0] == (3, 2) && open[4] == (3, 6) && open[9] == (4, 6), "an opening: a third of the dice of each face but ones, and one more");
        Check(LiarsDice.Bids(3, 4, 10).SequenceEqual(new[] { (3, 5), (3, 6), (4, 2), (4, 3), (4, 4), (4, 5), (4, 6) }), "higher faces, then one more of each face, never ones");
        Check(LiarsDice.Bids(3, 1, 10).First() == (3, 2) && LiarsDice.Bids(0, 0, 10).All(b => b.face != 1), "after a bid on ones: twos up");
        Check(LiarsDice.Bids(10, 6, 10).Count == 0 && LiarsDice.Bids(10, 5, 10).SequenceEqual(new[] { (10, 6) }) && LiarsDice.Bids(0, 0, 2).Count == 10, "nothing past the dice there are");
        Check(LiarsDice.Say(1, 6) == "1 six" && LiarsDice.Say(4, 5) == "4 fives" && LiarsDice.Say(2, 1) == "2 ones", "bids said plainly");

        var g = new LiarsDice();
        Check(g.Name == "dice" && g.Title == "Liar's dice" && g.MinPlayers == 2 && !g.Over, "the game");
        g.Begin(new byte[] { 1, 2, 3 }, 100, new[] { 500, 500, 500 }, new ScriptedRandom(Roll(1, "11234", "23456", "55566")));
        Check(g.Public() == "100 1 1 - 1:5,2:5,3:5 -" && g.Private(0) == "1 11234" && g.Private(1) == "1 23456" && g.Private(2) == "1 55566" && g.Private(3) == null,
              "five dice each, only the count public, each knows its own: " + g.Public());
        Check(g.Turn == 1 && g.DefaultAction(1) == "bid 1 2", "John opens; the lowest bid (not on ones) by default");
        Check(!g.Act(0, "bid 3 5") && !g.Act(1, "liar") && !g.Act(1, "spot") && !g.Act(1, "bid 0 5") && !g.Act(1, "bid 16 5") && !g.Act(1, "bid 3 7")
              && !g.Act(1, "bid 3") && !g.Act(1, "bid -3 5") && !g.Act(1, null), "out of turn, a call with no bid, odd bids: refused");
        Check(g.Act(1, "bid 4 5") && g.Turn == 2 && !g.Act(2, "bid 4 5") && !g.Act(2, "bid 3 6") && g.Act(2, "bid 4 6") && g.Turn == 0, "bids go round, each higher");
        Check(g.DefaultAction(0) == "bid 5 2" && g.Public() == "100 0 1 4,6,2 1:5,2:5,3:5 -", "past sixes the lowest raise is one more two: " + g.Public());
    }

    static void LiarsDiceCalls()
    {
        // Round 1: 4 sixes is true (two wild ones, a six, two sixes): Arthur's Liar! costs him a die and he starts.
        var g = new LiarsDice();
        g.Begin(new byte[] { 1, 2, 3 }, 100, new[] { 500, 500, 500 }, new ScriptedRandom(Roll(1, "11234", "23456", "55566").Concat(Roll(0, "1234", "11111", "66666").Skip(1))
                                                                         .Concat(Roll(0, "666", "22222", "3344").Skip(1)).Concat(Roll(0, "234", "23456", "345").Skip(1)).ToArray()));
        g.Act(1, "bid 4 5"); g.Act(2, "bid 4 6");
        Check(g.Act(0, "liar") && g.Public() == "100 0 2 - 1:4,2:5,3:5 L,0,2,4,6,11234/23456/55566" && g.Private(0) == "2 1234", "a wrong Liar!: the caller loses a die, starts, fresh dice: " + g.Public());
        // Round 2: 6 ones are there exactly (ones aren't wild when they're bid): John's Spot on costs the others a die each.
        Check(g.Act(0, "bid 6 1") && g.Act(1, "spot") && g.Public() == "100 1 3 - 1:3,2:5,3:4 S,1,0,6,1,1234/11111/66666", "a right Spot on: everyone else loses a die, the caller starts: " + g.Public());
        // Round 3: 3 threes is wrong (two threes, no ones): Sadie's Spot on costs her; then a true bid called Liar! costs the bidder.
        Check(g.Act(1, "bid 3 3") && g.Act(2, "spot") && g.Public() == "100 2 4 - 1:3,2:5,3:3 S,2,1,3,3,666/22222/3344", "a wrong Spot on: the caller loses a die: " + g.Public());
        Check(g.Act(2, "bid 1 2") && g.Act(0, "bid 20 1") == false && g.Act(0, "bid 11 6") && g.Act(1, "liar") && g.Public().StartsWith("100 0 5 - 1:2,2:5,3:3 L,1,0,11,6,"),
              "a false bid called Liar!: the bidder loses a die and starts: " + g.Public());

        // Elimination and the winner. All ones from here on, so every bid is true and every Liar! costs the caller.
        var e = new LiarsDice();
        e.Begin(new byte[] { 1, 2, 3 }, 100, new[] { 500, 500, 500 }, new ScriptedRandom());
        Func<int, bool> sadieCalls = s => s == 2;
        for (int k = 0; k < 40 && !e.Public().Contains(",3:0 "); k++) e.Act(e.Turn, sadieCalls(e.Turn) && e.Public().Split(' ')[3] != "-" ? "liar" : e.DefaultAction(e.Turn));
        Check(e.Public().StartsWith("100 0 ") && e.Public().Contains(" 1:5,2:5,3:0 ") && !e.HandOver && e.Private(2) == null, "Sadie's out, the next player along starts: " + e.Public());
        for (int k = 0; k < 40 && !e.HandOver; k++) e.Act(e.Turn, e.Turn == 1 && e.Public().Split(' ')[3] != "-" ? "liar" : e.DefaultAction(e.Turn));
        Check(e.HandOver && e.Turn == -1 && e.Public().Contains(" 1:5,2:0,3:0 ") && e.Settlement().SequenceEqual(new[] { 200, -100, -100 }), "the last with dice takes the pot: " + e.Public());
        Check(!e.Act(0, "bid 1 1") && e.Settlement().Sum() == 0, "over: no more moves; it adds up to zero");
        e.Forfeit(0); Check(e.Settlement().SequenceEqual(new[] { 200, -100, -100 }), "once it's over a forfeit changes nothing");
        var v = new LiarsDice(); v.OnPublic(e.Public());
        var lines = v.Lines(2, Name);
        Check(lines.Last().text == "Arthur wins the $3.00" && lines.Last().tone == 4 && lines.Any(x => x.text == "Sadie: out" && x.tone == 2) && v.Options(1).Count == 0, "view: " + string.Join(" / ", lines.Select(x => x.text)));

        // A player leaving loses their dice: the round starts again, the next player along if it was theirs.
        var f = new LiarsDice();
        f.Begin(new byte[] { 1, 2, 3 }, 250, new[] { 500, 500, 500 }, new ScriptedRandom());
        f.Act(0, "bid 3 2");
        f.Forfeit(1);
        Check(f.Public() == "250 2 2 - 1:5,2:-,3:5 -" && f.Private(1) == null, "John left on his turn: fresh dice, Sadie to open: " + f.Public());
        f.Forfeit(0);
        Check(f.HandOver && f.Settlement().SequenceEqual(new[] { -250, -250, 500 }), "one left with dice: they take it, the leavers' stakes too");
        var two = new LiarsDice();
        two.Begin(new byte[] { 1, 2 }, 100, new[] { 100, 100 }, new ScriptedRandom(1));
        two.Forfeit(0); Check(two.HandOver && two.Settlement().SequenceEqual(new[] { -100, 100 }), "of two, one leaves: the other wins");
    }

    static void LiarsDiceView()
    {
        var g = new LiarsDice();
        g.Begin(new byte[] { 1, 2, 3, 4 }, 100, new[] { 500, 500, 500, 500 }, new ScriptedRandom(Roll(1, "11234", "23456", "55566", "12345")));
        g.Act(1, "bid 4 5"); g.Act(2, "bid 5 6"); g.Act(3, "liar");   // 5 sixes: two wild ones, a six, two sixes, a wild one: 6 there
        var v = new LiarsDice(); v.OnPublic(g.Public()); v.OnPrivate(g.Private(3));
        var lines = v.Lines(4, Name);
        Check(lines.Count == 8 && lines[0].text == "$1.00 each, ones wild: the last with dice takes $4.00" && lines[1].text == "Arthur: 5 dice, showed 1 1 2 3 4"
              && lines[4].text == "4: 4 dice, showed 1 2 3 4 5" && lines[4].tone == 1 && lines[5].text.StartsWith("Your dice: ") && lines[5].tone == 1
              && lines[6].text == "4: Liar! 5 sixes, there were 6: 4 lost a die" && lines[7].text == "New dice: your bid to open", "the panel, 8 lines for 4: " + string.Join(" / ", lines.Select(x => x.text)));
        Check(v.MyDice == g.Private(3).Split(' ')[1] && v.DiceInPlay == 19 && v.BidQuantity == 0, "the bot's view");
        var o = v.Options(4);
        Check(o.Count == 10 && o[0] == ("Bid 6 twos", "bid 6 2") && v.Options(1).Count == 0, "the opener's bids, nobody else's: " + string.Join(", ", o.Select(x => x.label)));
        v.OnPrivate("1 11234");
        Check(v.MyDice == "" && !v.Lines(4, Name).Any(x => x.text.StartsWith("Your dice")), "dice from another round don't show");
        g.Act(3, "bid 6 4");
        v.OnPublic(g.Public());
        o = v.Options(1);
        Check(o.Count == 9 && o[0] == ("Bid 6 fives", "bid 6 5") && o[2] == ("Bid 7 twos", "bid 7 2") && o[7] == ("Liar! (6 fours is too many)", "liar") && o[8] == ("Spot on (exactly 6 fours)", "spot")
              && v.Lines(4, Name).Last().text == "4 bid 6 fours: Arthur to raise or call" && v.Lines(1, Name).Last().text == "4 bid 6 fours: raise it, or call Liar! or Spot on",
              "an answer: raise or call: " + string.Join(", ", o.Select(x => x.label)));
        string good = g.Public();
        foreach (var bad in new[] { null, "", "garbage", good + " x", "100 0 1 - 1:5,2:5", "100 7 1 - 1:5,2:5 -", "100 1 1 - 1:5,2:0 -", "100 0 1 11,2,0 1:5,2:5 -", "100 0 1 3,7,0 1:5,2:5 -",
                                     "100 0 1 3,2,5 1:5,2:5 -", "100 0 1 - 0:5,2:5 -", "100 0 1 - 1:5,1:5 -", "100 0 1 - 1:6,2:5 -", "100 0 1 - 1:5,2:5,3:5,4:5,5:5 -", "100 0 0 - 1:5,2:5 -",
                                     "-100 0 1 - 1:5,2:5 -", "100 0 1 - 1:5,2:5 L,0,1,3,2,123", "100 0 1 - 1:5,2:5 L,0,1,3,2,123/178", "100 0 1 - 1:5,2:5 X,0,1,3,2,1/2", "100 0 1 - 1:5,2:5 L,0,9,3,2,1/2",
                                     "100 0 1 - 1:5,2:5 L,0,1,3,2,123456/1", "100 0 1 - 255:5,2:5 -" })
        {
            var w = new LiarsDice(); w.OnPublic(good); w.OnPublic(bad);
            Check(w.Options(1).Count == 9, "refused, the last good state kept: " + bad);
        }
        foreach (var bad in new[] { "1 1237", "x 123", "1 123456", "1", "1 ", null })
        {
            var w = new LiarsDice(); w.OnPublic("100 0 1 - 1:5,2:5 -"); w.OnPrivate("1 12345"); w.OnPrivate(bad);
            Check(w.MyDice == "", "odd dice dropped: " + bad);
        }
    }

    // A whole game at a table: the host's rolls, each player's dice to them alone, a settlement that adds up.
    static void TableLiarsDiceGame()
    {
        var host = TableRun.Open(1, 1, () => new LiarsDice(), 100, new Vec3(0, 0, 0), 0);
        host.Rng = new Random(7); host.MyCash = 50_00; host.PosOf = id => new Vec3(2, 0, 0);
        var a = TableRun.Join(2, 1, "1-1", new LiarsDice(), 100, default, 0); a.MyCash = 20_00;
        var b = TableRun.Join(3, 1, "1-1", new LiarsDice(), 100, default, 0); b.MyCash = 1_00;
        Pump(0, host, a, b); host.Tick(100); Pump(100, host, a, b); host.Tick(200);
        var went = Pump(200, host, a, b);
        Check(host.LastPublic.EndsWith(" 1:5,2:5,3:5 -") && a.LastPublic == host.LastPublic && b.LastPublic == host.LastPublic, "dealt to all three: " + host.LastPublic);
        long now = 200;
        for (int k = 0; k < 3000 && host.LastPublic.Split(' ')[1] != "-1"; k++)
        {
            now += 700;
            foreach (var r in new[] { host, a, b })
            {
                var o = r.Options;
                if (o.Count == 0) continue;
                var view = (LiarsDice)r.Game;
                r.Act(view.BidQuantity * 2 > view.DiceInPlay ? "liar" : o[0].action, now);
            }
            went.AddRange(Pump(now, host, a, b)); host.Tick(now); went.AddRange(Pump(now, host, a, b));
        }
        Check(host.LastPublic.Split(' ')[1] == "-1" && a.LastPublic == host.LastPublic, "the game ran to its end: " + host.LastPublic);
        Check(went.Any(w => w.StartsWith("1>2:priv 1-1 ")) && went.Any(w => w.StartsWith("1>3:priv 1-1 ")) && !went.Any(w => w.Contains(">255:") && w.Contains("priv")), "dice go to their owner alone");
        Check(went.All(w => w.Length < 1100) && host.Paid.Count + a.Paid.Count + b.Paid.Count == 3 && host.Paid.Sum() + a.Paid.Sum() + b.Paid.Sum() == 0
              && new[] { host, a, b }.Count(r => r.Paid.SequenceEqual(new[] { 200 })) == 1, "everyone paid the stakes, the winner took $3.00, it adds up");
    }

    static void HighCardRules()
    {
        var g = new HighCard();
        Check(g.Name == "highcard" && g.Title == "High card" && g.MinPlayers == 2 && !g.Over, "the sample game");
        g.Begin(new byte[] { 1, 2, 3 }, 25, new[] { 100, 100, 100 }, new RiggedRandom((5, 2), (4, 1)));
        Check(g.Public() == "25 0 1:??,2:??,3:??" && g.Private(0) == "2C" && g.Private(1) == "3C" && g.Private(2) == "3D" && g.Private(3) == null, "dealt face down, each knows its own");
        Check(g.Turn == 0 && !g.HandOver && g.DefaultAction(0) == "show", "seat 0 shows first");
        Check(!g.Act(1, "show") && !g.Act(0, "fold") && g.Act(0, "show") && g.Turn == 1 && !g.Act(0, "show"), "in turn only, show only, once");
        g.Act(1, "show"); g.Act(2, "show");
        Check(g.HandOver && g.Public() == "25 -1 1:2C,2:3C,3:3D" && g.Settlement().SequenceEqual(new[] { -25, 13, 12 }), "a tie splits the pot, the odd cent to the first seat");
        g.Forfeit(2); Check(g.Settlement().SequenceEqual(new[] { -25, 13, 12 }), "once it's over a forfeit changes nothing");
        var v = new HighCard();
        v.OnPublic(g.Public());
        var lines = v.Lines(2, Name);
        Check(lines.Count == 5 && lines[2].text == "John: 3 of clubs" && lines[2].tone == 4 && lines[1].tone == 0 && lines[4].text == "John and Sadie split the $0.75", "view: " + string.Join(" / ", lines.Select(x => x.text)));
        foreach (var bad in new[] { "garbage", "25 5 1:??", "25 0 1:ZZ", "25 0 0:??", "25 0 1:??,2:??,3:??,4:??,5:??", "-25 0 1:??", "25 0 1??", null })
        {
            var w = new HighCard(); w.OnPublic("25 0 1:??,2:??"); w.OnPublic(bad);
            Check(w.Options(1).Count == 1, "refused, the last good state kept: " + bad);
        }
        var f = new HighCard();
        f.Begin(new byte[] { 1, 2, 3 }, 100, new[] { 100, 100, 100 }, new Random(3));
        f.Forfeit(0); Check(f.Turn == 1 && f.Public().StartsWith("100 1 1:--,"), "a forfeit before your turn: skipped");
        f.Forfeit(2); Check(f.HandOver && f.Settlement().SequenceEqual(new[] { -100, 200, -100 }), "one left in: they take it unshown");
        var all = new HighCard();
        all.Begin(new byte[] { 1, 2 }, 100, new[] { 100, 100 }, new Random(3));
        all.Forfeit(0); Check(all.HandOver && all.Settlement().SequenceEqual(new[] { -100, 100 }), "of two, one forfeits: the other wins");
        var w2 = new HighCard(); w2.OnPublic("100 -1 1:--,2:--");
        Check(w2.Lines(1, Name).Last().text == "Nobody's left in: the antes go back", "nobody left: no winner named");
    }

    static System.Collections.Generic.List<(int a, int b)> Tiles(params int[] t) => t.Select(x => (x / 10, x % 10)).ToList();

    static void DominoBoardRules()
    {
        var b = new DominoBoard();
        Check(b.Can((6, 3), 'L') && !b.Can((6, 3), 'U') && b.Plays(Tiles(63, 21)).Count == 2 && b.Count() == 0, "an empty board takes any tile, on L");
        b.Place((6, 3), 'L');
        Check(b.Count() == 9 && b.Spin < 0 && !b.Can((5, 2), 'L') && b.Can((3, 3), 'R') && !b.Can((3, 3), 'L'), "the lead counts both its pips; a tile must match its end");
        b.Place((3, 3), 'R');
        Check(b.Spin == 1 && b.Count() == 12 && !b.ArmsOpen && b.Ends().Count == 2 && !b.Can((3, 5), 'U'), "the first double spins and counts both halves at the end; no arms yet");
        b.Place((1, 3), 'R');
        Check(b.ArmsOpen && b.Line[2] == (3, 1) && b.Count() == 7 && new string(b.Ends().Select(e => e.end).ToArray()) == "LRUD", "both its sides taken: four ends, the spinner itself no longer counts");
        b.Place((5, 3), 'U');
        Check(b.Up[0] == (3, 5) && b.Count() == 12, "up from the spinner: its end counts");
        b.Place((6, 6), 'L');
        Check(b.Line[0] == (6, 6) && b.Spin == 2 && b.Count() == 18, "another double at an end counts both halves; the spinner keeps its tile");
        b.Place((4, 3), 'D'); b.Place((1, 1), 'R');
        Check(b.Down[0] == (3, 4) && b.Spin == 2 && b.Count() == 12 + 2 + 5 + 4, "only the first double spins");
        var d = new DominoBoard(); d.Place((5, 5), 'L');
        Check(d.Spin == 0 && d.Count() == 10, "a double lead is the spinner: 10");
        d.Place((5, 0), 'L');
        Check(d.Spin == 1 && d.Line[0] == (0, 5) && d.Count() == 10 && !d.ArmsOpen, "one side taken: the spinner still counts both halves");
        d.Place((2, 5), 'R');
        Check(d.ArmsOpen && d.Count() == 2 && d.Plays(Tiles(52)).Select(p => p.end).SequenceEqual(new[] { 'R', 'U', 'D' }), "both taken: 0 + 2; [5|2] goes right, up or down");
    }

    static void DominoesRules()
    {
        foreach (int n in new[] { 2, 3, 4 })
        {
            var g = new Dominoes();
            g.Begin(new byte[] { 1, 2, 3, 4 }.Take(n).ToList(), 100, Enumerable.Repeat(100, n).ToList(), new Random(n));
            var hands = Enumerable.Range(0, n).Select(i => g.Private(i).Split(' ')[1].Split('.')).ToList();
            int each = n == 2 ? 7 : 5, bone = int.Parse(g.Public().Split(' ')[2]);
            Check(hands.All(h => h.Length == each) && bone == 28 - n * each && hands.SelectMany(h => h).Distinct().Count() == n * each && g.Turn >= 0, $"{n} players: {each} each, the rest the boneyard");
        }
        var r = new Dominoes();
        Check(r.Name == "dominoes" && r.Title == "Dominoes" && r.MinPlayers == 2 && !r.Over, "the game");
        r.Begin(new byte[] { 1, 2 }, 100, new[] { 5000, 5000 }, new Random(1));
        r.Deal(new[] { Tiles(55, 52, 64), Tiles(50, 21, 66) }, Tiles(33, 44), 0);
        Check(r.Turn == 0 && !r.Act(1, "play 50 L") && !r.Act(0, "play 61 L") && !r.Act(0, "draw") && !r.Act(0, "pass") && !r.Act(0, "play 55 U") && !r.Act(0, "junk"), "in turn, own tiles, draw or pass only when stuck");
        Check(r.Act(0, "play 55 L") && r.Turn == 1 && r.Scores[0] == 10, "the double five leads: 10 scored");
        Check(!r.Act(1, "play 21 L") && r.Act(1, "play 05 R") && r.Scores[1] == 10, "a tile must match; [5|0] on the spinner: 10 + 0");
        Check(!r.Act(0, "play 52 U") && r.Act(0, "play 25 L") && r.Scores[0] == 10 && r.Public().EndsWith(" 25.55.50 1 - - -"), "no arms before both sides; 2 + 0 scores nothing: " + r.Public());
        Check(r.Act(1, "play 12 L") && r.Turn == 0, "[2|1] on the left");
        Check(!r.Act(0, "pass") && r.Act(0, "draw") && r.Turn == 0 && r.Private(0).EndsWith(" 64.44.33") && r.Public().Contains(" 0 -1 ") && r.Public().EndsWith(" d0.2"), "nothing to play: draws until the boneyard's empty: " + r.Public() + " / " + r.Private(0));
        Check(r.Act(0, "pass") && r.Turn == 1 && r.Act(1, "pass") && r.Turn == -1 && r.Scores[1] == 20 && r.Public().EndsWith(" p1.0/b1.10"), "blocked: John has 12 pips to 24, the 12 difference rounds to 10: " + r.Public());
        r.Tick(1000); r.Tick(5999);
        Check(r.Turn == -1 && !r.Act(1, "pass") && !r.HandOver, "the round's result shows 5 s");
        r.Tick(6000);
        Check(r.Turn == 1 && r.Private(1).Split(' ')[1].Split('.').Length == 7 && r.Public().EndsWith(" - -1 - - -"), "the next round: the winner leads");
        r.Tick(6001); r.Tick(6001 + TableGame.TurnMs);
        Check(r.Turn == 0 && r.Public().Split(' ')[6] != "-" && r.Private(1).Split(' ')[1].Split('.').Length == 6, "45 s: the table leads for John");

        var o = new Dominoes();
        o.Begin(new byte[] { 1, 2 }, 100, new[] { 100, 100 }, new Random(1));
        o.Deal(new[] { Tiles(64), Tiles(43, 22, 11) }, Tiles(), 0);
        Check(o.Act(0, "play 64 L") && o.Scores[0] == 25 && o.Turn == -1 && o.Public().EndsWith(" s0.10/o0.15"), "out with a scoring play: 10, then John's 13 pips round to 15: " + o.Public());

        var w = new Dominoes();
        w.Begin(new byte[] { 1, 2, 3 }, 250, new[] { 250, 1000, 250 }, new Random(1));
        w.Deal(new[] { Tiles(64, 11), Tiles(43), Tiles(22) }, Tiles(), 0);
        w.Scores[0] = 90;
        Check(w.Act(0, "play 64 L") && w.HandOver && w.Turn == -1 && w.Public().Split(' ')[3] == "0" && w.Settlement().SequenceEqual(new[] { 500, -250, -250 }), "100 ends the game at once: the winner takes the stakes from each");
        w.Forfeit(1);
        Check(!w.Act(1, "play 43 L") && w.Settlement().SequenceEqual(new[] { 500, -250, -250 }), "once it's over nothing changes");

        var f = new Dominoes();
        f.Begin(new byte[] { 1, 2, 3 }, 100, new[] { 100, 100, 100 }, new Random(1));
        f.Deal(new[] { Tiles(64, 11), Tiles(41, 12), Tiles(22, 13) }, Tiles(), 0);
        f.Forfeit(0);
        Check(f.Turn == 1 && !f.HandOver && f.Public().Contains("1:0:-1,") && f.Public().EndsWith(" l0.0") && f.Private(0).EndsWith(" -"), "a seat leaving on its turn: out, its tiles gone, the next plays");
        f.Scores[1] = 95;
        Check(f.Act(1, "play 41 L") && f.HandOver && f.Settlement().SequenceEqual(new[] { -100, 200, -100 }), "the leaver still pays the winner");
        var two = new Dominoes();
        two.Begin(new byte[] { 1, 2 }, 100, new[] { 100, 100 }, new Random(1));
        two.Forfeit(1);
        Check(two.HandOver && two.Settlement().SequenceEqual(new[] { 100, -100 }) && two.Settlement().Sum() == 0, "of two, one leaves: the other wins");

        var t = new Dominoes();
        t.Begin(new byte[] { 1, 2 }, 100, new[] { 100, 100 }, new Random(1));
        t.Deal(new[] { Tiles(66, 11), Tiles(65, 20) }, Tiles(), 0);
        t.Act(0, "play 66 L"); t.Act(1, "play 65 L");
        Check(t.Act(0, "pass") && t.Act(1, "pass") && t.Turn == -1 && t.Scores.SequenceEqual(new[] { 0, 0 }) && t.Public().EndsWith(" p1.0/t0.0"), "blocked with 2 pips each: a tie scores nothing: " + t.Public());
        Check(t.DefaultAction(0) == "pass", "nothing to play or draw: the table passes");
        var dflt = new Dominoes();
        dflt.Begin(new byte[] { 1, 2 }, 100, new[] { 100, 100 }, new Random(1));
        dflt.Deal(new[] { Tiles(66, 11), Tiles(65, 10) }, Tiles(33), 0);
        dflt.Act(0, "play 66 L");
        Check(dflt.DefaultAction(1).StartsWith("play 65 ") && dflt.Act(1, dflt.DefaultAction(1)) && dflt.DefaultAction(0) == "draw", "the table's move: a legal play, else draw");
    }

    static void DominoesRandomGames()
    {
        for (int seed = 1; seed <= 45; seed++)
        {
            int n = 2 + seed % 3;
            var ids = new byte[] { 1, 2, 3, 4 }.Take(n).ToList();
            var g = new Dominoes();
            g.Begin(ids, 100, Enumerable.Repeat(5000, n).ToList(), new Random(seed));
            long now = 0;
            int step = 0;
            for (; step < 4000 && !g.HandOver; step++)
            {
                if (seed % 5 == 0 && step == 40) g.Forfeit(1);
                if (g.Turn < 0) { now += Dominoes.RoundPauseMs; g.Tick(now); continue; }
                int seat = g.Turn;
                string pub = g.Public(), act = g.DefaultAction(seat);
                var v = new Dominoes(); v.OnPublic(pub); v.OnPrivate(g.Private(seat));
                var opts = v.Options(ids[seat]);
                Check(pub.Length <= TableMsg.MaxState && opts.Any(o => o.action == act) && opts.All(o => o.label.Length < 60), $"seed {seed}: the view offers the table's move {act}: {pub}");
                var lines = v.Lines(ids[seat], Name);
                Check(lines.Count <= 8 && lines.All(x => x.text.Length <= 64), $"seed {seed}: the panel fits: " + string.Join(" / ", lines.Select(x => x.text)));
                Check(g.Act(seat, act), $"seed {seed}: the move is taken: {act} at {pub}");
            }
            var net = g.Settlement();
            int won = Array.IndexOf(net.ToArray(), net.Max());
            Check(g.HandOver && (g.Scores[won] >= 100 || seed % 5 == 0) && net.Sum() == 0 && net[won] == 100 * (n - 1) && net.Count(x => x == -100) == n - 1, $"seed {seed}: over in {step} moves, the winner takes 1.00 from each: " + string.Join(",", net));
        }
    }

    static void DominoesView()
    {
        var v = new Dominoes();
        const string state = "100 0 6 -1 7 1:10:2,2:5:3 63.33.31 1 - - -";
        v.OnPublic(state);
        Check(v.Options(1).Count == 0, "no tiles of ours for this move yet: nothing offered");
        v.OnPrivate("6 35.64");
        Check(v.Options(1).Count == 0, "tiles from an older move: still nothing");
        v.OnPrivate("7 35.64");
        var o = v.Options(1);
        Check(o.Select(x => x.label).SequenceEqual(new[] { "Play [3|5] up from the spinner (end 3)", "Play [3|5] down from the spinner (end 3)", "Play [6|4] on the left (end 6): scores 5" })
            && o[2].action == "play 64 L" && v.Options(2).Count == 0, "every legal play, with the end and what it scores: " + string.Join(" / ", o.Select(x => x.label)));
        Check(v.Gain("play 64 L") == 5 && v.Gain("play 35 U") == 0 && v.Gain("play 35 L") == 0 && Dominoes.TilePips("play 64 L") == 10 && Dominoes.TilePips("draw") == -1, "what the bots see");
        var lines = v.Lines(1, Name).Select(x => x.text).ToList();
        Check(lines.SequenceEqual(new[] { "To 100: you 10, John 5", "Board: [6|3][3|3*][3|1]", "Spinner up: none, down: none", "Yours: [6|4][3|5]", "Ends add to 7, boneyard 6; tiles: John 3", "Your turn" }), "the panel: " + string.Join(" / ", lines));
        v.OnPublic("100 1 0 -1 8 1:10:2,2:5:3 63.33.31 1 - - p0.0");
        Check(v.Options(1).Count == 0 && v.Lines(2, Name).Last().text == "Your turn" && v.Lines(1, Name).Any(x => x.text == "You passed") && v.Lines(1, Name).Last().text == "John to play", "John's turn now");
        var empty = new Dominoes(); empty.OnPublic("100 0 14 -1 1 1:0:7,2:0:7 - -1 - - -"); empty.OnPrivate("1 64.00");
        Check(empty.Options(1).Select(x => x.label).SequenceEqual(new[] { "Lead with [6|4]: scores 10", "Lead with [0|0]" }), "a new round: any tile leads");
        var stuck = new Dominoes(); stuck.OnPublic("100 0 3 -1 4 1:0:1,2:0:7 63 -1 - - -"); stuck.OnPrivate("4 11");
        Check(stuck.Options(1).Single().action == "draw" && stuck.Options(1)[0].label == "Draw from the boneyard (3 left)", "nothing to play: draw");
        stuck.OnPublic("100 0 0 -1 5 1:0:1,2:0:7 63 -1 - - -"); stuck.OnPrivate("5 11");
        Check(stuck.Options(1).Single().action == "pass", "nothing to draw either: pass");
        var won = new Dominoes(); won.OnPublic("250 -1 0 1 9 1:40:3,2:100:0,3:20:-1 66 0 - - s1.10/o1.15");
        var wl = won.Lines(1, Name).Select(x => x.text).ToList();
        Check(won.Options(2).Count == 0 && wl[0] == "To 100: you 40, John 100, Sadie 20 (left)" && wl.Contains("John scored 10; John went out: +15") && wl.Last() == "John wins the game and $2.50 from each player", "over: " + string.Join(" / ", wl));
        Check(won.Lines(9, Name).Any(x => x.text == "You play from the next game"), "not in this game: from the next one");
        var bad = new[] { null, "", "garbage", "100 0 6 -1 7 1:10:2,2:5:3 63.33.31 1 - -", "100 0 6 -1 7 1:10:2 63 -1 - - -", "100 5 6 -1 7 1:10:2,2:5:3 63 -1 - - -",
            "100 0 6 -1 7 1:10:2,2:5:3 63.73 -1 - - -", "100 0 6 -1 7 1:10:2,2:5:3 63.32.21 1 - - -", "100 0 6 -1 7 1:10:2,2:5:3 63.33 1 35 - -", "100 0 6 -1 7 1:10:2,1:5:3 63 -1 - - -",
            "0 0 6 -1 7 1:10:2,2:5:3 63 -1 - - -", "100 0 6 -1 7 1:10:2,2:5:3 63 -1 - - x0.5", "100 0 6 -1 7 1:10:2,2:5:3 63 -1 - - s9.5", "100 0 6 -1 7 1:10:2,2:5:3 63 -1 - - s0.5//",
            "100 0 6 -1 7 1:10:2,2:5:3 63 9 - - -", "100 0 6 -1 7 0:10:2,2:5:3 63 -1 - - -", "100 0 99 -1 7 1:10:2,2:5:3 63 -1 - - -", "100 0 6 -1 7 1:10:2,2:5:3,3:0:0,4:0:0,5:0:0 63 -1 - - -" };
        foreach (var s in bad)
        {
            var w = new Dominoes(); w.OnPublic(state); w.OnPrivate("7 35.64"); w.OnPublic(s);
            Check(w.Options(1).Count == 3, "refused, the last good state kept: " + s);
        }
        foreach (var s in new[] { null, "7", "7 35.64.99", "x 35", "7 35.6" })
        {
            var w = new Dominoes(); w.OnPublic(state); w.OnPrivate("7 35.64"); w.OnPrivate(s);
            Check(w.Options(1).Count == 3, "a bad secret refused: " + s);
        }
    }

    static void DominoesAtTable()
    {
        var host = TableRun.Open(1, 1, () => new Dominoes(), 100, default, 0);
        host.Rng = new Random(11); host.MyCash = 50_00; host.PosOf = id => new Vec3(2, 0, 0);
        var a = TableRun.Join(2, 1, "1-1", new Dominoes(), 100, default, 0); a.MyCash = 50_00;
        Pump(0, host, a);
        long now = 0;
        int byHost = 0, byJohn = 0, pays = 0;
        for (int i = 0; i < 30_000 && host.Paid.Count == 0; i++)
        {
            now += 700;
            host.Tick(now); a.Tick(now);
            var ho = host.Options; if (ho.Count > 0) { host.Act(ho[0].action, now); byHost++; }
            var ao = a.Options; if (ao.Count > 0) { a.Act(ao[0].action, now); byJohn++; }
            pays += Pump(now, host, a).Count(w => w.Contains("pay 1-1"));
            Check(host.Seated && a.Seated && !a.Done, "both still at the table at " + now);
        }
        Check(host.Paid.Count == 1 && a.Paid.Count == 1 && host.Paid[0] + a.Paid[0] == 0 && Math.Abs(a.Paid[0]) == 100 && pays == 1 && byHost > 10 && byJohn > 10,
            $"paid once at the game's end, $1.00 to the winner: host {string.Join(",", host.Paid)}, John {string.Join(",", a.Paid)}, moves {byHost}/{byJohn}");
    }

    static string[] Cards(string s) => s.Split(new[] { ' ' });

    static void BlackjackHands()
    {
        Check(Blackjack.Total(Cards("AS 6H")) == (17, true) && Blackjack.Total(Cards("AS 6H TD")) == (17, false), "an ace is 11 until that busts");
        Check(Blackjack.Total(Cards("AS AD")) == (12, true) && Blackjack.Total(Cards("AS AD 9C")) == (21, true) && Blackjack.Total(Cards("KS QD 2C")) == (22, false), "two aces, faces");
        Check(Blackjack.Natural(Cards("AS KD")) && !Blackjack.Natural(Cards("7S 7D 7C")) && !Blackjack.Natural(Cards("AS 9D")), "a blackjack is 21 in two cards");
        Check(Blackjack.Outcome(Cards("AS KD"), Cards("7S 7D 7C")) == 3 && Blackjack.Outcome(Cards("7S 7D 7C"), Cards("AS KD")) == -2, "blackjack beats 21 either way");
        Check(Blackjack.Outcome(Cards("AS KD"), Cards("AH QC")) == 0 && Blackjack.Outcome(Cards("TS 8D"), Cards("9S 9D")) == 0, "pushes");
        Check(Blackjack.Outcome(Cards("TS 6D KC"), Cards("TS 6D 9C")) == -2 && Blackjack.Outcome(Cards("TS 8D"), Cards("TS 6D 9C")) == 2, "a bust loses even when the dealer busts");
        Check(Blackjack.Outcome(Cards("TS 9D"), Cards("TS 8D")) == 2 && Blackjack.Outcome(Cards("TS 7D"), Cards("TS 8D")) == -2, "higher wins");
    }

    static void BlackjackRound()
    {
        var g = new Blackjack();
        g.Begin(new byte[] { 1, 2, 3 }, 100, new[] { 5000, 5000, 300 }, new RiggedRandom());
        Check(g.Name == "blackjack" && g.Title == "Blackjack" && g.MinPlayers == 2 && !g.Over && g.Shuffles == 1 && g.ShoeLeft == 312, "six decks, shuffled");
        Check(g.Public() == "100 b 1 2000 1:0:-:h:0 2:0:-:w:0 3:0:-:w:0" && g.Turn == 1 && g.DefaultAction(1) == "sit", "bets first; the house covers 10 x stakes x seats: " + g.Public());
        Check(g.Private(0) == null && g.Private(1) == "5000" && g.Private(2) == "300" && g.Private(3) == null, "each seat's cash to it alone");
        Check(!g.Act(0, "bet 1") && !g.Act(1, "bet 3") && !g.Act(2, "bet 5") && !g.Act(1, "hit") && !g.Act(1, null), "the house doesn't bet, 1 2 or 5 x only, never over the cash, bets first");
        g.Stack("5S", "6D", "TS", "6H", "KH", "7S", "9C", "5C");
        Check(g.Act(1, "bet 2") && !g.Act(1, "bet 1") && g.Public() == "100 b 2 2000 1:0:-:h:0 2:200:-:p:0 3:0:-:w:0", "one bet each: " + g.Public());
        Check(g.Act(2, "bet 2") && g.Public() == "100 p 1 2000 1:0:KH.??:h:0 2:200:5S.6D:p:0 3:200:TS.6H:p:0" && g.DefaultAction(1) == "stand", "dealt, the hole card down: " + g.Public());
        var v = new Blackjack(); v.OnPublic(g.Public()); v.OnPrivate("5000");
        Check(string.Join("|", v.Options(2).Select(o => o.label + "=" + o.action)) == "Hit (you have 11)=hit|Stand=stand|Double: $2.00 more, one card=double" && v.Options(3).Count == 0 && v.Options(1).Count == 0, "John's move");
        var mid = v.Lines(2, Name);
        Check(mid[0].text == "Arthur deals: K ?" && mid[1].text == "John: $2.00 on 5 6 (11)" && mid[1].tone == 1 && mid[3].text == "Your move: hit, stand or double" && mid[4].text == "The house covers up to $20.00 a round",
              "mid-round panel: " + string.Join(" / ", mid.Select(x => x.text)));
        Check(!g.Act(2, "hit") && g.Act(1, "double") && g.Turn == 2, "in turn only; John doubles and gets one card");
        Check(!g.Act(2, "double") && g.Act(2, "hit") && g.HandOver && g.Turn == -1, "Sadie can't cover a double; she hits to 21, which stands, and the dealer plays");
        Check(g.Public() == "100 d -1 2000 1:0:KH.7S:h:-600 2:400:5S.6D.9C:d:400 3:200:TS.6H.5C:s:200" && g.Settlement().SequenceEqual(new[] { -600, 400, 200 }), "the dealer stands on 17, wins paid 1:1: " + g.Public());
        Check(g.Settlement().Sum() == 0 && TableMsg.CheckSettlement(g.Settlement(), new[] { 5000, 5000, 300 }) == null, "it adds up to zero");
        v.OnPublic(g.Public());
        var end = v.Lines(3, Name);
        Check(end.Count == 3 && end[0].text == "Arthur deals: K 7 (17), lost $6.00" && end[0].tone == 0 && end[1].text == "John: $4.00 on 5 6 9 (20), doubled, won $4.00" && end[1].tone == 4
              && end[2].text == "Sadie: $2.00 on 10 6 5 (21), won $2.00", "result panel: " + string.Join(" / ", end.Select(x => x.text)));
    }

    static void BlackjackRules()
    {
        var cash3 = new[] { 5000, 5000, 5000 };
        var g = new Blackjack();
        g.Begin(new byte[] { 1, 2, 3 }, 100, cash3, new RiggedRandom());
        g.Act(2, "sit"); g.Stack("AS", "KD", "9S", "7H", "5C"); g.Act(1, "bet 1");
        Check(g.HandOver && g.Public() == "100 d -1 2000 1:0:9S.7H:h:-150 2:100:AS.KD:b:150 3:0:-:o:0" && g.Settlement().SequenceEqual(new[] { -150, 150, 0 }), "blackjack pays 3:2, no draw against it, a sit-out is out: " + g.Public());

        g.Begin(new byte[] { 1, 2, 3 }, 100, cash3, null); g.Stack("TS", "9H", "AS", "QD", "AH", "KC"); g.Act(1, "bet 1"); g.Act(2, "bet 1");
        Check(g.HandOver && g.Settlement().SequenceEqual(new[] { 100, -100, 0 }), "the dealer's blackjack ends the round at the deal: 19 loses, blackjack pushes");

        g.Begin(new byte[] { 1, 2 }, 100, new[] { 5000, 5000 }, null); g.Stack("TS", "8H", "AS", "6H", "5C"); g.Act(1, "bet 1"); g.Act(1, "stand");
        Check(g.Public() == "100 d -1 1000 1:0:AS.6H:h:-100 2:100:TS.8H:s:100", "the dealer stands on a soft 17: " + g.Public());
        g.Begin(new byte[] { 1, 2 }, 100, new[] { 5000, 5000 }, null); g.Stack("TS", "8H", "TS", "6H", "5C"); g.Act(1, "bet 1"); g.Act(1, "stand");
        Check(g.Public() == "100 d -1 1000 1:0:TS.6H.5C:h:100 2:100:TS.8H:s:-100", "and draws on 16: " + g.Public());
        g.Begin(new byte[] { 1, 2, 3 }, 100, cash3, null); g.Stack("TS", "6H", "TS", "7H", "TS", "6C", "KD", "9S"); g.Act(1, "bet 1"); g.Act(2, "bet 1");
        g.Act(1, "hit"); Check(g.Turn == 2 && g.Public().Contains(" 2:100:TS.6H.KD:x:0 "), "a bust is done: " + g.Public());
        g.Act(2, "stand");
        Check(g.Settlement().SequenceEqual(new[] { 0, -100, 100 }) && g.Public().StartsWith("100 d -1 2000 1:0:TS.6C.9S:h:0 "), "the dealer busts: the bust still loses, the 17 wins: " + g.Public());

        // The house brought $6: it covers $6 a round (not 10 x $1 x 3 = $30), and two 3:2 wins of $7.50 less a $5 loss is $10: the wins are cut by the same share.
        var cash4 = new[] { 600, 5000, 5000, 5000 };
        g.Begin(new byte[] { 1, 2, 3, 4 }, 100, cash4, null); g.Stack("AS", "KD", "AH", "QD", "TS", "7H", "9S", "TC");
        g.Act(1, "bet 5"); g.Act(2, "bet 5"); g.Act(3, "bet 5"); g.Act(3, "stand");
        var net = g.Settlement();
        Check(g.Public().StartsWith("100 c -1 600 ") && net.SequenceEqual(new[] { -600, 550, 550, -500 }) && TableMsg.CheckSettlement(net, cash4) == null, "capped at what the house brought: " + g.Public());
        var v = new Blackjack(); v.OnPublic(g.Public());
        Check(v.Lines(1, Name).Last() == ("The house covers only $6.00 a round: wins cut", 3) && v.Lines(1, Name)[0] == ("Arthur deals: 9 10 (19), lost $6.00", 1), "said on screen");

        // Leaving: a stood winner gets its bet back, the one whose turn it is stands, a bettor-to-be sits out; the house leaving plays its hand out.
        g.Begin(new byte[] { 1, 2, 3 }, 100, cash3, null); g.Stack("TS", "QH", "9S", "7H", "TS", "7C"); g.Act(1, "bet 1"); g.Act(2, "bet 1");
        g.Act(1, "stand"); g.Forfeit(1); Check(g.Turn == 2 && !g.HandOver, "a seat that stood and left: play goes on");
        g.Forfeit(2);
        Check(g.HandOver && g.Settlement().SequenceEqual(new[] { 100, 0, -100 }) && g.Public() == "100 d -1 2000 1:0:TS.7C:h:100 2:100:TS.QH:l:0 3:100:9S.7H:l:-100", "20 left: a push; 16 left: stands and loses: " + g.Public());
        g.Forfeit(1); Check(g.Settlement().SequenceEqual(new[] { 100, 0, -100 }), "once it's over a forfeit changes nothing");
        g.Begin(new byte[] { 1, 2, 3 }, 100, cash3, null); g.Forfeit(2); g.Stack("TS", "QH", "TS", "7C"); g.Act(1, "bet 1");
        Check(g.Turn == 1 && g.Public() == "100 p 1 2000 1:0:TS.??:h:0 2:100:TS.QH:p:0 3:0:-:l:0", "left before betting: out of the round: " + g.Public());
        g.Forfeit(0);
        Check(g.HandOver && g.Settlement().SequenceEqual(new[] { -100, 100, 0 }) && g.Public() == "100 d -1 2000 1:0:TS.7C:l:-100 2:100:TS.QH:s:100 3:0:-:l:0",
              "the house got up: John's 20 stands, the dealer's 17 stands, John is paid: " + g.Public());
        v.OnPublic(g.Public()); Check(v.Lines(2, Name)[0] == ("Arthur (the house) got up: 10 7 (17), lost $1.00", 3), "and says so: " + v.Lines(2, Name)[0].Item1);
        g.Begin(new byte[] { 1, 2, 3 }, 100, new[] { 5000, 5000, 5000 }, null); g.Act(1, "bet 2"); g.Forfeit(0);
        Check(g.HandOver && g.Settlement().All(n => n == 0), "the house got up before the deal: nothing dealt, nobody pays");

        g.Begin(new byte[] { 1, 2 }, 100, new[] { 5000, 5000 }, null);
        g.Tick(0); g.Tick(TableGame.TurnMs - 1); Check(!g.HandOver, "45 s to bet");
        g.Tick(TableGame.TurnMs); Check(g.HandOver && g.Settlement().SequenceEqual(new[] { 0, 0 }) && g.Public() == "100 d -1 1000 1:0:-:h:0 2:0:-:o:0", "then the seat sits out: a round of nothing");
        v.OnPublic(g.Public()); Check(v.Lines(1, Name).Select(x => x.text).SequenceEqual(new[] { "Arthur deals (the house)", "John: sits this round out" }), "nothing dealt: " + string.Join(" / ", v.Lines(1, Name).Select(x => x.text)));

        const string good = "100 b 1 2000 1:0:-:h:0 2:0:-:w:0";
        foreach (var bad in new[] { null, "garbage", "100 b 1 2000 1:0:-:h:0", "100 x 1 2000 1:0:-:h:0 2:0:-:w:0", "100 b 0 2000 1:0:-:h:0 2:0:-:w:0", "100 b 2 2000 1:0:-:h:0 2:0:-:w:0",
                                     "100 b 1 2000 1:0:-:w:0 2:0:-:w:0", "100 b 1 2000 1:0:-:h:0 2:0:-:h:0", "100 b 1 2000 1:0:-:h:0 1:0:-:w:0", "100 b 1 2000 1:0:-:h:0 2:0:ZZ:w:0",
                                     "100 b 1 2000 1:0:-:h:0 2:0:??:w:0", "100 b 1 2000 1:0:-:h:0 2:-5:-:w:0", "100 b 1 2000 1:0:-:h:0 2:0:-:w:99999999", "0 b 1 2000 1:0:-:h:0 2:0:-:w:0",
                                     "100 b 1 2000 1:0:-:h:0 2:0:-:w", "100 b 1 2000 1:0:-:h:0 255:0:-:w:0", "100 b 1 2000 1:0:-:h:0 2:0:-:w:0 3:0:-:w:0 4:0:-:w:0 5:0:-:w:0",
                                     "100 b 1 2000 1:0:-:h:0 2:0:" + string.Join(".", Enumerable.Repeat("AS", 23)) + ":w:0", "100 b 1 2000 1:0:-:h:0 2:0:-:q:0", "100 b 1 -2000 1:0:-:h:0 2:0:-:w:0" })
        {
            var w = new Blackjack(); w.OnPublic(good); w.OnPublic(bad);
            Check(w.Options(2).Select(o => o.action).SequenceEqual(new[] { "bet 1", "sit" }), "refused, the last good state kept: " + bad);
        }
        var c = new Blackjack(); c.OnPublic(good);
        c.OnPrivate("5000"); Check(c.Options(2).Select(o => o.label).SequenceEqual(new[] { "Bet $1.00", "Bet $2.00 (2x)", "Bet $5.00 (5x)", "Sit this round out" }), "with the cash known, the bets it covers");
        c.OnPrivate("x"); Check(c.Options(2).Count == 2 && c.Lines(2, Name).Last() == ("The house covers up to $20.00 a round", 2) && c.Lines(2, Name)[2] == ("Your bet: pick it from the list", 1), "odd cash: the stakes only");
    }

    static void BlackjackShoe()
    {
        var g = new Blackjack();
        var rng = new Random(7);
        int reshuffles = 0;
        for (int round = 0; round < 300; round++)
        {
            int left = g.ShoeLeft, before = g.Shuffles;
            var cash = new[] { 100 * rng.Next(1, 51), 100 * rng.Next(1, 51), 100 * rng.Next(1, 51), 100 * rng.Next(1, 51) };
            g.Begin(new byte[] { 1, 2, 3, 4 }, 100, cash, rng);
            bool fresh = g.Shuffles == before + 1;
            Check(fresh == (left < 78) && (fresh ? g.ShoeLeft == 312 : g.ShoeLeft == left), $"round {round}: {left} left, reshuffled {fresh}");
            if (fresh && round > 0) reshuffles++;
            for (int moves = 0; !g.HandOver && moves < 100; moves++)
            {
                int seat = g.Turn;
                var acts = g.Public().Split(' ')[1] == "b" ? new[] { "bet 1", "bet 2", "bet 5", "sit" } : new[] { "hit", "stand", "double" };
                if (!g.Act(seat, acts[rng.Next(acts.Length)])) Check(g.Act(seat, g.DefaultAction(seat)), "the default move is always legal");
            }
            Check(g.HandOver && TableMsg.CheckSettlement(g.Settlement(), cash) == null && g.Public().Length <= TableMsg.MaxState, $"round {round} adds up within the cash brought: {g.Public()}");
        }
        Check(reshuffles >= 2, "reshuffled " + reshuffles + " times");
    }

    static void BlackjackAtTable()
    {
        var host = TableRun.Open(1, 1, () => new Blackjack(), 100, new Vec3(0, 0, 0), 0);
        host.Rng = new RiggedRandom();   // the shoe in order: 2C 2D 2H 2S 3C 3D 3H 3S 4C...
        host.MyCash = 50_00; host.PosOf = id => new Vec3(2, 0, 0);
        var a = TableRun.Join(2, 1, "1-1", new Blackjack(), 100, default, 0); a.MyCash = 3_00;
        Pump(0, host, a); host.Tick(100); Pump(100, host, a); host.Tick(200); var went = Pump(200, host, a);
        Check(host.LastPublic == "100 b 1 1000 1:0:-:h:0 2:0:-:w:0" && went.Contains("1>2:priv 1-1 300") && !went.Any(w => w.Contains(">255:") && w.Contains("priv")), "betting; John's cash to John alone: " + string.Join(" / ", went));
        Check(a.Options.Select(o => o.label).SequenceEqual(new[] { "Bet $1.00", "Bet $2.00 (2x)", "Sit this round out" }) && host.Options.Count == 0, "only the bets he covers; the house has no moves");
        a.Act("bet 2", 300); Pump(300, host, a); host.Tick(1000); Pump(1000, host, a);
        Check(a.LastPublic == "100 p 1 1000 1:0:2H.??:h:0 2:200:2C.2D:p:0" && a.Options.Select(o => o.action).SequenceEqual(new[] { "hit", "stand" }), "dealt; no double he can't cover: " + a.LastPublic);
        a.Act("stand", 1100); Pump(1100, host, a); host.Tick(1700); Pump(1700, host, a);
        Check(a.LastPublic == "100 d -1 1000 1:0:2H.2S.3C.3D.3H.3S.4C:h:200 2:200:2C.2D:s:-200" && a.Paid.SequenceEqual(new[] { -200 }) && host.Paid.SequenceEqual(new[] { 200 }), "the dealer draws to 20; paid: " + a.LastPublic);
        // The host short of the stakes: no round (a guest is never made the house), and it's told.
        var poor = TableRun.Open(1, 2, () => new Blackjack(), 100, new Vec3(0, 0, 0), 0);
        poor.MyCash = 50; poor.PosOf = id => new Vec3(2, 0, 0);
        var j = TableRun.Join(2, 1, "1-2", new Blackjack(), 100, default, 0); j.MyCash = 50_00;
        var k = TableRun.Join(3, 1, "1-2", new Blackjack(), 100, default, 0); k.MyCash = 50_00;
        for (long t = 0; t <= 12_000; t += 500) { Pump(t, poor, j, k); poor.Tick(t); }
        Check(poor.LastPublic == null && j.LastPublic == null && poor.Lines(Name).Any(x => x.text == "You need $1.00 in cash to play a hand"), "the house hasn't the stakes: nothing dealt");
        poor.MyCash = 50_00;
        for (long t = 12_500; t <= 20_000; t += 500) { Pump(t, poor, j, k); poor.Tick(t); }
        Check(poor.LastPublic != null && poor.LastPublic.Split(' ')[4].StartsWith("1:0:-:h"), "once it has: dealt, the host the house: " + poor.LastPublic);
    }

    // ---- poker ----
    // Deals the cards asked for first (each seat's two, seat by seat, then the board), the rest of the deck after.
    sealed class StackedDeck : Random
    {
        readonly System.Collections.Generic.Queue<int> _js = new System.Collections.Generic.Queue<int>();
        public StackedDeck(string cards)
        {
            var want = Enumerable.Range(0, cards.Length / 2).Select(i => Poker.Cards(cards.Substring(2 * i, 2))[0]).ToList();
            var deck = Enumerable.Range(0, 52).ToList();
            var full = want.Concat(deck.Where(c => !want.Contains(c))).ToList();
            for (int i = 51; i > 0; i--) { int j = deck.IndexOf(full[i]); _js.Enqueue(j); int t = deck[i]; deck[i] = deck[j]; deck[j] = t; }
        }
        public override int Next(int maxValue) => _js.Count > 0 ? _js.Dequeue() : 0;
    }

    static int Hand(string cards) => Poker.Best(Poker.Cards(cards));
    static Poker Deal(int stakes, int[] cash, string cards = "", Poker g = null)
    {
        g = g ?? new Poker();
        g.Begin(Enumerable.Range(1, cash.Length).Select(i => (byte)i).ToList(), stakes, cash, new StackedDeck(cards));
        return g;
    }
    static string[] Head(Poker g) => g.Public().Split(' ');
    static string[] SeatOf(Poker g, int i) => g.Public().Split(' ')[9].Split(',')[i].Split(':');

    static void PokerRanks()
    {
        var named = new (string cards, string name)[]
        {
            ("AsKsQsJsTs2c3d", "a royal flush"), ("9h8h7h6h5h2c2d", "a straight flush, Nine high"), ("5d4d3d2dAd9c9s", "a straight flush, Five high"),
            ("7s7h7d7cKs2d3h", "four Sevens"), ("QsQhQd4c4s4d9h", "a full house, Queens over Fours"), ("9s9h9dKcKs4c4d", "a full house, Nines over Kings"),
            ("Ah9h7h4h2hKsKd", "a flush, Ace high"), ("2h5h7h9hJhTc8c", "a flush, Jack high"), ("Ts9c8d7h6s2c2d", "a straight, Ten high"),
            ("As2c3d4h5sKdQc", "a straight, Five high"), ("Ts9c8d7h6s6d6c", "a straight, Ten high"), ("9c8d7h6s5c4d2h", "a straight, Nine high"),
            ("8s8h8dAcKs2d4h", "three Eights"), ("JsJhTdTc2s5d9h", "two pair, Jacks and Tens"), ("AsAhKdKcQsQh2d", "two pair, Aces and Kings"),
            ("6s6hAdKcQs2d9h", "a pair of Sixes"), ("AsJd9c7h5s3d2c", "Ace high"), ("QsKsAs2h3d", "Ace high"),
        };
        foreach (var (c, n) in named) Check(Poker.HandName(Hand(c)) == n, $"{c}: {Poker.HandName(Hand(c))}, not {n}");
        // a against b: 1 a wins, 0 a tie, -1 b wins
        var vs = new (string a, string b, int win)[]
        {
            ("9h8h7h6h5hAcAd", "AsAhAdAcKs2d3h", 1), ("6c5c4c3c2c", "5d4d3d2dAd", 1), ("7s7h7d7cKs", "7s7h7d7cQs", 1), ("7s7h7d7c2s", "6s6h6d6cAs", 1),
            ("3s3h3d2c2s", "2s2h2dAcAs", 1), ("QsQhQd4c4s", "QsQhQd3c3s", 1), ("AhKh9h7h3h", "AhKh9h7h2h", 1), ("AhKh9h7h3h2hQc", "AhKh9h7h3hQd2c", 0),
            ("AhKh9h7h3h", "AsKs9s7s3s", 0), ("2h4h6h8hTh", "AsKdQcJhTs", 1), ("6s5h4d3c2s", "5s4h3d2cAs", 1), ("AsKdQcJhTs", "KsQdJcTh9s", 1),
            ("Ts9c8d7h6s6d6c", "TsTcTd7h6s2d3c", 1), ("8s8h8dAcKs", "8s8h8dAcQs", 1), ("8s8h8d2c3s", "7s7h7dAcKs", 1), ("KsKh2d2cAs", "QsQhJdJcAs", 1),
            ("KsKh5d5c2s", "KsKh4d4cAs", 1), ("KsKh5d5cAs", "KsKh5d5cQs", 1), ("AsAhKdKcQsQh2d", "AsAhKdKcJs9h8d", 1), ("6s6hAdKcQs", "6s6hAdKcJs", 1),
            ("6s6hAdKcQs2d3h", "6s6hAdKcQs4d5h", 0), ("AsAh2c3d5h", "KsKhAcQdJh", 1), ("AsJd9c7h5s", "AsJd9c7h4s", 1), ("AsJd9c7h5s3d2c", "AsJd9c7h5s4d2c", 0),
            ("AsKsQsJsTs2c3d", "AsKsQsJsTs4h5h", 0), ("QsKsAs2h3d", "KsQsJs2h3d", 1), ("2s2h3c4d6h", "AsKdQcJh9s", 1), ("5s4h3d2cAs", "AsAhAdKcQs", 1),
        };
        foreach (var (a, b, w) in vs)
        {
            Check(Math.Sign(Hand(a).CompareTo(Hand(b))) == w, $"{a} against {b}: should be {w}");
            Check(Math.Sign(Hand(b).CompareTo(Hand(a))) == -w, $"{b} against {a}: should be {-w}");
        }
        Check(Poker.Cards("AsAs") == null && Poker.Cards("Zz") == null && Poker.Cards("A") == null && Poker.Cards("AS") == null && Poker.Cards(null) == null
              && Poker.Cards("").Count == 0 && Hand("AsKs") == -1, "bad cards refused, fewer than five no hand");
        Check(Enumerable.Range(0, 52).All(c => Poker.Cards(Poker.Code(c))[0] == c) && Poker.Code(Poker.Cards("Td")[0]) == "Td", "every card's code round trips");
    }

    static void PokerBetting()
    {
        // Three seats, $1 stakes: the button (seat 0) acts first before the flop, after the blinds of seats 1 and 2.
        var g = Deal(100, new[] { 5000, 5000, 5000 });
        Check(g.Public() == "1 100 0 0 0 100 200 1 - 1:5000:0:0:p:-:0,2:4950:50:50:p:-:0,3:4900:100:100:p:-:0", "blinds $0.50 and $1: " + g.Public());
        Check(g.Turn == 0 && g.DefaultAction(0) == "fold" && !g.Act(1, "call") && !g.Act(0, "check"), "the button first; facing a bet no check, the timer folds");
        foreach (var bad in new[] { "raise 150", "raise 5001", "raise", "raise -300", "raise 3e2", "raise 300 1", "call 5", "dance", "", "Raise 300", null })
            Check(!g.Act(0, bad), "refused: " + bad);
        Check(g.Act(0, "raise 300") && g.Turn == 1 && !g.Act(1, "raise 450") && g.Act(1, "raise 500") && Head(g)[6] == "700", "a raise is at least the last one: 300, then 500, min next 700");
        Check(g.Act(2, "call") && g.Turn == 0 && Head(g)[7] == "1" && g.Act(0, "call") && Head(g)[3] == "1", "re-raised: seat 0 may raise again; all called: the flop");
        Check(g.Turn == 1 && Head(g)[8].Length == 6 && Head(g)[5] == "0" && Head(g)[6] == "100" && g.DefaultAction(1) == "check", "after the flop the seat after the button first; nothing to call");
        Check(!g.Act(1, "raise 99") && g.Act(1, "check") && g.Act(2, "raise 100") && g.Act(0, "fold") && g.Act(1, "call") && Head(g)[3] == "2" && g.Turn == 1, "a bet is a big blind at least; fold; the turn");
        Check(SeatOf(g, 0)[4] == "f" && SeatOf(g, 0)[3] == "500" && SeatOf(g, 1)[3] == "600" && SeatOf(g, 2)[3] == "600", "what each put in");
        g.Act(1, "check"); g.Act(2, "check"); g.Act(1, "check");
        Check(!g.HandOver && Head(g)[8].Length == 10 && g.Act(2, "check") && g.HandOver && g.Turn == -1 && Head(g)[3] == "4", "checked down to the showdown");
        Check(g.Settlement().Sum() == 0 && g.Settlement()[0] == -500 && !g.Act(1, "check"), "it adds up; nothing more once it's over");

        // Heads-up: the button posts the small blind, acts first before the flop and last after it.
        var h = Deal(100, new[] { 5000, 5000 });
        Check(SeatOf(h, 0)[2] == "50" && SeatOf(h, 1)[2] == "100" && h.Turn == 0, "heads-up: the button is the small blind and starts");
        Check(h.Act(0, "call") && h.Turn == 1 && Head(h)[7] == "1" && h.Act(1, "check") && h.Turn == 1 && Head(h)[3] == "1", "the big blind's option, then it acts first on the flop");
        h.Act(1, "check"); h.Act(0, "check");
        Check(h.Act(1, "raise 100") && h.Act(0, "fold") && h.HandOver && h.Settlement().SequenceEqual(new[] { -100, 100 }) && Head(h)[8].Length == 8
              && SeatOf(h, 1)[5] == "-", "everyone else folded: no showdown, no cards shown, the board as far as it got");
        Deal(100, new[] { 5000, 5000 }, "", h);
        Check(Head(h)[0] == "2" && Head(h)[2] == "1" && SeatOf(h, 1)[2] == "50" && h.Turn == 1, "next hand: the button moves, it's the small blind and starts");

        // The button goes round, and to the next seat when its player has gone. Four seats: the one after the big blind starts.
        var r = new Poker();
        var buttons = new System.Collections.Generic.List<string>();
        foreach (var seats in new[] { new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }, new byte[] { 1, 3 }, new byte[] { 1, 3 } })
        {
            r.Begin(seats, 100, seats.Select(s => 5000).ToList(), new Random(1));
            buttons.Add(seats[int.Parse(Head(r)[2])].ToString());
        }
        Check(string.Join(",", buttons) == "1,2,3,1,2,3,1", "the button by player: " + string.Join(",", buttons));
        var four = Deal(100, new[] { 5000, 5000, 5000, 5000 });
        Check(four.Turn == 3 && SeatOf(four, 1)[2] == "50" && SeatOf(four, 2)[2] == "100" && SeatOf(four, 3)[2] == "0", "four seats: blinds after the button, the next starts");

        // A short all-in raise doesn't reopen the betting for those who acted; short all-ins adding up to a full raise do.
        var s3 = Deal(100, new[] { 5000, 5000, 150 });
        s3.Act(0, "call"); s3.Act(1, "call");
        Check(s3.Turn == 2 && s3.Act(2, "raise 150") && s3.Turn == 0 && Head(s3)[7] == "0" && !s3.Act(0, "raise 300") && s3.Act(0, "call")
              && !s3.Act(1, "raise 300") && s3.Act(1, "call") && Head(s3)[3] == "1" && SeatOf(s3, 2)[4] == "a", "all in $0.50 over: call or fold only");
        var s4 = Deal(100, new[] { 5000, 250, 310, 5000 });
        s4.Act(3, "call"); s4.Act(0, "call"); s4.Act(1, "call"); s4.Act(2, "check");
        Check(s4.Turn == 1 && s4.Act(1, "check") && s4.Act(2, "check") && s4.Act(3, "raise 100") && s4.Act(0, "call"), "on the flop a $1 bet, called");
        Check(s4.Act(1, "raise 150") && Head(s4)[7] == "1" && s4.Act(2, "raise 210") && s4.Turn == 3 && Head(s4)[7] == "1" && s4.Act(3, "raise 400"),
              "all in for $1.50 (short), then $2.10: together a full raise, so the bettor may raise again");
        Check(Deal(100, new[] { 5000, 5000 }).Act(0, "raise 5000") && Deal(100, new[] { 5000, 5000 }).Act(0, "raise 120") == false, "all in any time, but not under the min raise");

        // A stack is the cash brought, at most 50 big blinds.
        var cap = Deal(100, new[] { 999_999, 6000 });
        Check(SeatOf(cap, 0)[1] == "4950" && SeatOf(cap, 1)[1] == "4900", "capped at 50 big blinds");
    }

    static void PokerPots()
    {
        // Three all in for different amounts: Arthur's aces take the main pot, John's kings the side pot, Sadie's queens nothing.
        var g = Deal(100, new[] { 1000, 3000, 5000 }, "AsAh" + "KsKh" + "QsQh" + "2c7d9cJd3h");
        Check(g.Act(0, "raise 1000") && g.Act(1, "raise 3000") && g.Turn == 2 && Head(g)[7] == "0" && !g.Act(2, "raise 5000") && g.Act(2, "call"), "all in, all in, Sadie can only call");
        Check(g.HandOver && Head(g)[8] == "2c7d9cJd3h" && SeatOf(g, 0)[5] == "AsAh" && SeatOf(g, 0)[6] == "3000" && SeatOf(g, 1)[6] == "4000" && SeatOf(g, 2)[6] == "0"
              && SeatOf(g, 2)[1] == "2000", "run out to the showdown: " + g.Public());
        Check(g.Settlement().SequenceEqual(new[] { 2000, 1000, -3000 }), "net: " + string.Join(",", g.Settlement()));
        // Four: John best, Arthur second, Micah third, Sadie last.
        var f = Deal(100, new[] { 1000, 2000, 3000, 4000 }, "QsQh" + "AsAh" + "4s3h" + "6s6h" + "2c7d9cJd8s");
        Check(f.Act(3, "raise 4000") && f.Act(0, "call") && f.Act(1, "call") && f.Act(2, "call") && f.HandOver, "everyone all in or called");
        Check(f.Settlement().SequenceEqual(new[] { -1000, 5000, -3000, -1000 }) && SeatOf(f, 3)[6] == "3000", "four pots: " + string.Join(",", f.Settlement()));
        var pots = Poker.Pots(new[] { 1000, 2000, 3000, 4000 }, new[] { true, true, true, true });
        Check(pots.Select(p => p.amount).SequenceEqual(new[] { 4000, 3000, 2000, 1000 }) && pots[2].seats.SequenceEqual(new[] { 2, 3 }), "the pot levels");
        var folded = Poker.Pots(new[] { 200, 50, 0 }, new[] { false, true, false });
        Check(folded.Count == 1 && folded[0].amount == 250 && folded[0].seats.SequenceEqual(new[] { 1 }), "a folded seat's chips over every live one's go to the last pot");
        // A tie splits the pot, the odd cent to the first winner left of the button; the board plays.
        var t = Deal(50, new[] { 2500, 2500, 2500 }, "2c3d" + "4c5d" + "6c7d" + "AsKsQsJsTs");
        t.Act(0, "call"); t.Act(1, "fold"); t.Act(2, "check");
        for (int i = 0; i < 3; i++) { t.Act(2, "check"); t.Act(0, "check"); }
        Check(t.HandOver && t.Settlement().SequenceEqual(new[] { 12, -25, 13 }), "$1.25 split: " + string.Join(",", t.Settlement()));
    }

    static void PokerLeaving()
    {
        // A player leaving folds: what they put in stays in.
        var g = Deal(100, new[] { 5000, 5000, 5000 });
        g.Forfeit(2);
        Check(g.Turn == 0 && SeatOf(g, 2)[4] == "l" && g.Act(0, "fold") && g.HandOver && g.Settlement().SequenceEqual(new[] { 0, 100, -100 }), "the big blind left: the small blind takes it");
        g.Forfeit(1); Check(g.Settlement().SequenceEqual(new[] { 0, 100, -100 }), "once it's over a forfeit changes nothing");
        var o = Deal(100, new[] { 5000, 5000, 5000 });
        o.Forfeit(0); Check(o.Turn == 1 && SeatOf(o, 0)[4] == "l", "the one to act left: the next acts");
        var h = Deal(100, new[] { 5000, 5000 });
        h.Forfeit(0); Check(h.HandOver && h.Settlement().SequenceEqual(new[] { -50, 50 }), "heads-up, one leaves: the other wins");
        // Nobody left to bet against: the board runs out.
        var r = Deal(100, new[] { 5000, 5000, 1000 }, "AsAh" + "KsKh" + "QsQh" + "2c7d9cJd3h");
        r.Act(0, "call"); r.Act(1, "call"); r.Act(2, "raise 1000"); r.Act(0, "call"); r.Act(1, "call");
        Check(r.Turn == 1 && Head(r)[3] == "1", "the flop, Sadie all in");
        r.Forfeit(0);
        Check(r.HandOver && r.Settlement().SequenceEqual(new[] { -1000, 2000, -1000 }) && SeatOf(r, 0)[5] == "-" && SeatOf(r, 1)[5] == "KsKh", "Arthur left: John and Sadie show down, John's kings win: " + r.Public());
        // The 45 s timer: checks when it can, folds facing a bet.
        var tm = Deal(100, new[] { 5000, 5000 });
        tm.Tick(0); tm.Tick(TableGame.TurnMs);
        Check(tm.HandOver && tm.Settlement().SequenceEqual(new[] { -50, 50 }), "timed out facing the big blind: folded");
        var tc = Deal(100, new[] { 5000, 5000 });
        tc.Act(0, "call"); tc.Tick(0); tc.Tick(TableGame.TurnMs);
        Check(!tc.HandOver && Head(tc)[3] == "1", "timed out with nothing to call: checked");
    }

    static void PokerTable()
    {
        // Heads-up at a table: Arthur (host, the button) holds aces, John kings; John's cash is capped at $50.
        var host = TableRun.Open(1, 1, () => new Poker(), 100, new Vec3(0, 0, 0), 0);
        host.Rng = new StackedDeck("AsAh" + "KsKh" + "2c7d9cJd3h");
        host.MyCash = 80_00; host.PosOf = id => new Vec3(2, 0, 0);
        var a = TableRun.Join(2, 1, "1-1", new Poker(), 100, default, 0); a.MyCash = 100_00;
        Pump(0, host, a); host.Tick(100); Pump(100, host, a); host.Tick(200); var went = Pump(200, host, a);
        Check(host.LastPublic == "1 100 0 0 0 100 200 1 - 1:4950:50:50:p:-:0,2:4900:100:100:p:-:0" && a.LastPublic == host.LastPublic, "dealt: " + host.LastPublic);
        Check(went.Contains("1>2:priv 1-1 1 KsKh") && !went.Any(w => w.Contains(">255:") && w.Contains("priv")) && !went.Any(w => w.Contains("AsAh")), "each sees only their own cards");
        Check(a.Lines(Name).Any(x => x.text == "Your cards: Ks Kh (a pair of Kings)" && x.tone == 1) && host.Lines(Name).Any(x => x.text == "Your cards: As Ah (a pair of Aces)")
              && !host.Lines(Name).Any(x => x.text.Contains("Ks")), "panel: " + string.Join(" / ", a.Lines(Name).Select(x => x.text)));
        Check(string.Join("|", host.Options.Select(o => o.label + "=" + o.action)) == "Call $0.50=call|Raise to $2.00 (min)=raise 200|Raise to $3.00 (2x)=raise 300|All in ($50.00)=raise 5000|Fold=fold"
              && a.Options.Count == 0, "the button's moves: " + string.Join("|", host.Options.Select(o => o.label)));
        host.Act("call", 300); host.Tick(900); Pump(900, host, a);
        Check(string.Join("|", a.Options.Select(o => o.label)) == "Check|Raise to $2.00 (min)|Raise to $3.00 (2x)|All in ($50.00)", "the big blind's option: " + string.Join("|", a.Options.Select(o => o.label)));
        Check(a.Lines(Name).Any(x => x.text == "Pre-flop: blinds $0.50/$1.00   pot $2.00") && a.Lines(Name).Any(x => x.text == "Arthur (D): $49.00, bet $1.00") && a.Lines(Name).Last().text == "Your move: check or bet",
              "panel: " + string.Join(" / ", a.Lines(Name).Select(x => x.text)));
        a.Act("raise 300", 1000); Pump(1000, host, a); host.Tick(1600); Pump(1600, host, a);
        Check(string.Join("|", host.Options.Select(o => o.label)) == "Call $2.00|Raise to $5.00 (min)|Raise to $7.00 (2x)|Raise to $9.00 (pot)|All in ($50.00)|Fold", "facing a raise: " + string.Join("|", host.Options.Select(o => o.label)));
        host.Act("raise 5000", 1700); host.Tick(2300); Pump(2300, host, a);
        Check(string.Join("|", a.Options.Select(o => o.label)) == "Call $47.00 (all in)|Fold" && a.Lines(Name).Any(x => x.text == "Arthur (D): $0.00, bet $50.00, all in"), "all in: call or fold");
        a.Act("call", 2400); Pump(2400, host, a); host.Tick(3000); went = Pump(3000, host, a);
        Check(host.Paid.SequenceEqual(new[] { 5000 }) && a.Paid.SequenceEqual(new[] { -5000 }) && went.Any(w => w.StartsWith("1>255:pay 1-1 1 5000 1\npay 1-1 2 -5000 1")), "paid: " + string.Join(" / ", went));
        var lines = a.Lines(Name);
        Check(lines.Last().text == "Arthur wins with a pair of Aces" && lines.Last().tone == 4 && lines.Any(x => x.text == "Arthur (D): As Ah, a pair of Aces, takes $100.00" && x.tone == 4)
              && lines.Any(x => x.text == "John: Ks Kh, a pair of Kings" && x.tone == 1) && lines.Any(x => x.text == "Showdown: 2c 7d 9c Jd 3h   pot $100.00") && lines.Count <= 8,
              "result: " + string.Join(" / ", lines.Select(x => x.text)));
    }

    // Random hands of random offered moves (and players leaving): each ends, and adds up within what was brought.
    static void PokerFuzz()
    {
        var rng = new Random(7);
        var g = new Poker();
        for (int hand = 0; hand < 2000; hand++)
        {
            int n = rng.Next(2, 5);
            var ids = Enumerable.Range(1, n).Select(i => (byte)i).ToList();
            var cash = ids.Select(i => rng.Next(100, 6000)).ToList();
            g.Begin(ids, 100, cash, rng);
            for (int step = 0; !g.HandOver; step++)
            {
                Check(step < 200 && g.Public().Length <= TableMsg.MaxState, "a hand that never ends: " + g.Public());
                if (rng.Next(40) == 0) { g.Forfeit(rng.Next(n)); continue; }
                var v = new Poker(); v.OnPublic(g.Public());
                var opts = v.Options(ids[g.Turn]);
                Check(opts.Count > 0, "no moves for the seat to act: " + g.Public());
                var pick = opts[rng.Next(opts.Count)].action;
                Check(g.Act(g.Turn, pick), $"an offered move refused: {pick} in {g.Public()}");
            }
            var net = g.Settlement();
            Check(TableMsg.CheckSettlement(net, cash.Select(c => Math.Min(c, TableMsg.CashCap(100))).ToList()) == null, $"hand {hand}: {string.Join(",", net)} from {g.Public()}");
        }
    }

    static void PokerView()
    {
        const string good = "1 100 0 0 0 100 200 1 - 1:4950:50:50:p:-:0,2:4900:100:100:p:-:0";
        foreach (var bad in new[] { null, "", "junk", "1 100 0 0 0 100 200 1 -", "0 100 0 0 0 100 200 1 - 1:4950:50:50:p:-:0,2:4900:100:100:p:-:0",
                                     "1 -100 0 0 0 100 200 1 - 1:4950:50:50:p:-:0,2:4900:100:100:p:-:0", "1 100 0 5 0 100 200 1 - 1:4950:50:50:p:-:0,2:4900:100:100:p:-:0",
                                     "1 100 0 0 2 100 200 1 - 1:4950:50:50:p:-:0,2:4900:100:100:p:-:0", "1 100 0 0 -2 100 200 1 - 1:4950:50:50:p:-:0,2:4900:100:100:p:-:0",
                                     "1 100 2 0 0 100 200 1 - 1:4950:50:50:p:-:0,2:4900:100:100:p:-:0", "1 100 0 0 0 100 200 2 - 1:4950:50:50:p:-:0,2:4900:100:100:p:-:0",
                                     "1 100 0 0 0 100 200 1 AsAs 1:4950:50:50:p:-:0,2:4900:100:100:p:-:0", "1 100 0 0 0 100 200 1 AsKsQsJsTs9s 1:4950:50:50:p:-:0,2:4900:100:100:p:-:0",
                                     "1 100 0 0 0 100 200 1 Zz 1:4950:50:50:p:-:0,2:4900:100:100:p:-:0", "1 100 0 0 0 100 200 1 - 0:4950:50:50:p:-:0,2:4900:100:100:p:-:0",
                                     "1 100 0 0 0 100 200 1 - 1:4950:50:50:p:-:0,1:4900:100:100:p:-:0", "1 100 0 0 0 100 200 1 - 1:4950:50:50:x:-:0,2:4900:100:100:p:-:0",
                                     "1 100 0 0 0 100 200 1 - 1:4950:50:50:p:As:0,2:4900:100:100:p:-:0", "1 100 0 0 0 100 200 1 - 1:99999999999:50:50:p:-:0,2:4900:100:100:p:-:0",
                                     "1 100 0 0 0 100 200 1 - 1:2000000:50:50:p:-:0,2:4900:100:100:p:-:0", "1 100 0 0 0 100 200 1 - 1:4950:-50:50:p:-:0,2:4900:100:100:p:-:0",
                                     "1 100 0 0 0 100 200 1 - 1:4950:50:50:p:-:0", "1 100 0 0 0 100 200 1 - 1:4950:50:50:p:-:0,2:4900:100:100:p:-",
                                     "1 100 0 0 0 100 200 1 - 1:1:1:1:p:-:0,2:1:1:1:p:-:0,3:1:1:1:p:-:0,4:1:1:1:p:-:0,5:1:1:1:p:-:0", "1 100 0 0 0 100 200 1 - 1:4950:50:50:p:-:0,255:4900:100:100:p:-:0" })
        {
            var v = new Poker(); v.OnPublic(good); v.OnPublic(bad);
            Check(v.Options(1).Count == 5 && v.ToCall(1) == 50, "refused, the last good state kept: " + bad);
        }
        var w = new Poker(); w.OnPublic(good);
        int before = Stats.Get("poker.hands");
        foreach (var bad in new[] { null, "1 AsAs", "AsKh", "1 AsKh Qd", "x AsKh", "-1 AsKh", "1 AsKhQd", "1 Zz9c" }) w.OnPrivate(bad);
        Check(w.MyRank() == -1 && Stats.Get("poker.hands") == before, "bad secrets dropped");
        w.OnPrivate("1 AsKh"); w.OnPrivate("1 AsKh"); w.OnPrivate("0 2c2d");
        Check(w.MyRank() == 0 && Stats.Get("poker.hands") == before + 1 && w.Lines(1, Name).Any(x => x.text == "Your cards: As Kh"), "our cards, the hand counted once, an older hand's ignored");
        w.OnPublic("2 100 1 0 1 100 200 1 - 1:4900:100:100:p:-:0,2:4950:50:50:p:-:0");
        Check(w.MyRank() == -1 && !w.Lines(1, Name).Any(x => x.text.StartsWith("Your cards")), "a new hand: the last hand's cards don't show");
        Check(w.Options(1).Count == 0 && w.Options(2).Count > 0 && w.Options(9).Count == 0, "only the seat to act has moves");
    }

    static void Check(bool ok, string what) { if (!ok) throw new Exception(what); }

    // ---- update check ----
    static void UpdateVersions()
    {
        Check(UpdateCheck.Newer("0.6.10", "0.6.9"), "0.6.10 after 0.6.9");
        Check(UpdateCheck.Newer("0.7.0", "0.6.6"), "0.7.0 after 0.6.6");
        Check(UpdateCheck.Newer("1.0", "0.9.9"), "1.0 after 0.9.9");
        Check(!UpdateCheck.Newer("0.6.6", "0.6.6"), "same version");
        Check(!UpdateCheck.Newer("0.6.7", "0.7.0-test"), "a test build of 0.7.0 is past 0.6.7");
        Check(!UpdateCheck.Newer("0.6.5", "0.6.6"), "older");
        Check(UpdateCheck.Newer("0.8.0", "0.8.0-beta.1"), "the release after its beta");
        Check(!UpdateCheck.Newer("0.8.0-beta.2", "0.8.0"), "a beta isn't offered over its release");
        Check(!UpdateCheck.Newer("0.9.0-beta.1", "0.8.0"), "nor to anyone on a release");
        Check(UpdateCheck.Newer("0.9.0-beta.1", "0.8.0-beta.1"), "a beta player gets the next beta");
        Check(UpdateCheck.Newer("0.8.0-beta.3", "0.8.0-beta.2"), "the next beta of the same version");
        Check(UpdateCheck.Newer("0.8.0-beta.10", "0.8.0-beta.9"), "beta.10 after beta.9");
        Check(!UpdateCheck.Newer("0.8.0-beta.2", "0.8.0-beta.2"), "the same beta");
        Check(!UpdateCheck.Newer("0.8.0-beta.1", "0.8.0-beta.2"), "an older beta");
        Check(!UpdateCheck.Newer("0.7.2", "0.8.0-beta.2"), "the release before the beta");
    }

    static void UpdateLatestText()
    {
        Check(UpdateCheck.Parse("0.6.7\r\n13\r\nFixed the thing.\r\nAnd more.\r\n", out var v, out int p, out var notes), "parsed");
        Check(v == "0.6.7" && p == 13 && notes == "Fixed the thing. And more.", $"fields: {v} {p} {notes}");
        Check(!UpdateCheck.Parse("../../evil\n13", out _, out _, out _), "a path is not a version");
        Check(UpdateCheck.Api("0.9.0\n13\nnotes\napi 2.3.0.0\n") == "2.3.0.0", "api line read");
        Check(UpdateCheck.Api("0.6.7\r\n13\r\nFixed the thing.\r\n") == null, "no api line in an old latest.txt");
        Check(!UpdateCheck.Parse("", out _, out _, out _), "empty");
    }

    // A throwaway key's signature of a known message (made with openssl dgst -sha256 -sign).
    const string TestKey = "uDYsMNqPbjtL0gBXoAhoffbHwqM1t6XC1pZRKyfFGqD8V2XEVNhwucIVr6qapE5HnZvsRTawQfIVKr2pIIblggi/+WtazlztTtTQijMzGZgwIfwsAC8YlLJyAyc/npnIsOtng1nbZphWamolBwOS1LM/G0r6t+EyVxdlMQIPTylFfMI4EPOLta3AFApw6dVxsnkYbxvBLT3c52fuiHfQl7StHIPJKFIvi5Gf+a/YN+kgBQeZz98yL702KzvuZiMJGM+241zb78z9R+20/gMvL7SoJn5T20asB7xpPzmn0cm67P9JKZ2uJyhRdKWQBmc6iljBsvNjvjTSjscn83YfkQ==";
    const string TestSig = "WYHyewuWX8ngbN1wKQQ1Kpky/4LIf9UC7mZi9vjgZQVPny5lOBd48ajQekMDT30d6Ppt7pyqhELtzhbfMAvT5n/yg4vfWh32v5bNqhvmL1hcHhnReCHTATOwAdBQ1pNjzoUa8ajm/KFviN7TVi27ANIkXo/EMYEH7+EnplB9mbZniPunD2WDo51pHfgDseZW+23rpUAY5DV1jYSZs2T4pbhi7RvNdBZC4lafLAlw9LChVPaYehWhI9wDZcAyR6iH9Vuk5yunSygCqmSGAtJIx+6MF28IIFYpTVB4t3bw4XZsn18C/oHPxjJvWn4PGPNZkqikJqn9IRnjR19WpJ9jpg==";

    static void BetaLogs()
    {
        Check(LogShare.IsBeta("0.9.0-beta.6") && LogShare.IsBeta("0.7.4-test") && !LogShare.IsBeta("0.7.3"), "betas and test builds have a dash");
        Check(LogShare.EveryFor("0.9.0-beta.8.4") == LogShare.EveryMs && LogShare.EveryFor("0.7.62") == LogShare.StableEveryMs, "betas send every 10 minutes, stable every 30");
        Check(LogShare.Label("0.7.62") == "Co-op logs" && LogShare.Label("0.9.0-beta.8.4") == "Beta logs" && LogShare.QuestionFor("0.7.62").Contains("Y = yes"), "stable asks too, in its own words");
        Check(LogShare.Parse("Yes") == LogShare.Consent.Yes && LogShare.Parse(" no") == LogShare.Consent.No && LogShare.Parse("") == LogShare.Consent.Unasked && LogShare.Parse("maybe") == LogShare.Consent.Unasked, "consent words");
        string s = LogShare.Scrub("joined example.ddns.net:27460 as player 2\nserver: Lykos joined as guest (id 2) from 100.64.0.7\nsettings E:\\SteamLibrary\\x\\RDR2Coop.ini\nsettings C:\\Users\\Dave Smith\\x\nCo-op: calling 188.64.35.234:27461\n/home/dave/.steam\nfe80::1ff:fe23:4567:890a x");
        Check(!s.Contains("example.ddns") && s.Contains("joined [server]:27460"), "server names blanked: " + s);
        Check(!s.Contains("100.64.0.7") && s.Contains("from [ip]") && !s.Contains("188.64") && !s.Contains("fe80"), "addresses blanked: " + s);
        Check(!s.Contains("Dave Smith") && s.Contains("C:\\Users\\[user]\\x") && s.Contains("/home/[user]/.steam") && s.Contains("E:\\SteamLibrary\\x"), "user folders blanked, other paths kept: " + s);
        Check(LogShare.Scrub("Lykos's look applied (26 pieces)") == "Lykos's look applied (26 pieces)", "names and plain lines untouched");
        string big = new string('a', LogShare.MaxBytes) + "\nlast line";
        string tail = LogShare.Tail(big);
        Check(tail.StartsWith("[earlier lines cut]") && tail.EndsWith("last line") && System.Text.Encoding.UTF8.GetByteCount(tail) <= LogShare.MaxBytes, "the tail fits and ends on the last line");
        Check(LogShare.Tail("short") == "short", "a short log is whole");
        Check(!LogShare.Due(30_000, -1, 0) && LogShare.Due(61_000, -1, 0), "first upload a minute after the start");
        Check(!LogShare.Due(500_000, 100_000, 0) && LogShare.Due(100_000 + LogShare.EveryMs + 1, 100_000, 0), "then every 10 minutes");
        Check(!LogShare.Due(100_000 + LogShare.EveryMs + 1, 100_000, 0, LogShare.StableEveryMs) && LogShare.Due(100_000 + LogShare.StableEveryMs + 1, 100_000, 0, LogShare.StableEveryMs), "stable: every 30 minutes");
    }

    static void IniSet()
    {
        string t = "[Coop]\r\nName=Arthur\r\n\r\n[Testing]\r\nTraceJoins=No\r\n";
        string u = Ini.Apply(t, "Testing", new System.Collections.Generic.Dictionary<string, string> { { "SendLogs", "Yes" }, { "TraceJoins", "Yes" } });
        Check(u.Contains("[Testing]\r\nSendLogs=Yes\r\nTraceJoins=Yes") && u.Contains("Name=Arthur"), "added under the section, existing key replaced: " + u.Replace("\r\n", "|"));
        string v = Ini.Apply("[Coop]\r\nName=Arthur\r\n", "Testing", new System.Collections.Generic.Dictionary<string, string> { { "SendLogs", "No" } });
        Check(v.EndsWith("[Testing]\r\nSendLogs=No\r\n") && v.StartsWith("[Coop]"), "a missing section is added at the end: " + v.Replace("\r\n", "|"));
        Check(Ini.Parse(v).Get("Testing", "SendLogs", "") == "No", "and reads back");
    }

    static void UpdateSignature()
    {
        byte[] msg = System.Text.Encoding.ASCII.GetBytes("RDR2 Co-op update check test"), sig = Convert.FromBase64String(TestSig);
        Check(UpdateCheck.Verify(msg, sig, TestKey), "good signature passes");
        var changed = (byte[])msg.Clone(); changed[0] ^= 1;
        Check(!UpdateCheck.Verify(changed, sig, TestKey), "changed file refused");
        var badSig = (byte[])sig.Clone(); badSig[10] ^= 1;
        Check(!UpdateCheck.Verify(msg, badSig, TestKey), "changed signature refused");
        Check(!UpdateCheck.Verify(msg, sig, UpdateCheck.ReleaseKey), "another key's signature refused");
        Check(!UpdateCheck.Verify(msg, new byte[0], TestKey), "empty signature refused");
    }
    // ---- wear and emotes (0.8.0) ----
    static void WearRules()
    {
        Near(WearLevels.Wet(0f, 0.5f, 0), 0.5f, "waist deep: wet to the waist");
        Near(WearLevels.Wet(1f, 0f, (long)(WearLevels.DryMs / 2)), 0.5f, "half dry after half the drying time");
        Near(WearLevels.Wet(0.2f, 0f, (long)WearLevels.DryMs), 0f, "dry, not below");
        Near(WearLevels.Wet(0.9f, 0.3f, 0), 0.9f, "wading doesn't make wet clothes drier");
        Check(WearLevels.ToByte(float.NaN) == 0 && WearLevels.ToByte(7f) == 255 && WearLevels.ToByte(-1f) == 0, "levels clamp");

        byte shown = 0;
        Check(!WearLevels.Wetter(10, ref shown), "a splash isn't shown");
        Check(WearLevels.Wetter(255, ref shown) && shown == 255, "soaked is shown");
        Check(!WearLevels.Wetter(200, ref shown) && shown == 200, "drying isn't (the stand-in dries itself)");
        Check(WearLevels.Wetter(240, ref shown), "soaked again is shown");
        Check(!WearLevels.Wetter(0, ref shown) && shown == 0, "0 (or a player before 0.8.0) changes nothing");

        byte dirt = 0;
        Check(!WearLevels.DirtChanged(0, ref dirt), "clean stays clean");
        Check(!WearLevels.DirtChanged(20, ref dirt), "a speck isn't shown");
        Check(WearLevels.DirtChanged(150, ref dirt) && dirt == 150, "muddy is shown");
        Check(!WearLevels.DirtChanged(160, ref dirt), "a little more isn't");
        Check(WearLevels.DirtChanged(0, ref dirt) && dirt == 0, "a bath is shown");
    }

    static void EmoteWant()
    {
        Check(EmoteRules.Want(0xFEA62BA1, 0f, 0.4f) == 0xFEA62BA1, "standing by them: plays it");
        Check(EmoteRules.Want(0xFEA62BA1, 1.4f, 0.4f) == 0, "they walk off: stands up");
        Check(EmoteRules.Want(0xFEA62BA1, 0f, 5f) == 0, "still on its way to them: walks there first");
        Check(EmoteRules.Want(0, 0f, 0f) == 0, "no scenario (or a player before 0.8.0): nothing");
    }

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
            MountPosition = new Vec3(-300.4f, 800.2f, 116f), MountHeading = 270f, Following = 2, Weapon = 0x169F59F7,
            Pose = PoseFlags.Crouching | PoseFlags.Aiming, Shots = 7, AimAt = new Vec3(-310f, 805f, 120.5f), Scenario = 0xAABBCCDD,
            Carrying = 0x11223344, CarryKind = 1, Wet = 200, Dirt = 50, Health = 180, RidingWith = 3,
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
        Check(r.Weapon == 0x169F59F7, "weapon in hand");
        Check(r.Pose == (PoseFlags.Crouching | PoseFlags.Aiming) && r.Shots == 7 && r.Scenario == 0xAABBCCDD && r.Carrying == 0x11223344
              && r.CarryKind == 1 && r.Wet == 200 && r.Dirt == 50 && r.Health == 180 && r.RidingWith == 3, "pose block");
        Near(r.AimAt.Z, 120.5f, "aim point");
        // A state from 0.7.1 (weapon, no pose block) reads with default poses.
        var v071 = Protocol.Read(bytes, bytes.Length - 28);
        Check(v071 != null && v071.State.Weapon == 0x169F59F7 && v071.State.Pose == PoseFlags.None && v071.State.Carrying == 0, "0.7.1 state");
        // A state from before 0.7.1 (no weapon on the end) still reads, with empty hands.
        var old = Protocol.Read(bytes, bytes.Length - 28 - 4);
        Check(old != null && old.State.Seq == 42 && old.State.Weapon == 0, "old state without the weapon");
    }

    static void RejectsGarbage()
    {
        Check(Protocol.Read(new byte[] { 1, 2, 3, 4, 5, 6 }, 6) == null, "bad magic");
        var bytes = Protocol.State(At(1, 5));
        Check(Protocol.Read(bytes, bytes.Length - 28 - 4 - 3) == null, "truncated state");   // into the fields every version sends (the weapon on the end is optional)
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
        var h = Protocol.Hello("Al", false, "0.9.0-beta.7 net9");
        Check(Protocol.Read(h, h.Length).Build == "0.9.0-beta.7 net9", "build read back");
        Check(Protocol.Read(h, h.Length - 1 - "0.9.0-beta.7 net9".Length).Build == "", "a Hello without the build (older mod) reads as empty");
    }

    static void PartyListLines()
    {
        var o = new Vec3(100, 200, 50);
        Vec3 Off(float dx, float dy) => new Vec3(o.X + dx, o.Y + dy, o.Z);
        string[] want = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        Vec3[] to = { Off(0, 10), Off(10, 10), Off(10, 0), Off(10, -10), Off(0, -10), Off(-10, -10), Off(-10, 0), Off(-10, 10) };
        for (int i = 0; i < 8; i++) Check(PartyLines.Direction(o, to[i]) == want[i], $"direction {want[i]}: {PartyLines.Direction(o, to[i])}");
        Check(PartyLines.Direction(o, Off(2, 10)) == "N" && PartyLines.Direction(o, Off(-1, -10)) == "S", "nearest point wins");
        Check(PartyLines.Distance(12.4f) == "12 m" && PartyLines.Distance(2345f) == "2.3 km", "distance");
        Check(PartyLines.HealthByte(600, 600) == 255 && PartyLines.HealthByte(300, 600) == 128 && PartyLines.HealthByte(0, 600) == 1
              && PartyLines.HealthByte(900, 600) == 255 && PartyLines.HealthByte(-5, 600) == 1 && PartyLines.HealthByte(50, 0) == 255, "health byte: 1..255, never 0");
        var st = new PlayerState { Position = Off(0, 12), Health = 128 };
        Check(PartyLines.Line("Sam", false, false, st, o) == "Sam   50%   12 m N", PartyLines.Line("Sam", false, false, st, o));
        Check(PartyLines.Line("Arthur", true, true, new PlayerState { Position = o, Health = 255 }, o) == "Arthur (you, host)   100%", "our own line");
        st.Flags = StateFlags.Downed;
        Check(PartyLines.Line("Sam", true, false, st, o) == "Sam (host)   DOWN   12 m N", "down");
        st.Flags = StateFlags.Downed | StateFlags.Dead;
        Check(PartyLines.Line("Sam", false, false, st, o) == "Sam   dead   12 m N", "dead beats down");
        st.Flags = StateFlags.InCutscene;
        Check(PartyLines.Line("Sam", false, false, st, o) == "Sam   in a cutscene   12 m N", "cutscene");
        st.Flags = StateFlags.None; st.Position = Off(-2300, 0);
        Check(PartyLines.Line("Ann", false, false, st, o) == "Ann   50%   far away, 2.3 km W", PartyLines.Line("Ann", false, false, st, o));
        st.Health = 0;   // 0.7.x players send no health
        Check(PartyLines.Line("Old", false, false, st, o) == "Old   far away, 2.3 km W", "unknown health left out");
        Check(PartyLines.Line("Tim", false, false, null, o) == "Tim   loading", "no state yet");
        string bad = PartyLines.Line("~e~Evil\n<font>" + new string('x', 300), false, false, null, o);
        Check(bad == "eEvilfont" + new string('x', 11) + "   loading" && PartyLines.Line("~~", false, false, null, o) == "?   loading", "names cleaned: " + bad);
    }

    static void NameTagText()
    {
        Check(NameTag.Clean("~r~Bob~s~") == "Bob", "colour codes stripped: " + NameTag.Clean("~r~Bob~s~"));
        Check(NameTag.Clean(" Ann ~COLOR_RED~Lee ") == "Ann Lee", "a long code inside, spaces trimmed");
        Check(NameTag.Clean(null) == "" && NameTag.Clean("~q~") == "", "nothing left: empty (not drawn)");
        Check(NameTag.Clean(new string('x', 255)).Length == 32, "a hostile server's 255-byte name cut");
        Check(NameTag.Scale(0) == 0.40f && NameTag.Scale(5000) == 0.24f, "size clamped near and far");
        Near(NameTag.Scale(NameTag.Range), 0.24f, "smallest at the range", 0.001f);
        Check(NameTag.Scale(10) > NameTag.Scale(20) && NameTag.Scale(20) > NameTag.Scale(30), "smaller further away");
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

    static void RideDoubleRules()
    {
        var rider = new PlayerState { Seq = 5, Flags = StateFlags.Mounted | StateFlags.InMission, MountModel = 0x1234, Position = new Vec3(10, 20, 30),
                                      MountPosition = new Vec3(10, 20, 31), MountHeading = 90, Velocity = new Vec3(4, 0, 0), Weapon = 7 };
        var p = Riding.AsPassenger(rider, 3);
        Check(!p.Mounted && p.MountModel == 0 && p.MountHeading == 0 && p.MountPosition.Length == 0, "a passenger has no horse of their own");
        Check(p.RidingWith == 3 && p.Position.X == 10 && p.Velocity.X == 4 && p.Weapon == 7 && (p.Flags & StateFlags.InMission) != 0,
              "everything else is kept: where they are, how fast they go, the other flags");
        Check(Riding.CanCarry(rider) && !Riding.CanCarry(p), "riders carry, passengers don't");
        var down = rider; down.Flags |= StateFlags.Downed;
        var dead = rider; dead.Flags |= StateFlags.Dead;
        Check(!Riding.CanCarry(down) && !Riding.CanCarry(dead) && !Riding.CanCarry(new PlayerState()), "nobody down, dead or on foot carries (or a 0.7 state)");
        var wire = Protocol.State(p);
        var back = Protocol.Read(wire, wire.Length).State;
        Check(back.RidingWith == 3 && !back.Mounted && back.MountModel == 0, "a passenger's state crosses the wire as it is");
    }

    // ---- carrying ----

    static void CarryWhere()
    {
        var body = new PlayerState { Carrying = 0x11223344, CarryKind = Carry.Shoulder };
        Check(Carry.Where(new PlayerState()) == Carry.Nothing, "0.7.x state: nothing");
        Check(Carry.Where(body) == Carry.Shoulder, "on foot: shoulder");
        var riding = body; riding.Flags = StateFlags.Mounted;
        Check(Carry.Where(riding) == Carry.Nothing, "shoulder while riding: nothing");
        var down = body; down.Flags = StateFlags.Downed;
        Check(Carry.Where(down) == Carry.Nothing, "shoulder while down: nothing");
        var seated = body; seated.VehicleId = 4;
        Check(Carry.Where(seated) == Carry.Nothing, "shoulder in a wagon: nothing");
        var behind = body; behind.RidingWith = 2;
        Check(Carry.Where(behind) == Carry.Nothing, "shoulder while sitting behind a rider (ride double): nothing");
        var stowed = riding; stowed.CarryKind = Carry.Horse;
        Check(Carry.Where(stowed) == Carry.Horse, "riding: on the horse");
        var walking = stowed; walking.Flags = StateFlags.None;
        Check(Carry.Where(walking) == Carry.Horse, "horse load on foot: still on their horse (it may wait for them)");
        var odd = body; odd.CarryKind = 7;
        Check(Carry.Where(odd) == Carry.Nothing, "unknown kind: nothing");
        var empty = stowed; empty.Carrying = 0;
        Check(Carry.Where(empty) == Carry.Nothing, "kind without a model: nothing");
    }

    static void CarryBiggest()
    {
        Check(Carry.Biggest(new float[0]) == -1, "none");
        Check(Carry.Biggest(new[] { 0.5f, 2.5f, 1f }) == 1, "the biggest");
        Check(Carry.Biggest(new[] { 2f, 2f }) == 0, "a tie keeps the first");
        Check(Carry.Biggest(new[] { float.NaN, 1f }) == 1, "a bad size loses");
        Check(Carry.Biggest(new[] { float.NaN }) == 0, "a bad size alone still counts");
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

    static void WaypointNotices()
    {
        var at = new Vec3(-283.4f, 790.2f, 118f);
        Check(WaypointNotice.TryParse(WaypointNotice.Encode(at), out var back) && back.HasValue && Vec3.Distance(back.Value, at) < 0.06f, "a waypoint round trips");
        Check(WaypointNotice.TryParse("", out back) && back == null && WaypointNotice.TryParse(null, out back) && back == null, "empty text clears it");
        Check(!WaypointNotice.TryParse("1e9 2 3", out back) && !WaypointNotice.TryParse("NaN 2 3", out back) && !WaypointNotice.TryParse("1 Infinity 3", out back),
              "off-map, NaN and infinite numbers ignored");
        Check(!WaypointNotice.TryParse("12 34", out back) && !WaypointNotice.TryParse("a b c", out back), "garbage ignored");
        Check(WaypointNotice.TryParse("1 2 3 more", out back) && back.Value.Z == 3f, "fields a newer version adds are skipped");
        Check(WaypointNotice.Encode(new Vec3(float.NaN, 1e9f, -1e9f)) == "0 20000 -20000", "what we send is in range too");
        Check(WaypointNotice.Follow(false, at, false, false).Equals(at), "a guest with no waypoint follows the host's");
        Check(WaypointNotice.Follow(false, at, true, false) == null, "never over the player's own waypoint");
        Check(WaypointNotice.Follow(true, at, false, false) == null && WaypointNotice.Follow(false, at, false, true) == null
              && WaypointNotice.Follow(false, null, false, false) == null, "not the host, not in a story mission, not when there's none");
    }

    static void ExplosionNotices()
    {
        var at = new Vec3(-283.12f, 790.45f, 118.3f);
        Check(Blast.Read(Blast.Write(25, at), out int kind, out var p) && kind == 25 && Vec3.Distance(p, at) < 0.01f, "dynamite round trip");
        Check(Blast.Read("2 -283.12 790.45 118.30 later-field", out kind, out _) && kind == 2, "fields a later version adds are ignored");
        Check(!Blast.Read("5 1 2 3", out _, out _) && !Blast.Read("33 1 2 3", out _, out _), "a wagon's or lightning's explosion isn't passed on");
        foreach (var bad in new[] { null, "", "25 1 2", "x 1 2 3", "25 NaN 2 3", "25 1 Infinity 3", "25 1 2 30000", "25 1,5 2 3" })
            Check(!Blast.Read(bad, out _, out _), "refused: " + (bad ?? "null"));
        var gate = new BlastGate();
        int let = 0;
        for (int i = 0; i < 20; i++) if (gate.Allow(i * 10)) let++;
        Check(let == BlastGate.Max, "a burst is capped");
        Check(!gate.Allow(999) && gate.Allow(1000), "allowed again a second after the first");
    }

    static void ChatRules()
    {
        string c = ChatLog.Clean("~COLOR_RED~hi~s~ there\n\t you~");
        Check(c == "hi there you", "codes, control characters, a stray ~: '" + c + "'");
        Check(ChatLog.Clean("a\u202Eb\u200Bc") == "abc" && ChatLog.Clean(null) == "" && ChatLog.Clean(" \r\n ") == "", "invisible characters, empty");
        Check(ChatLog.Clean(new string('x', 300)).Length == ChatLog.MaxChars, "cut at 200");
        Check(ChatLog.Clean(new string('x', 199) + "\U0001F600").Length == 199, "never half an emoji");
        var w = ChatLog.Wrap("aaa bbb ccc", 7);
        Check(w.Count == 2 && w[0] == "aaa bbb" && w[1] == "ccc", "wraps at a space: " + string.Join("|", w));
        w = ChatLog.Wrap(new string('x', 15), 7);
        Check(w.Count == 3 && w[0].Length == 7 && w[2] == "x", "a long word is cut: " + string.Join("|", w));
        var log = new ChatLog();
        Check(log.Add("~r~Bob", "hello", 0) == "Bob: hello" && log.Add("Bob", " ~n~ ", 0) == null && log.Add("~~", "hi", 0) == "?: hi",
              "name cleaned, nothing left = nothing shown");
        for (int i = 0; i < 6; i++) log.Add("Al", "line " + i, 1000);
        var v = log.Visible(1000, false);
        Check(v.Count == ChatLog.MaxLines && v[v.Count - 1] == "Al: line 5", "the newest kept: " + string.Join("|", v));
        Check(log.Visible(1000 + ChatLog.ShowMs, false).Count == 0 && log.Visible(1000 + ChatLog.ShowMs, true).Count == ChatLog.MaxLines,
              "gone after 10 s, still there while typing");
        log.Add("Al", new string('y', 150), 2000);
        Check(log.Visible(2000, false).Count(l => l.Contains("y")) == 3, "a long message takes 3 lines");
        Check(log.Allow(2, 0) && log.Allow(2, 100) && log.Allow(2, 200) && !log.Allow(2, 300) && log.Allow(3, 300) && log.Allow(2, 1000),
              "3 a second from each partner");
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

    // A fake story mission table: count, then per mission a start point and a marker handle (0 = no marker).
    static void MissionMarkersFromTable()
    {
        var ints = new System.Collections.Generic.Dictionary<int, int>();
        var floats = new System.Collections.Generic.Dictionary<int, float>();
        int highest = 0;
        Func<int, int> gi = g => { highest = Math.Max(highest, g); return ints.TryGetValue(g, out var v) ? v : 0; };
        Func<int, float> gf = g => floats.TryGetValue(g, out var v) ? v : 0f;
        void Mission(int slot, int marker, float x, float y, float z)
        {
            int b = MissionMarkers.Table + 1 + MissionMarkers.EntrySize * slot;
            ints[b + MissionMarkers.MarkerField] = marker;
            floats[b + MissionMarkers.StartField] = x; floats[b + MissionMarkers.StartField + 1] = y; floats[b + MissionMarkers.StartField + 2] = z;
        }
        var near = new Vec3(0, 0, 0);

        ints[MissionMarkers.Table] = 5;
        Mission(0, 0x1502, 0f, 0f, 40f);          // no start point: skipped
        Mission(1, 0x1702, 100f, 200f, 30f);
        Mission(2, 0, 300f, 300f, 30f);           // no marker: not shown on the host's map
        Mission(3, 0x1902, -50f, 60f, 10f);
        Mission(4, 0x1A02, float.NaN, 5f, 5f);    // a misread: never sent
        var list = MissionMarkers.Read(gi, gf, near, 8f);
        Check(list.Count == 2 && list[0].Position.X == 100f && list[0].Position.Y == 200f && list[0].Position.Z == 30f
              && list[1].Position.X == -50f && list.All(o => o.Radius == 8f), "shown missions, in table order: " + list.Count);
        Check(MissionMarkers.Same(list, MissionMarkers.Read(gi, gf, near, 8f)), "the same table reads the same");
        Mission(2, 0x1B02, 300f, 300f, 30f);
        var more = MissionMarkers.Read(gi, gf, near, 8f);
        Check(more.Count == 3 && !MissionMarkers.Same(list, more), "a new marker changes the list");
        Mission(2, 0, 300f, 300f, 30f);
        Check(MissionMarkers.Same(list, MissionMarkers.Read(gi, gf, near, 8f)), "and its going changes it back");

        ints.Clear(); floats.Clear();
        ints[MissionMarkers.Table] = 12;
        for (int i = 0; i < 12; i++) Mission(i, 0x1002 + i * 0x100, (12 - i) * 100f, 0f, 0f);   // slot 11 nearest
        var capped = MissionMarkers.Read(gi, gf, near, 8f);
        Check(capped.Count == Objective.Max && capped[0].Position.X == 800f && capped[7].Position.X == 100f,
              $"the 8 nearest, in table order: {capped.Count}, {capped[0].Position.X}..{capped[capped.Count - 1].Position.X}");
        string text = MissionMarkers.Write(capped);
        Check(text.Length <= NoticeMsg.MaxBytes && MissionMarkers.Same(capped, MissionMarkers.Parse(text, 8f)), "they go in one MissionStep notice as they are: " + text.Length);
        Check(MissionMarkers.Write(new System.Collections.Generic.List<Objective>()) == "" && MissionMarkers.Parse("", 8f).Count == 0, "none: an empty notice");
        var odd = MissionMarkers.Parse(string.Join("\n", "1 2", "NaN 1 1", "0 0 5", "1e9 1 1", "zz 1 1", "12.5 -40 3 x"), 8f);
        Check(odd.Count == 1 && odd[0].Position.X == 12.5f && odd[0].Position.Z == 3f && odd[0].Radius == 8f, "bad lines dropped, a newer version's fields left: " + odd.Count);
        Check(MissionMarkers.Parse(string.Join("\n", Enumerable.Repeat("1 2 3", 50)), 8f).Count == Objective.Max, "8 at most");

        ints.Clear(); floats.Clear(); highest = 0;
        ints[MissionMarkers.Table] = 1000000;   // a misread count: stays inside the table's possible size
        MissionMarkers.Read(gi, gf, near, 8f);
        Check(highest <= MissionMarkers.Table + 1 + MissionMarkers.EntrySize * 128, "read past the table: " + highest);
        ints[MissionMarkers.Table] = -3;
        Check(MissionMarkers.Read(gi, gf, near, 8f).Count == 0, "negative count");
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

    static void AimPoints()
    {
        var cam = new Vec3(100, 200, 10);
        var north = Aim.Along(cam, new Vec3(0, 0, 0));
        Near(north.X, 100, "north x"); Near(north.Y, 200 + Aim.Reach, "north y"); Near(north.Z, 10, "north z");
        var west = Aim.Along(cam, new Vec3(0, 0, 90));
        Near(west.X, 100 - Aim.Reach, "heading 90 looks west"); Near(west.Y, 200, "west y");
        var down = Aim.Along(cam, new Vec3(-30, 0, 180));
        Near(down.Y, 200 - Aim.Reach * 0.866f, "south, tilted down", 0.05f); Near(down.Z, 10 - Aim.Reach * 0.5f, "30 degrees down", 0.05f);
        var me = new Vec3(0, 0, 0);
        Check(!Aim.Moved(me, new Vec3(0, 50, 0), new Vec3(0, 50, 1)), "a metre at 50 m stays");
        Check(Aim.Moved(me, new Vec3(0, 50, 0), new Vec3(0, 50, 4)), "four metres at 50 m re-aims");
        Check(!Aim.Moved(me, new Vec3(0, 2, 0), new Vec3(0, 2, 0.15f)), "close up, a few cm stays");
        Check(Aim.Moved(me, new Vec3(0, 2, 0), new Vec3(0, 2, 0.3f)), "close up, 30 cm re-aims");
    }

    // ---- stance (0.8.0) ----

    static void StanceJumps()
    {
        var t = new StanceTracker();
        Check(t.Update(PoseFlags.None, false, 0) == StanceStep.None, "no pose (older versions): nothing");
        Check(t.Update(PoseFlags.Crouching | PoseFlags.Swimming, false, 50) == StanceStep.None, "crouching and swimming start no move");
        Check(t.Update(PoseFlags.Jumping, true, 100) == (StanceStep.Jump | StanceStep.Hold), "a jump: one jump, steering waits");
        Check(t.Update(PoseFlags.Jumping, true, 200) == StanceStep.Hold, "no second jump while the flag stays up");
        Check(t.Update(PoseFlags.None, true, 300) == StanceStep.Hold, "the jump plays out");
        Check(t.Update(PoseFlags.Jumping, true, 400) == StanceStep.Hold, "no new move while one plays");
        Check(t.Update(PoseFlags.None, true, 950) == StanceStep.None, "steering is back");
        Check(t.Update(PoseFlags.None, true, 1900) == StanceStep.None, "level check not yet");
        Check(t.Update(PoseFlags.None, true, 1960) == StanceStep.CheckLevel, "a second after the jump ended: level check");
        Check(t.Update(PoseFlags.None, true, 2000) == StanceStep.None, "checked once");
        Check(t.Update(PoseFlags.Jumping, false, 2100) == StanceStep.None, "out of reach: no jump");
        Check(StanceTracker.OffLevel(new Vec3(0, 0, 0), new Vec3(1, 1, 2.5f)), "below the wall they climbed");
        Check(!StanceTracker.OffLevel(new Vec3(0, 0, 0), new Vec3(1, 1, 1f)), "same level");
        Check(!StanceTracker.OffLevel(new Vec3(0, 0, 0), new Vec3(10, 0, 3f)), "far off: following catches it up");
    }

    static void StanceClimbsAndFalls()
    {
        var t = new StanceTracker();
        t.Update(PoseFlags.None, true, 0);
        Check(t.Update(PoseFlags.Climbing, true, 100) == (StanceStep.Climb | StanceStep.Hold), "a climb near them");
        var step = StanceStep.None;
        for (long ms = 200; ms <= 8000; ms += 100) step = t.Update(PoseFlags.Climbing, true, ms);
        Check(step == StanceStep.Hold, "steering waits while they climb");
        Check(t.Update(PoseFlags.Climbing, true, 8100) == StanceStep.None, "but not past 8 s (a stuck flag can't freeze it)");
        for (long ms = 8200; ms < 9200; ms += 100) step = t.Update(PoseFlags.None, true, ms);
        Check(step == StanceStep.None && t.Update(PoseFlags.None, true, 9200) == StanceStep.CheckLevel, "level check a second after the climb");
        Check(t.Update(PoseFlags.Climbing, false, 9300) == StanceStep.Hold, "out of reach: no climb, but it still waits");

        var f = new StanceTracker();
        f.Update(PoseFlags.None, true, 0);
        for (long ms = 100; ms < 900; ms += 100) Check(f.Update(PoseFlags.Falling, true, ms) == StanceStep.None, "a short drop: nothing");
        Check(f.Update(PoseFlags.Falling, true, 900) == (StanceStep.Ragdoll | StanceStep.Hold), "falling 0.8 s: limp");
        Check(f.Update(PoseFlags.Ragdoll, true, 1000) == StanceStep.Hold, "limp on landing too: no second ragdoll");
        Check(f.Update(PoseFlags.None, true, 1500) == StanceStep.Hold, "the ragdoll plays out");
        Check(f.Update(PoseFlags.None, true, 2400) == StanceStep.None && f.Update(PoseFlags.None, true, 2500) == StanceStep.CheckLevel, "up, then a level check");
        Check(f.Update(PoseFlags.Ragdoll, true, 2600) == (StanceStep.Ragdoll | StanceStep.Hold), "knocked over again: limp again");
        Check(f.Update(PoseFlags.Ragdoll | PoseFlags.Jumping | PoseFlags.Climbing, true, 10000) == StanceStep.None,
              "after a break (on a horse, in a vehicle, down) nothing starts");

        var g = new StanceTracker();
        g.Update(PoseFlags.None, false, 0);
        Check(g.Update(PoseFlags.Ragdoll, false, 100) == StanceStep.None && g.Update(PoseFlags.Ragdoll, true, 200) == StanceStep.None,
              "knocked over out of reach: no ragdoll, not even once back in reach");
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
        others[1] = O(250);
        Check(Authority.Pick(3, new Vec3(0, 0, 0), 1, others, 3) == 2, "host 250 m away: not joined");
        Check(Authority.Pick(3, new Vec3(0, 0, 0), 1, others, 1) == 1, "but kept while under 300 m");
        others[1] = O(350);
        Check(Authority.Pick(3, new Vec3(0, 0, 0), 1, others, 1) == 2, "dropped past 300 m");
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

    static OtherPlayer Away(float x, byte following = 0) => new OtherPlayer { Position = new Vec3(x, 0, 0), Following = following, Away = true };

    static void AuthorityAway()
    {
        var o = new Vec3(0, 0, 0);
        var others = new System.Collections.Generic.Dictionary<byte, OtherPlayer> { [1] = Away(100), [2] = O(50) };
        Check(Authority.Pick(3, o, 1, others, 3) == 2, "a paused host doesn't own: the next player does");
        Check(Authority.Pick(3, o, 1, others, 1) == 2, "the current owner going silent is dropped at once");
        Check(Authority.Pick(3, o, 1, new System.Collections.Generic.Dictionary<byte, OtherPlayer> { [1] = Away(10) }, 1) == 3, "alone with a paused host: my own area");
        var chain = new System.Collections.Generic.Dictionary<byte, OtherPlayer> { [2] = O(100, following: 1), [1] = Away(400) };
        Check(Authority.Pick(3, o, 9, chain, 3) == 3, "a chain ending on a paused game: my own area");
        var fine = new System.Collections.Generic.Dictionary<byte, OtherPlayer> { [1] = O(100), [2] = O(50) };
        Check(Authority.Pick(3, o, 1, fine, 3) == 1, "nobody away: the host, as before");
    }

    static byte[] Header(PacketType t) => new byte[] { (byte)'R', (byte)'D', (byte)'C', (byte)'O', (byte)t, 0, 0 };

    static void StalledFilter()
    {
        Check(!ServerLink.KeepWhileStalled(Protocol.State(At(1, 5))), "positions dropped");
        Check(!ServerLink.KeepWhileStalled(Protocol.World(new WorldSnapshot { Seq = 1, Entities = new System.Collections.Generic.List<EntityState> { Ent(1, 1) } })[0]), "snapshots dropped");
        Check(!ServerLink.KeepWhileStalled(Protocol.HitPacket(new Hit { Seq = 1, Damage = 5 }, 2)), "hits on us dropped (the game was frozen: it took none)");
        Check(ServerLink.KeepWhileStalled(Protocol.HitPacket(new Hit { Seq = 1, EntityId = 7, Damage = 5 }, 2)), "hits on our NPCs kept (applied on the first tick back)");
        Check(ServerLink.KeepWhileStalled(Protocol.ActionPacket(new GuestAction { Seq = 1, Kind = ActionKind.Hogtie }, 2)), "actions kept (a hogtie is never resent)");
        Check(!ServerLink.KeepWhileStalled(Header(PacketType.Challenge)), "challenges dropped (answered later, they'd be stale)");
        Check(ServerLink.KeepWhileStalled(Protocol.NoticePacket(new NoticeMsg { Kind = NoticeKind.Text, Text = "hi" }, Protocol.Broadcast)), "notices kept");
        Check(ServerLink.KeepWhileStalled(Protocol.PartyPacket(new PartyMsg { Kind = PartyKind.Invite, PartyId = 1 })), "party kept");
        foreach (var t in new[] { PacketType.Welcome, PacketType.Roster, PacketType.Reject, PacketType.Appearance, PacketType.EntityLook, PacketType.Objectives })
            Check(ServerLink.KeepWhileStalled(Header(t)), t + " kept");
        Check(ServerLink.KeepWhileStalled(new byte[3]), "too short to have a type: left for Handle to reject");
    }

    // ---- the relay's logic, no sockets ----

    static System.Net.IPEndPoint Addr(string ip, int port) => new System.Net.IPEndPoint(System.Net.IPAddress.Parse(ip), port);

    static Packet First(System.Collections.Generic.List<(byte[] data, System.Net.IPEndPoint to)> output, PacketType type, System.Net.IPEndPoint to)
    {
        foreach (var (data, at) in output)
            if (at.Equals(to)) { var p = Protocol.Read(data, data.Length); if (p != null && p.Type == type) return p; }
        return null;
    }

    static Packet Join(Relay r, System.Net.IPEndPoint from, string name, bool prefer = false)
    {
        var ch = r.Handle(Protocol.Hello(name, prefer), from);
        var nonce = First(ch, PacketType.Challenge, from).Nonce;
        return First(r.Handle(Protocol.Auth(nonce, "pw"), from), PacketType.Welcome, from);
    }

    static void HitDamageChecked()
    {
        Check(Hit.Sane(1) && Hit.Sane(Hit.MaxDamage), "a gun's damage");
        Check(Hit.Sane(Hit.Kill), "the kill marker a guest sends when it killed its copy outright (dropped since beta.8)");
        Check(!Hit.Sane(0) && !Hit.Sane(-5) && !Hit.Sane(Hit.MaxDamage + 1) && !Hit.Sane(Hit.Kill - 1), "anything else is a bad packet");
    }

    static void RetryBookWaits()
    {
        var b = new RetryBook();
        Check(b.CanTry(7, 0), "a model never refused can be tried");
        Check(b.Failed(7, 1000, 5000), "the first refusal is logged");
        Check(!b.CanTry(7, 3000) && !b.CanTry(7, 5999), "not again before the wait is over");
        Check(b.CanTry(7, 6000), "tried again after 5 s");
        Check(!b.Failed(7, 7000, 5000), "a second refusal within a minute isn't logged again");
        Check(b.Failed(7, 62000, 5000), "a minute later it is");
        Check(b.CanTry(8, 7000), "other models aren't held up");
    }

    static void PartyDownAll()
    {
        Check(!PartyDown.AllDown(new bool[0]), "alone: nobody to wait for, so not 'all down' (the down state is off anyway)");
        Check(!PartyDown.AllDown(new[] { true, false }), "one partner still up");
        Check(PartyDown.AllDown(new[] { true }) && PartyDown.AllDown(new[] { true, true, true }), "everyone down");
    }

    static void LogsInstallIds()
    {
        Check(LogShare.ValidInstall("2a2e49e3b7fc") && LogShare.ValidInstall("0242e671-b847"), "hex ids pass");
        Check(!LogShare.ValidInstall("") && !LogShare.ValidInstall(null) && !LogShare.ValidInstall("my pc") && !LogShare.ValidInstall("abc"), "empty, typed or short ids don't");
        string n = LogShare.NewInstall();
        Check(LogShare.ValidInstall(n) && n.Length == 12 && n != LogShare.NewInstall(), "new ids are valid and different");
    }

    static void AuthorityWhyOwn()
    {
        var me = new Vec3(0, 0, 0);
        var others = new System.Collections.Generic.Dictionary<byte, OtherPlayer>();
        Check(Authority.WhyOwn(me, 1, others, 2).Contains("no position"), "no host state");
        others[1] = new OtherPlayer { Position = new Vec3(250, 0, 0) };
        Check(Authority.WhyOwn(me, 1, others, 2).Contains("250 m away (over 200 m)"), "joining needs 200 m");
        Check(Authority.WhyOwn(me, 1, others, 1) == "unknown", "following the host, 250 m is inside the 300 m leave radius");
        others[1] = new OtherPlayer { Position = new Vec3(10, 0, 0), Away = true };
        Check(Authority.WhyOwn(me, 1, others, 1).Contains("silent"), "a paused host");
        others[1] = new OtherPlayer { Position = new Vec3(10, 0, 0), Following = 3 };
        Check(Authority.WhyOwn(me, 1, others, 1).Contains("player 3"), "the host shows someone else's area");
    }

    static void ListServerSetting()
    {
        const string D = "https://list.example";
        Check(RoomList.ListServerFrom("", D, out var n1) == D && n1 == null, "empty: the default");
        Check(RoomList.ListServerFrom(" OFF ", D, out _) == "", "off: off");
        Check(RoomList.ListServerFrom("https://mine.example/", D, out var n2) == "https://mine.example/" && n2 == null, "a web address as given");
        Check(RoomList.ListServerFrom("81.2.69.160", D, out var n3) == D && n3.Contains("isn't a web address"), "an IP address: the default, with why");
        Check(RoomList.ListServerFrom("Yes", D, out _) == D, "a guess: the default");
        Check(RoomList.ListServerFrom("ftp://x.example", D, out _) == D, "not http(s): the default");
    }

    static void ListRefusalText()
    {
        Check(RoomList.ErrorText("{\"error\":\"your internet provider shares your address\"}") == "your internet provider shares your address", "the sentence only");
        Check(RoomList.ErrorText("Bad Gateway") == "Bad Gateway", "not JSON: as it came");
        Check(RoomList.ErrorText("{}") == "{}", "JSON without an error: as it came");
    }

    static void RelayJoinLogsBuild()
    {
        var lines = new System.Collections.Generic.List<string>();
        double t = 0;
        var r = new Relay("pw", () => t, lines.Add);
        var a = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 40001);
        var nonce = First(r.Handle(Protocol.Hello("Al", true, "0.9.0-beta.7 net9"), a), PacketType.Challenge, a).Nonce;
        r.Handle(Protocol.Auth(nonce, "pw"), a);
        Check(lines.Exists(l => l.Contains("Al joined as host") && l.Contains("mod 0.9.0-beta.7 net9")), "the build is in the join line: " + string.Join(" | ", lines));
        var b = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 40002);
        var old = Protocol.Hello("Bo", false);
        Array.Resize(ref old, old.Length - 1);   // a mod before 0.9.0-beta.7 sends no build string
        nonce = First(r.Handle(old, b), PacketType.Challenge, b).Nonce;
        r.Handle(Protocol.Auth(nonce, "pw"), b);
        Check(lines.Exists(l => l.Contains("Bo joined as guest") && l.Contains("mod older than")), "an old mod is named as such: " + string.Join(" | ", lines));
    }

    static void RelayRepeatHello()
    {
        double t = 0;
        var r = new Relay("pw", () => t, null);
        var a = Addr("10.0.0.1", 1);
        var n1 = First(r.Handle(Protocol.Hello("A", false), a), PacketType.Challenge, a).Nonce;
        t += 2;
        var n2 = First(r.Handle(Protocol.Hello("A", false), a), PacketType.Challenge, a).Nonce;
        Check(n1.SequenceEqual(n2), "the same challenge again");
        var w = First(r.Handle(Protocol.Auth(n1, "pw"), a), PacketType.Welcome, a);
        Check(w != null && w.MyId == 1, "the first challenge's answer is accepted");
        var b = Addr("10.0.0.2", 2);
        var m1 = First(r.Handle(Protocol.Hello("B", false), b), PacketType.Challenge, b).Nonce;
        t += 11;
        var m2 = First(r.Handle(Protocol.Hello("B", false), b), PacketType.Challenge, b).Nonce;
        Check(!m1.SequenceEqual(m2), "after the challenge expired: a new one");
    }

    static void RelayHelloOnly()
    {
        double t = 0;
        var r = new Relay("pw", () => t, null);
        var a = Addr("10.0.0.1", 1); var b = Addr("10.0.0.2", 2);
        Join(r, a, "A"); Join(r, b, "B");
        for (int i = 0; i < 30; i++)
        {
            t += 1;
            Check(First(r.Handle(Protocol.Hello("A", false), a), PacketType.Welcome, a) != null, "a known address's Hello is answered with its Welcome");
            r.Handle(Protocol.State(At(1, 5)), b);
            Check(r.Expire().Count == 0, "nobody dropped at " + i + " s");
        }
        Check(r.PlayerCount == 2, "both in after 30 s of Hello-only from A");
        t += 11;
        r.Handle(Protocol.State(At(2, 5)), b);
        var output = r.Expire();
        Check(r.PlayerCount == 1 && First(output, PacketType.Roster, b)?.Roster.Count == 1, "11 s of silence: A dropped, B told");
    }

    static void RelayPreferHostRejoin()
    {
        double t = 0;
        var r = new Relay("pw", () => t, null);
        var a = Addr("10.0.0.1", 1); var b = Addr("10.0.0.2", 2);
        Check(Join(r, a, "Host", prefer: true).HostId == 1, "first in hosts");
        Join(r, b, "Friend");
        t += 5; r.Handle(Protocol.State(At(1, 5)), b);
        t += 6; r.Expire();   // the host silent 11 s: dropped, Friend stands in
        Check(First(r.Handle(Protocol.Hello("Friend", false), b), PacketType.Welcome, b).HostId == 2, "Friend stands in as host");
        var back = Join(r, Addr("10.0.0.1", 3), "Host", prefer: true);   // same machine, a new socket
        Check(back.MyId == 1 && back.HostId == 1, $"the host gets its role back on rejoin: id {back.MyId}, host {back.HostId}");
        var stranger = Join(r, Addr("10.0.0.3", 4), "Newcomer", prefer: true);
        Check(stranger.HostId == 1, "a PreferHost player joining someone else's room stays a guest");

        r = new Relay("pw", () => t, null);
        Join(r, a, "Host", prefer: true); Join(r, b, "Alice", prefer: true);
        t += 5; r.Handle(Protocol.State(At(1, 5)), b);
        t += 6; r.Expire();
        Check(Join(r, a, "Host", prefer: true).HostId == 2, "a stand-in who prefers hosting too keeps it");

        r = new Relay("pw", () => t, null);
        Join(r, a, "Host", prefer: true); Join(r, b, "Friend");
        t += 5; r.Handle(Protocol.State(At(1, 5)), b);
        t += 6; r.Expire();
        t += 121; r.Handle(Protocol.State(At(2, 5)), b); r.Expire();
        Check(Join(r, a, "Host", prefer: true).HostId == 2, "too late (2 minutes): the stand-in keeps it");
    }

    // ---- card and table games (0.8.0) ----
    static void TableNotices()
    {
        string sent = null; long ms = 0;
        Check(TableRules.Due(null, ref sent, ref ms, 0) == null, "not playing: nothing");
        Check(TableRules.Due("poker", ref sent, ref ms, 100) == "poker", "sits down: sent at once");
        Check(TableRules.Due("poker", ref sent, ref ms, 4000) == null, "not again within 5 s");
        Check(TableRules.Due("poker", ref sent, ref ms, 5100) == "poker", "again after 5 s (a lost packet heals)");
        Check(TableRules.Due(null, ref sent, ref ms, 6000) == "", "gets up: \"\" once");
        Check(TableRules.Due(null, ref sent, ref ms, 20000) == null, "and then nothing");
        sent = "dominoes"; ms = 20000;
        Check(TableRules.Due("dominoes", ref sent, ref ms, 20001) == null, "same game, just sent");
        sent = null;   // someone joined
        Check(TableRules.Due("dominoes", ref sent, ref ms, 20002) == "dominoes", "a newcomer hears it at once");
        Check(TableRules.Label("poker") == " (playing poker)" && TableRules.Label("fillet") == " (playing five finger fillet)"
              && TableRules.Label(null) == "", "F10 words");
    }

    static void TablePartners()
    {
        var t = new TablePlayers();
        t.Heard(2, "blackjack", 0);
        Check(t.Of(2, 1000) == "blackjack", "heard");
        Check(t.Of(2, TableRules.ForgetMs + 1) == null, "not heard of for 15 s: over");
        t.Heard(2, "poker", 20000);
        t.Heard(2, "", 21000);
        Check(t.Of(2, 21000) == null, "\"\": over");
        t.Heard(3, "~r~poker", 0); t.Heard(3, "Poker", 0); t.Heard(3, null, 0);
        Check(t.Of(3, 0) == null, "only the exact names (the text comes from strangers)");
        t.Heard(4, "fillet", 0); t.Heard(4, "fishing", 100);
        Check(t.Of(4, 100) == null, "another activity: not at a table any more");
        t.Heard(5, "dominoes", 0); t.Heard(6, "poker", 0);
        t.KeepOnly(new[] { (byte)6 });
        Check(t.Of(5, 0) == null && t.Of(6, 0) == "poker", "who left is forgotten");
        // The line partners see shows once: not at a table game's 5 s repeat.
        Check(t.Heard(7, "poker", 0) && !t.Heard(7, "poker", 5000) && t.Heard(7, "blackjack", 6000)
              && t.Heard(7, "", 7000) && t.Heard(7, "is fishing", 8000) && t.Heard(7, "is fishing", 9000), "news: a game, another, its end, other activities");
        Check(t.Heard(7, "poker", 10000) && t.Heard(7, "poker", 10000 + TableRules.ForgetMs + 1), "forgotten, then heard again: news");
    }

    static void TableSits()
    {
        Check(TableRules.Sits(true, true, 0f, 0.4f, false), "at a table, still, reached them: sits");
        Check(!TableRules.Sits(true, true, 0f, 5f, false), "still on its way: walks there first");
        Check(TableRules.Sits(true, true, 0.2f, 5f, true), "sitting, nudged away: stays put (no sit-stand loop)");
        Check(!TableRules.Sits(true, true, 1.4f, 0.4f, true), "they walk off: stands up");
        Check(!TableRules.Sits(false, true, 0f, 0.4f, true), "game over: stands up");
        Check(!TableRules.Sits(true, false, 0f, 0.4f, false), "mounted, in a wagon or down: never sits");
    }

    // ---- mini-games and activities ----

    static void ActivityRules()
    {
        var a = new ActivityState();
        Check(!a.Update("", 0) && a.Doing == "", "nothing to start with");
        Check(a.Update("is fishing", 1000) && a.Doing == "is fishing", "starts");
        Check(!a.Update("is fishing", 1500), "no change while it runs");
        Check(!a.Update(null, 2000) && a.Doing == "is fishing", "a script restarting between rounds isn't leaving");
        Check(!a.Update("", 1500 + ActivityState.HoldMs - 1) && a.Doing == "is fishing", "held until it's been gone a while");
        Check(a.Update("", 1500 + ActivityState.HoldMs) && a.Doing == "", "then over");
        a.Update("is fishing", 20000);
        Check(a.Update("is playing poker", 20500) && a.Doing == "is playing poker", "straight on to another");

        Check(ActivityText.Clean("is ~COLOR_RED~fishing~s~ <font>\n") == "is fishing font", "codes, markup and line breaks out");
        Check(ActivityText.Clean(new string('x', 500)).Length == ActivityText.MaxLength, "capped");
        Check(ActivityText.Clean(null) == "", "nothing");
        Check(ActivityText.Of("poker") == "is playing poker" && ActivityText.Of("fillet") == "is playing five finger fillet"
              && ActivityText.Of("is ~r~fishing") == "is fishing" && ActivityText.Of("") == "", "a table game's name (TableGames) reads as a line");
    }

    static void RodShotsLeftOut()
    {
        var rod = new RodShots();
        var s = new PlayerState { Shots = 3, Pose = PoseFlags.Aiming | PoseFlags.Crouching };
        rod.Apply(ref s, false);
        Check(s.Shots == 3 && s.Pose == (PoseFlags.Aiming | PoseFlags.Crouching), "a gun: as it is");
        s = new PlayerState { Shots = 7, Pose = PoseFlags.Aiming | PoseFlags.Crouching };
        rod.Apply(ref s, true);
        Check(s.Shots == 3 && s.Pose == PoseFlags.Crouching, "the rod: no aim, its casts aren't shots");
        s = new PlayerState { Shots = 9 };
        rod.Apply(ref s, false);
        Check(s.Shots == 5 && Shots.NewShots(3, s.Shots) == 2, "a gun again: only its own shots count");
        s = new PlayerState { Shots = 1 };   // 248 shots later, past the wrap
        rod.Apply(ref s, false);
        Check(s.Shots == (byte)(5 + 248), "wraps like the counter");
    }

    static void BrowserQuery()
    {
        var r = new Relay("pw", () => 0, null, "Arthur's game");
        var from = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5);
        Check(r.Handle(Relay.QueryMagic.Concat(new byte[20]).ToArray(), from).Count == 0, "short query ignored");
        var q = Relay.QueryMagic.Concat(new byte[Relay.QuerySize - 4]).ToArray();
        var a = r.Handle(q, from);
        Check(a.Count == 1 && a[0].to.Equals(from), "one answer, to the asker");
        var ans = a[0].data;
        Check(ans.Length <= q.Length, "answer no bigger than the question");
        Check(ans[0] == 'R' && ans[3] == 'A' && BitConverter.ToInt32(ans, 4) == Protocol.Version && ans[8] == 0 && ans[9] == 4, "header, version, 0 of 4");
        Check(System.Text.Encoding.UTF8.GetString(ans, 11, ans[10]) == "Arthur's game", "room name");
    }

    // ---- doors ----

    const uint JailDoor = 1207903970;   // joaat("DOOR_STR_JAILPOST_INT_1"): the Strawberry jail cell its town script locks

    static void DoorList()
    {
        Check(Doors.TryGet(JailDoor, out var jail), "a door the story scripts use is on the list");
        Near(jail.X, -1812.0f, "jail x", 0.06f); Near(jail.Y, -351.9f, "jail y", 0.06f); Near(jail.Z, 160.5f, "jail z", 0.06f);
        Check(!Doors.TryGet(12345, out _), "any other hash isn't");
        var raw = DoorTable.Raw;
        Check(raw.Length % 4 == 0 && raw.Length / 4 > 1500, "doors: " + raw.Length / 4);
        for (int i = 0; i < raw.Length; i += 4)
            Check(Math.Abs(raw[i + 1]) < 80000 && Math.Abs(raw[i + 2]) < 80000 && raw[i + 3] > -500 && raw[i + 3] < 5000, $"door {raw[i]} is on the map");
        var near = Doors.Near(new[] { jail }, 30f, 64);
        Check(near.Count > 1 && near[0] == JailDoor, "nearest first: " + string.Join(",", near.Take(3)));
        Check(near.Zip(near.Skip(1), (a, b) => Dist(a, jail) <= Dist(b, jail)).All(x => x), "sorted by distance");
        Check(Doors.Near(new[] { jail }, 30f, 2).Count == 2 && Doors.Near(new[] { new Vec3(0, 0, -5000) }, 30f, 9).Count == 0, "cap + range");
    }

    static float Dist(uint door, Vec3 p) { Doors.TryGet(door, out var at); return Vec3.Distance(at, p); }

    static void DoorNotices()
    {
        Doors.TryGet(JailDoor, out var jail);
        var some = Doors.Near(new[] { jail }, 30f, 3);
        var sent = some.Select((h, i) => new DoorLine { Hash = h, State = i, Ratio = -0.5f + i * 0.75f, Open = i == 1 }).ToList();
        var crate = new BreakLine { Model = 0xDEADBEEF, At = new Vec3(-1810.25f, -350.5f, 160.04f) };
        string text = Doors.Join("P", sent.Select(Doors.Line).Concat(new[] { Doors.Line(crate) }));
        var pk = Protocol.NoticePacket(new NoticeMsg { Kind = NoticeKind.Door, Text = text }, Protocol.Broadcast);
        var p = Protocol.Read(pk, pk.Length);
        var doors = new System.Collections.Generic.List<DoorLine>();
        var breaks = new System.Collections.Generic.List<BreakLine>();
        Doors.Parse(p.Notice.Text, out bool picture, doors, breaks);
        Check(picture && doors.Count == 3 && breaks.Count == 1, $"picture {picture}, {doors.Count} doors, {breaks.Count} breaks");
        for (int i = 0; i < 3; i++)
        {
            Check(doors[i].Hash == sent[i].Hash && doors[i].State == sent[i].State && doors[i].Open == sent[i].Open, "door " + i);
            Near(doors[i].Ratio, sent[i].Ratio, "ratio " + i);
        }
        Check(breaks[0].Model == 0xDEADBEEF, "break model");
        Near(breaks[0].At.X, -1810.25f, "break x", 0.06f); Near(breaks[0].At.Z, 160.04f, "break z", 0.06f);

        var many = Doors.Join("P", Enumerable.Range(0, 300).Select(i => Doors.Line(new DoorLine { Hash = JailDoor, State = 1, Ratio = -0.99f, Open = true })));
        Check(many.Length <= NoticeMsg.MaxBytes && many.Split('\n').Length > 40, "a full list fits one notice: " + many.Length);
        string longest = Doors.Line(new DoorLine { Hash = 0xFFFFFFFF, State = Doors.MaxState, Ratio = -0.99f, Open = true });
        var biggest = Doors.Join("P", Enumerable.Repeat(longest, Doors.MaxPicture));
        Check(longest.Length == 18 && biggest.Split('\n').Length == Doors.MaxPicture + 1, "the owner's biggest picture is never cut: " + biggest.Length);
        doors.Clear(); breaks.Clear();
        Doors.Parse(many, out _, doors, breaks);
        Check(doors.Count == many.Split('\n').Length - 1, "and reads back whole");

        string j = JailDoor.ToString("X");
        doors.Clear(); breaks.Clear();
        Doors.Parse(string.Join("\n", "ZZZ 1 0 0", j + " 9 0 0", j + " -1 0 0", j + " 1 NaN 0", j + " 1 5 1", "3039 1 0 0", j + " 1 0 2", j + " 1 0",
                                "B 0 1 2 3", "B 1A 1e9 0 0", "B 1A 1 2", "B 1A NaN 2 3", "B 1A 4.5 -6 7"), out picture, doors, breaks);
        Check(!picture && doors.Count == 1 && doors[0].Ratio == 1f && doors[0].Open && doors[0].State == 1, "only the good door line, ratio clamped: " + doors.Count);
        Check(breaks.Count == 1 && breaks[0].Model == 0x1A && breaks[0].At.Y == -6f, "only the good break line: " + breaks.Count);
        doors.Clear(); breaks.Clear();
        Doors.Parse(string.Join("\n", "P x", j + " 2 0 1 x", "B 1A 1 2 3 x"), out picture, doors, breaks);
        Check(picture && doors.Count == 1 && doors[0].State == 2 && breaks.Count == 1 && breaks[0].At.Z == 3f, "fields a newer version adds are left for it");
        doors.Clear();
        Doors.Parse(string.Join("\n", Enumerable.Repeat(j + " 1 0 1", 500)), out _, doors, breaks);
        Check(doors.Count == Doors.MaxLines, "line count capped: " + doors.Count);
    }

    sealed class RelayServer : IDisposable
    {
        public int Port;
        bool _builtIn;
        Process _p;
        RelayHost _host;
        public static RelayServer Start(bool builtIn, int port = 0)
        {
            if (port == 0) port = new Random().Next(28000, 29000);
            var s = new RelayServer { Port = port, _builtIn = builtIn };
            if (builtIn) { s._host = new RelayHost(port, "pw", _ => { }); s.Port = s._host.Port; return s; }
            var psi = new ProcessStartInfo("python3", $"-u server/relay.py --port {port} --password pw")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            s._p = Process.Start(psi);
            s._p.StandardOutput.ReadLine();   // "relay on udp/..."
            return s;
        }
        // The same port with nothing remembered: what a server restart (or a dead link) looks like to a player.
        public void Restart() { Dispose(); var n = Start(_builtIn, Port); _host = n._host; _p = n._p; }
        public void Dispose() { _host?.Dispose(); try { _p?.Kill(); _p?.WaitForExit(); } catch (Exception) { } }
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

    static void ServerKeepalive(RelayServer server)
    {
        var a = Link(server.Port, "Alone");
        string lost = null; int joined = 0;
        a.Disconnected += r => lost = r;
        a.Joined += () => joined++;
        Check(PumpUntil(() => a.State == LinkState.Connected, a), "joined");
        var until = Clock.ElapsedMilliseconds + 7000;
        while (Clock.ElapsedMilliseconds < until) { a.Poll(Clock.ElapsedMilliseconds); Thread.Sleep(20); }
        Check(lost == null && a.State == LinkState.Connected, "still connected after 7 s alone: " + lost);
        // The server forgot us (a restart, or a link dead for 10 s): it challenges our keepalive; we
        // say so, clear the session and join again instead of pretending nothing happened.
        server.Restart();
        Check(PumpUntil(() => joined == 2, a), $"joined again: lost '{lost}', state {a.State}");
        Check(lost == "the server forgot us" && a.State == LinkState.Connected && a.MyId == 1 && a.IAmHost, $"honest rejoin: '{lost}', id {a.MyId}");
        Bye(a);
    }

    // A game in the pause menu: no Poll for 12 s, while its partner keeps sending positions
    // and a third player joins. The link's own thread keeps the seat, and nothing is replayed.
    static void ServerPause(int port)
    {
        var a = Link(port, "Host"); var b = Link(port, "Friend");
        string aLost = null; int aJoined = 0, aStates = 0;
        a.Disconnected += r => aLost = r;
        a.Joined += () => aJoined++;
        a.StateReceived += (from, s, t) => aStates++;
        Check(PumpUntil(() => a.State == LinkState.Connected && b.State == LinkState.Connected && a.Roster.Count == 2 && b.Roster.Count == 2, a, b), "both in");
        Check(a.IAmHost, "a hosts");
        byte aId = a.MyId;
        var c = Link(port, "Third");
        var until = Clock.ElapsedMilliseconds + 12000;
        while (Clock.ElapsedMilliseconds < until)
        {
            b.Poll(Clock.ElapsedMilliseconds); c.Poll(Clock.ElapsedMilliseconds);
            b.SendState(At(1, 5));
            Thread.Sleep(20);
        }
        Check(b.Roster.Count == 3 && b.HostId == aId && c.State == LinkState.Connected, $"the server kept a as host through the stall: {b.Roster.Count} in, host {b.HostId}");
        Check(PumpUntil(() => a.Roster.Count == 3, a, b, c), "back: the roster that arrived during the stall was kept: " + a.Roster.Count);
        Check(aLost == null && aJoined == 1 && a.MyId == aId && a.IAmHost, $"same seat: lost '{aLost}', joined {aJoined} times, id {a.MyId}, host {a.IAmHost}");
        Check(aStates < 100, "the positions sent during the stall were dropped at the socket: " + aStates);
        Bye(a, b, c);
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

    static void TrainMatchPicks()
    {
        // Engine 10 or 11, tender 20, then cars.
        var configs = new System.Collections.Generic.Dictionary<uint, System.Collections.Generic.List<uint>>
        {
            { 1, new System.Collections.Generic.List<uint> { 10, 20, 30, 30, 40 } },
            { 2, new System.Collections.Generic.List<uint> { 10, 20, 50 } },
            { 3, new System.Collections.Generic.List<uint> { 11, 20, 30 } },
        };
        uint Pick(uint[] withEngine, out int alike) =>
            TrainMatch.Closest(configs, new System.Collections.Generic.IList<uint>[] { withEngine.Skip(1).ToList(), withEngine }, out alike);

        Check(Pick(new uint[] { 10, 20, 50 }, out int a) == 2 && a == TrainMatch.Exact, "exact, counted with the engine");
        Check(Pick(new uint[] { 99, 10, 20, 50 }, out a) == 2 && a == TrainMatch.Exact, "exact, counted without the engine");
        Check(Pick(new uint[] { 10, 20, 30 }, out a) == 1 && a == 3, "last cars uncoupled: the config it came from, not engine 11's");
        Check(Pick(new uint[] { 10, 20, 60 }, out a) == 2 && a == 2, "equally alike: the nearer length");
        Check(Pick(new uint[] { 30, 40 }, out a) == 0 && a == 0, "a loose carriage: no train");
        Check(Pick(new uint[] { 10 }, out a) == 2 && a == 1, "a lone engine: the shortest train with it");
        Check(TrainMatch.Closest(configs, new System.Collections.Generic.IList<uint>[0], out a) == 0, "nothing seen");
    }

    static void FollowPromptRules()
    {
        var p = new FollowPrompt();
        Check(!p.Tick(false, 1000) && !p.Asking, "nothing wanted: nothing asked");
        Check(!p.Tick(true, 1000) && p.Asking && p.SecondsLeft(1000) == 30, "wanted: the question starts at 30 s");
        Check(!p.Tick(true, 1000 + FollowPrompt.AskMs - 1) && p.SecondsLeft(1000 + FollowPrompt.AskMs - 1) == 1, "still asking at 29.999 s");
        Check(p.Tick(true, 1000 + FollowPrompt.AskMs) && !p.Asking, "30 s with no answer: go");
        Check(!p.Tick(true, 50000) && p.Asking, "wanted again later: asked again");
        Check(!p.Tick(false, 51000) && !p.Asking, "no longer wanted: the question is dropped");
        Check(!p.Decline(52000), "N with no question: nothing");
        p.Tick(true, 60000);
        Check(p.Decline(61000) && !p.Asking && p.Staying(61000), "N: staying");
        Check(!p.Tick(true, 61000 + FollowPrompt.StayMs - 1) && !p.Asking, "not asked again for 5 minutes, even if far all along");
        Check(!p.Tick(true, 61000 + FollowPrompt.StayMs) && p.Asking, "after 5 minutes: asked again");
        var q = new FollowPrompt();
        q.Tick(true, 100);
        Check(q.Accept() && !q.Asking && !q.Staying(100), "Y: the question is answered, no hold");
        Check(!q.Accept(), "Y with no question: nothing");
    }

    static void OnlineRoomIsNotExtra()
    {
        var json = System.Text.Encoding.UTF8.GetBytes("{\"rooms\":[{\"name\":\"Anu\",\"ip\":\"10.0.0.9\",\"port\":27460,\"players\":1,\"max\":4,\"version\":13}]}");
        var rooms = RoomList.ReadRooms(new System.IO.MemoryStream(json));
        Check(rooms.Count == 1 && rooms[0].Name == "Anu", "parsed");
        Check(rooms[0].Extra == -1 && !rooms[0].HostHere && !rooms[0].Party && !rooms[0].Update, "a plain room: Enter asks for its password");
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

    static void LookStability()
    {
        MetaTag T(uint d) => new MetaTag { Drawable = d, Albedo = 1, Normal = 2, Material = 3, Palette = 4, Tint0 = 5 };
        var a = new Appearance { Model = 7, Tags = new System.Collections.Generic.List<MetaTag> { T(10), T(20), T(30) } };
        var b = new Appearance { Model = 7, Tags = new System.Collections.Generic.List<MetaTag> { T(30), T(10), T(20) } };
        var c = new Appearance { Model = 7, Tags = new System.Collections.Generic.List<MetaTag> { T(10), T(20), T(31) } };
        Check(a.Signature() == b.Signature(), "order doesn't matter");
        Check(a.Signature() != c.Signature(), "a different piece does");
        var s = new LookSender();
        Check(s.ShouldSend(100, 0, out bool ch) && ch, "the first look goes out");
        Check(!s.ShouldSend(100, 2000, out _), "the same look doesn't repeat");
        Check(!s.ShouldSend(101, 4000, out _), "a new reading waits");
        Check(!s.ShouldSend(102, 6000, out _) && !s.ShouldSend(103, 8000, out _) && !s.ShouldSend(104, 10000, out _), "a reading that changes every time is never sent");
        Check(s.ShouldSend(105, 16000, out ch) && !ch, "the 15 s repeat still goes out, as whatever the current reading is");
        Check(!s.ShouldSend(200, 18000, out _) && s.ShouldSend(200, 20000, out ch) && ch, "a look that holds for two readings is sent as a change");
        s.Resend();
        Check(s.ShouldSend(200, 22000, out ch) && !ch, "a newcomer gets it again");
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

    // ---- law (crimes count for whoever did them) ----

    // The game's hash of a name (Jenkins one-at-a-time over the lower-case name).
    static uint Joaat(string s)
    {
        uint h = 0;
        foreach (char c in s.ToLowerInvariant()) { h += c; h += h << 10; h ^= h >> 6; }
        h += h << 3; h ^= h >> 11; h += h << 15;
        return h;
    }

    static void LawHashes()
    {
        Check(LawRules.Murder == Joaat("CRIME_MURDER") && LawRules.MurderLaw == Joaat("CRIME_MURDER_LAW")
              && LawRules.AssaultLaw == Joaat("CRIME_ASSAULT_LAW"), "passed-on crimes");
        var held = new[] { "ASSAULT", "MURDER", "ASSAULT_LAW", "MURDER_LAW", "UNARMED_ASSAULT", "ASSAULT_HORSE", "MURDER_HORSE",
                           "ASSAULT_ANIMAL", "MURDER_ANIMAL", "ASSAULT_LIVESTOCK", "MURDER_LIVESTOCK" };
        Check(LawRules.FromHits.SequenceEqual(held.Select(n => Joaat("CRIME_" + n))), "held-back crimes");
        Check(LawRules.RelCop == Joaat("rel_cop") && LawRules.RelGuamaLaw == Joaat("rel_guama_law"), "lawmen's groups");
    }

    static void LawCharges()
    {
        // human, lawman, killed, provoked (it wasn't fighting us), in a story mission
        Check(LawRules.For(true, false, true, true, false) == LawRules.Murder, "a calm townsperson shot dead: murder");
        Check(LawRules.For(true, false, true, false, false) == 0, "someone who fought us: self-defence");
        Check(LawRules.For(true, false, false, true, false) == 0, "only wounded: let off");
        Check(LawRules.For(true, true, true, false, false) == LawRules.MurderLaw, "a lawman killed in a shoot-out still counts");
        Check(LawRules.For(true, true, false, true, false) == LawRules.AssaultLaw, "a calm lawman shot");
        Check(LawRules.For(true, true, false, false, false) == 0, "a lawman already fighting us, wounded");
        Check(LawRules.For(false, false, true, true, false) == 0, "animals and horses: nothing");
        Check(LawRules.For(true, true, true, true, true) == 0, "story missions keep their own law");
        Check(LawRules.Name(LawRules.Murder) != null && LawRules.Name(Joaat("CRIME_ASSAULT")) == null, "only the passed-on crimes have names");
    }

    static void LawNoticeText()
    {
        string t = LawNotice.Format(LawRules.MurderLaw, new Vec3(-283.4f, 790.6f, 118f));
        Check(t == "7797FCE7 -283 791 118", "format: " + t);
        Check(LawNotice.TryParse(t, out uint crime, out var at) && crime == LawRules.MurderLaw
              && Math.Abs(at.X + 283f) < 0.01f && Math.Abs(at.Y - 791f) < 0.01f && Math.Abs(at.Z - 118f) < 0.01f, "round trip");
        foreach (var bad in new[] { null, "", "E28ECE7E 1 2", "0BADC882 1 2 3", "zz 1 2 3", "E28ECE7E NaN 2 3",
                                    "E28ECE7E 1 2 99999", "E28ECE7E Infinity 2 3", "E28ECE7E 1,5 2 3" })
            Check(!LawNotice.TryParse(bad, out _, out _), "refused: " + bad);
        Check(LawNotice.TryParse("E28ECE7E 1 2 3 x", out crime, out at) && crime == LawRules.Murder && at.Z == 3f, "a field a newer version adds is left for it");
    }

    static void LawGateCaps()
    {
        var g = new LawGate();
        for (int i = 0; i < LawGate.PerMinute; i++) Check(g.Allow(i * 100), "within the cap " + i);
        Check(!g.Allow(5000), "over the cap");
        Check(g.Allow(60000), "a minute after the first, room again");
        Check(!g.Allow(60050), "but only as the old ones age out");
    }

    // ---- spectate ----
    static void SpectateNext()
    {
        var all = new byte[] { 4, 2, 3 };
        Func<byte, bool> any = id => true;
        Check(SpectateOrder.Next(all, 0, any) == 2, "first watch: the lowest id");
        Check(SpectateOrder.Next(all, 2, any) == 3, "then the next one up");
        Check(SpectateOrder.Next(all, 4, any) == 2, "round again after the highest");
        Check(SpectateOrder.Next(all, 9, any) == 2, "a partner who left: start over");
        Check(SpectateOrder.Next(all, 2, id => id != 3) == 4, "a far one (no stand-in) is skipped");
        Check(SpectateOrder.Next(all, 2, id => id == 2) == 2, "the only one in sight stays");
        Check(SpectateOrder.Next(all, 2, id => false) == 0, "nobody in sight");
        Check(SpectateOrder.Next(new byte[0], 0, any) == 0, "nobody there");
        Check(SpectateOrder.Next(new byte[] { 0, 3, 3 }, 0, any) == 3, "no id 0, no repeats");
    }
}
