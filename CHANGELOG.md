# RDR2 Co-op - changelog

Servers and players must run the same protocol version; when it changes, update
the server's relay.py together with the mod.

## 0.9.0-beta.8.4 (beta, 29 Oct 2026) - protocol 13
- Guests play in the host's story: about 8 seconds after a guest joins, the
  guest's game switches to the host's story (camp, missions, progress) with
  no Load Game, and keeps the guest's own character, money, clothes and
  horse. The host's changes follow live while you play. Your own saves are
  backed up first (RDR2Coop\backup) and guarded: anything the game saves
  while you're in the host's story is moved aside and your own save put back,
  and your own story comes back when you leave. New, so back up your saves
  (Documents\Rockstar Games\Red Dead Redemption 2\Profiles) before trying
  it. [Gameplay] HostWorld=No turns it off.
- Bubbles (new, off by default, please test): each game keeps running the
  world round its own player, and players near each other see each other's
  people, horses, wagons and trains alongside their own; nothing a guest is
  doing is wiped when the host rides up. Where two players' worlds overlap
  there's one crowd, one set of trains and one of each gang member (the
  host's). In the host's story missions guests near the host see the host's
  world only, as before. Every player needs [Gameplay] Bubbles=Yes, or the
  old shared area is used.
- Guests and horses: a horse from the host's world that a guest gets on is
  now the guest's to ride (it used to snap back to where it stood in the
  host's game), and a guest's own horse is no longer cleared away.
- Story missions know who's who: the mod reads each mission's own cast (story
  missions, stranger missions and random encounters) for its allies and
  enemies instead of guessing, and sends the mission's key people first.
- A simpler RDR2Coop.ini: it now says up front that you only need to set your
  Name and that the co-op menu (F10 twice, Games tab) joins and hosts games
  and fills in the connection settings for you. The rest is grouped
  (connection, hosting, extras) and the old [Games] section is gone. Existing
  ini files keep working as they are.
- A game's name typed into Server= (instead of the host's address) no longer
  switches co-op off: the mod looks for a game with that name and joins it with
  your Password, or tells you on screen to pick one in the Games tab.
- Story cutscenes for guests: in a mission party, when the host's game plays a
  story cutscene, each guest close by now watches the same cutscene in their
  own game instead of a black screen. The host's character is played by the
  host's stand-in and the people, horses and wagons by the copies a guest
  already sees; anyone the scene needs that the host's game doesn't share, the
  scene brings itself. It keeps time with the host's (it plays a little faster
  or slower to catch up). If a cutscene won't play in a guest's game, the
  black screen is back for that one. [Gameplay] Cutscenes=No turns it off.
- The co-op menu (F10 twice) in the game's own pause-menu style: the game's
  fonts and ink-brush panel, tabs at the top right switched with Q and E
  (Session, Games, More), the list on the left and what the chosen row does
  on the right, and the keys along the bottom. On an ultrawide screen it
  keeps to the middle instead of stretching across it. The party list hides
  while the menu is open.
- The mod's own card and dice tables, contests and scoreboard are gone from
  the menu: playing together is moving to the game's own mini-games, starting
  with its poker ("Take a seat at the host's poker table"). The partner
  actions (emote, ping, give money, watch a partner) are under Session.
- Mission recordings, to make story missions sync properly: with beta logs
  on, while a story mission runs in your game, the mod records how the
  mission's own script memory changes (which values are people, vehicles or
  map markers, who dies and who killed them, cutscenes and checkpoints) to
  scripts\RDR2Coop.missions.log, which goes up with your other beta logs.
  Game state only: nothing about you or your PC. It only reads, never writes,
  and needs the mod's own loader (the main download, not the
  ScriptHookRDR2DotNet build). [Testing] RecordMissions=No switches it off.
  Needs the new RDR2Coop.asi from the zip (the one-press update keeps the old
  loader, which can't do it: the log says so).
- NPCs a partner kills die in the host's game too, again. A partner who
  killed someone outright (fists, a finishing move, a big hit) told the host's
  game, but the host threw that message away since beta.8 (it looked like too
  much damage to be real), so the person lived on there, and on the partner's
  screen dropped, vanished and came back (the log scan: 47 times in one of Big
  Kitty's fights; "my friend kills them and they fall off the map"). Hits over
  1000 damage now count as 1000 instead of being dropped.
- In a story mission, the host's allies can't be hurt on partners' screens.
  The host's game ignores partners' hits on its allies (hurting one fails the
  mission), so on a partner's screen they only died and came back. The host
  now marks them, and partners' copies of them can't be hurt (as the host's
  own horse already was); they can be again if they turn on the players.
- A copy that dies on a partner's screen but lives in the host's game gets
  back up where it fell instead of vanishing and coming back somewhere else.
  The host's game decides who dies.
- First version (being tested): a partner plays at the game's OWN poker. The
  host sits at one of the story's poker tables the usual way (hold E). A
  partner within 10 m picks "Take a seat at <host>'s poker table" in the
  co-op menu and plays one of the card players' seats: the host's game gives
  them that seat's cards, and their moves replace the card player's (the
  game deals and plays everything else as usual). The partner sees their two
  cards, the stacks, the board and their moves; they play the seat's table
  chips, not their own money; 40 s without a move, or getting up on their
  turn, folds. Both players need this version. For testing:
  [Testing] PokerTrace=Yes records the game's poker to
  scripts\RDR2Coop.poker.log, and [Keys] PokerForceFold= makes the next card
  player to act fold.
- Loading a save no longer switches co-op off until the game restarts: the
  game restarts its scripts on a load, and the loader tried to start .NET a
  second time ("hostfxr_initialize_for_runtime_config failed: 0x1", "loader
  idle"), so F10, Insert and every co-op key stopped. It now starts .NET once
  and only starts the mod again. (In every 0.9.0 beta so far.)
- Insert no longer makes enRage warn "two copies of enRage" (pointing at
  System.Private.CoreLib.dll): the loader kept the old scripts in memory after
  unloading them, so enRage saw its own last copy. Insert now waits until
  they're gone before loading the scripts again (the loader log says whether
  they went).
- Poker at the game's own poker tables, together (first version, being
  tested): stand at one of the story's poker tables, open the co-op menu (F10
  twice) > Play cards or dice here > "Poker at this table". Partners within
  10 m join from their co-op menu as for any mod table. Everyone sits in the
  table's real chairs (the table's own card player moves out of a chair one of
  you takes), the cards on the table are the game's card models (the town's
  own deck), with face-down cards in front of everyone still in and the
  cards turned up at a showdown, and your own two cards show at the bottom of
  the screen with the game's card faces. The hand itself is the mod's poker,
  dealt by whoever opened the table (as on the mod's other tables): the
  game's own poker isn't started. The table's card players play too: the
  ones still in their chairs are your opponents (Card player 1, 2... in the
  panel; the host's game plays them), so you can play alone or with partners.
  A card player in a chair one of you takes stands up and walks off, and the
  game's own cards and chips on that table are hidden while yours are dealt.
  Money won from or lost to them is real, as at the game's own tables. Up to
  6 at a table (4 players at most).
- A new look for the co-op menu (F10 twice): a header with the version and
  who's in the session, the rows grouped under Session, Play together, More
  and Games, a bar on the chosen row, servers in columns (players, where, or
  why one can't be joined), and the keys along the bottom. Same keys and rows
  as before; the password and search screens match.
- Partners' dives show: diving into water, the sprinting dive and diving out
  of the way of a horse, wagon or train. The diver's game sends the dive and
  their stand-in makes it (a leap into the water or forward; dives away from
  the nearest wagon or horse), and a dive into water no longer makes the
  stand-in go limp mid-fall. Both players need this version. For testing, each
  log says once what the game called your first dive of each kind, and
  whether a partner's stand-in really made it.
- An enRage version the co-op patch doesn't fit is now said on screen once,
  20 s after the game starts ("enRage 0.9.5.3 isn't supported by the co-op
  patch yet (it needs enRage 0.9.5.2)"), not only in the log.

## 0.9.0-beta.8.1 (beta, 26 Oct 2026) - protocol 13
- Partners wear their own clothes again. Since 0.7.3 a partner always showed
  in the default outfit: a timer that limits how often clothes are rebuilt
  started at a value that never counted as "long enough ago", so their look
  was received but never put on (Buck's question; no "look applied" line in
  any player's log since 0.7.3).
- enRage with the co-op patch needs no installer any more: the patch comes
  with the mod (RDR2Coop\enrage-coop-patch) and the mod's loader applies it in
  memory when it starts the official enRage 0.9.5.2 from scripts\, leaving
  your enRage file as it is (and sets enRage's PauseWorld=False, as the
  installers did). A copy the installer already patched runs as it is;
  another enRage version runs without the co-op link and the log says why.
  enRage itself now runs in the mod's loader (tried in game, loads cleanly);
  its co-op link with partners isn't tested yet. Keep ScriptHookRDR2DotNet
  (ScriptHookRDRDotNet.asi) out of the game folder, or it runs enRage instead. This
  part comes with the zip, not the one-press update (a new loader and the
  RDR2Coop\enrage-coop-patch folder).
- The bundled .NET runtime no longer carries createdump.exe (it only writes
  crash dumps on request; Nexus quarantined uploads with it).
- People who stayed invisible for guests in busy towns (Valentine's
  townsfolk in players' logs) appear again: when the game refuses to make a
  copy, its model is tried again 5 s later instead of being given up for the
  session (a model slow to load gets another try a minute later). The log says
  how many people the game holds when it refuses.
- The hiding of a guest's own camp and side-quest people (beta.8) skips
  everything the mod made itself: copied wagons' drivers, carried bodies and
  pelts, partners' waiting horses and far ambushes no longer go invisible.
- Party down: everyone in a session can go down now, the host too, instead of
  dying. If the game kills a player anyway (a one-shot kill, a fall, fire) while
  a partner could still revive them, its respawn and death camera are held back
  and they're brought back where they fell, down. Only when every player is
  down does the game's own death happen (a story mission fails, the game
  respawns you). A death screen no longer counts as a cutscene, so partners
  don't get the black "in a cutscene" screen for it. If bringing a player back
  doesn't work within 5 s, the game's respawn goes ahead (nobody gets stuck).
  New in this beta and not yet tried with partners.

## 0.9.0-beta.8-r2 (Nexus repack of 0.9.0-beta.8) - protocol 13
- The same mod as 0.9.0-beta.8. The bundled .NET runtime no longer carries
  createdump.exe, a Microsoft helper that only writes crash dumps when asked
  to; the mod never uses it.

## 0.9.0-beta.8 (beta, 26 Oct 2026) - protocol 13
- A downed player no longer gets up by themselves when the host's cutscene
  ends (#10): the 10 s "fight's over" timer is paused while the host is in a
  cutscene or loading, and a downed player whose controls get handed back
  (the mission party after a cutscene) stays down. The log says when the
  timer pauses and resumes.
- An NPC a guest kills outright (a punch, a finishing move) dies in the host's
  game too, instead of getting back up (#9). The body stays down for up to 3 s
  while the host's game catches up. Knockouts aren't passed on yet; the log
  notes each one, for testing.
- Guests' shots count on mission enemies in the host's story missions (#8):
  only hits on allies (friendly NPCs and the host's own group) are dropped
  now, instead of everything not already fighting. The host's log says, once
  per NPC, whether a guest's hit was applied or ignored and why.
- While the host is in a cutscene, mission-party members see a black screen
  that says so ("<host> is in a cutscene - you'll join them when it ends"),
  drawn by the mod: the game's own fade hid the old subtitle (#23).
- Double NPCs at camp (#4, Hypnos and Shatter): a guest's game kept the people
  its own scripts made (the camp's gang, side quests' strangers) while also
  showing the host's copies of them, so everyone appeared twice from the moment
  a guest joined at camp. They're now hidden while the guest shows the host's
  world and come back when it stops ([Gameplay] OwnMissionsAsGuest=Yes keeps
  them); the log says how many.
- Beta logs that never arrived: a player who switched them on by typing
  SendLogs=Yes into RDR2Coop.ini had no install id, and the log store refused
  every upload (the store now accepts those too). The mod now makes the id
  whenever logs are on, and F10 shows whether they go out ("beta logs: on, last
  sent 3 min ago" or "on, but sending failed: ...").
- Double NPCs for guests (#4), two causes found in players' beta logs: a guest
  whose own save is already in a story mission (chapter 1 runs as one long
  mission) now gets a warning every 30 s to load a save outside a mission and
  rejoin; and every switch of a guest's game to its own world now says why in
  the log (host too far, host silent, an activity), for the short switches
  seen in Lothos's log.
- A ListServer= setting that isn't a web address (an IP address, "Yes") no
  longer breaks the online list with "Invalid URI": the mod uses its own list
  and says why in the log. ListServer is the room list, not your server.
- enRage runs on the mod's own loader (no ScriptHookRDR2DotNet needed): when
  ScriptHookRDRDotNet.asi isn't installed, the loader runs ScriptHookRDR2DotNet
  scripts from scripts\ itself, through two stand-in DLLs in RDR2Coop\ that
  point their types at the mod's API, plus Microsoft's event log library. The
  co-op link to enRage works again, since both now share one runtime. Checked
  offline: every method of enRage 0.9.5.2, official and with the co-op patch,
  compiles against it; not yet tried in game. Comes with the zip, not the
  one-press update (new files in RDR2Coop\).

## 0.9.0-beta.7 (beta, 25 Oct 2026) - protocol 13
- The server's join line names each player's mod version and build ("mod
  0.9.0-beta.7 net48"), so a session's logs show who ran what (older mods show
  as "older than 0.9.0-beta.7"). A guest whose game build doesn't match the
  script globals is told in its log that its own missions can't be blocked
  (bug #4: two sets of mission NPCs).
- A room that can't be listed online says why in plain words instead of the
  list server's raw reply, and a host whose provider shares their internet
  address (CGNAT) isn't announced at all: nobody could join it. Both point to
  the free dedicated servers in the Discord's #find-a-partner.

## 0.9.0-beta.6 (beta, 25 Oct 2026) - protocol 13
- Betas can send your co-op logs to the developer automatically, so bugs get
  looked at without a report: asked once in game (Y/N, 90 s), kept in
  [Testing] SendLogs= in RDR2Coop.ini, nothing is sent on No or without an
  answer. On Yes the end of scripts\RDR2Coop.log (and the loader's and host's
  logs) goes up a minute after the start, every 10 minutes, after an error and
  when the mod stops; addresses, server names and user folders are blanked
  first, player names stay. RDR2Coop.log says what was sent and when. The
  co-op menu has the same switch ("Beta logs: ...").
- Partners' aiming shows with whatever gun their stand-in is holding: after a
  weapon change the game can keep the previous gun in hand for a while, and
  the aim pose was refused until the two matched (xCappO's log).

## 0.9.0-beta.5 (beta, 25 Oct 2026) - protocol 13
- Partners' aiming shows again when they aim from the saddle or a wagon seat:
  the game reported their own horse or wagon as the thing aimed at, so the aim
  point sat on themselves and the other games refused it. The camera's line of
  sight is used then.
- A host in a cutscene or a loading fade keeps running the shared area for up
  to 3 minutes instead of 20 s: before, their guests' games switched to their
  own world during every mission cutscene and back after, which doubled the
  NPCs and made animals vanish and reappear (Kaz and xCappO's report).

## 0.9.0-beta.4 (beta, 25 Oct 2026) - protocol 13
- More diagnostics: when a partner's aim pose isn't shown, the log says why,
  once per weapon.

## 0.9.0-beta.3 (beta, 25 Oct 2026) - protocol 13
- Diagnostics in scripts\RDR2Coop.log for a report that a partner's aiming or
  hits don't show: what weapon each partner sends and when they aim, whether
  their stand-in got the weapon, hits sent and received, and copies that died
  in your game but not in the owner's.

## 0.9.0-beta.2 (beta, 25 Oct 2026) - protocol 13
- Everything from 0.8.0-beta.2.8 and 2.9 (joining online servers from the list
  fixed, no more T-posing partners) is in this build too.
- No .NET to install any more: the mod now loads itself (RDR2Coop.asi + an RDR2Coop
  folder with its own .NET 9) and needs only ScriptHookRDR2. On Linux/Proton the .NET
  Framework 4.8 step is gone. Unpack, run the installer (or copy the two items into
  the game folder) and play. Had the mod before? Delete scripts\RDR2Coop.dll: the
  installers do it; settings in scripts\RDR2Coop.ini are kept.
- enRage hosts keep the ScriptHookRDR2DotNet version (new optional download
  RDR2-Coop-ScriptHookDotNet-<version>.zip): the enRage link only works there for
  now. Only one version may be installed; with both, only the ScriptHookRDR2DotNet
  one runs and scripts\RDR2Coop.Core.log says so.
- The in-game update keeps working on both versions (press Insert to load it, as
  before); a change to the loader or the bundled .NET will say to download the zip
  again. The mod's logs are scripts\RDR2Coop.Host.log, RDR2Coop.Core.log and
  RDR2Coop.log.

## 0.8.0-beta.2.9 (beta, 25 Oct 2026) - protocol 13
- Partners' stand-ins no longer freeze in a T-pose (the 0.7.3 fix): with some
  outfits the game lists the clothes in a changing order, so the look was
  re-sent and the stand-in's clothes rebuilt every 2 s, which resets its pose.

## 0.8.0-beta.2.8 (beta, 25 Oct 2026) - protocol 13
- Joining a server from the online list works again: since beta.2.5, Enter on
  a listed server (dedicated ones included) opened the scoreboard instead of
  asking for the password. Servers found on your own network were fine.

## 0.8.0-beta.2.7 (beta, 25 Oct 2026) - protocol 13
- A guest's own story missions no longer start: standing at a mission marker as
  a guest (which the mission party does to you) used to start your own copy of
  the host's mission on top of theirs, so you saw two of every mission NPC.
  Only the host's story is the shared one; [Gameplay] OwnMissionsAsGuest=Yes
  brings a guest's own missions back.
- The mission party is now really on by default: the host's game starts it as
  soon as a second player is on the server, so nobody has to start one for a
  mission or a legendary animal (beta.2.6 still needed the host to start it, or
  a cutscene). The co-op menu row is now a switch: "Mission party: on (switch
  off)" / "off (switch on)". Money the host makes is shared with members only
  during a story mission (free-roam cash stays the host's).

## 0.8.0-beta.2.6 (beta, 25 Oct 2026) - protocol 13
- Mission party by default: when the host starts a mission (or a party from the
  co-op menu) everyone is in, no Y to press. Members close to the host follow
  as before (faded out for the host's cutscenes, put next to the host after).
  A member far from the host is asked before being moved: "going to <host> in
  30 s", N stays put for 5 minutes, Y goes at once, no answer goes after 30 s.
- You share the host's world (their people and animals) from 200 m instead of
  150 m, and keep it until 300 m instead of 250 m. Note the game itself only
  loads the world a few hundred metres around each player, so sharing can't
  reach much further than this: stay together for shared encounters.

## 0.8.0-beta.2.5 (beta, 25 Oct 2026) - protocol 13
- Groundwork for playing together: the co-op menu takes rows from features, a pick-list and a
  number prompt for them, the mod's cash helper, session stats, and switches in [Games].
- The co-op menu has a "Support the mod" row: every tip goes to the Wildlife Conservation Society.
- Scoreboard (co-op menu > Scoreboard, or a key: [Keys] Scoreboard=): everyone's kills (people/animals),
  times down, revives, money won and lost at the mod's tables and contest wins, top right under
  the party list. The game that runs an area counts the kills in it, so what you shoot in a
  partner's world counts for you. [Convenience] Scoreboard=No turns it off.
- Horse races (co-op menu > Start a contest > Race): the player who starts one sets a map waypoint as the
  finish and picks whether it's on horseback only; everyone within 60 m of them is in, a 10-second countdown,
  GO, and the first to the finish (within 15 m of it) wins. The finish is on your map and marked in the world;
  the standings top right show who's in with their times and how far the rest have to go. The race ends when
  everyone's in, a minute after the first finisher, or after 10 minutes; the winner is named for 15 seconds,
  and wins count on the scoreboard. Anyone can quit a race from the co-op menu: they come off everyone's
  standings and the race goes on without them (the starter calls it off for everyone). The starter's game
  only lets in partners it sees nearby, and a contest that goes wrong is called off for everyone rather
  than getting in the way of co-op. Partners need 0.8.0-beta.2.5 too; [Games] Race=No turns it off.
- Hunting contests (co-op menu > Start a contest > Hunting): the starter picks 3, 5 or 10 minutes, everyone
  within 60 m of them is in, and after the countdown whoever gets the most points for the animals they kill wins.
  The bigger the animal, the more: 1 for small game and birds, 2 for foxes, coyotes, turkeys and birds of prey, 3
  for deer and wolves, 5 for elk, cougars, panthers, black bears and alligators, 8 for grizzlies, bison and moose,
  15 for the legendary boar, bison and bull gator and the lion. Horses, dogs, cats, farm animals and people never
  count. Each kill pops up with its points; the standings top right show the time left and everyone's points, and
  at the end the winner and the biggest animal of the hunt are named for 15 seconds. What you shoot in a partner's
  world counts for you as on the scoreboard. Partners need 0.8.0-beta.2.5 too; [Games] Hunting=No turns it off.
- Watch a partner while you're down or dead: press F9 (or F10 twice > Watch <name>) and the camera
  follows them from behind, F9 again for the next partner, Esc to stop; you're back in your own view
  when you get up or respawn. Only partners close enough to see (their character is in your world) can
  be watched; not during story cutscenes or missions. Partners don't need 0.8.0-beta.2.5 for this;
  [Convenience] Spectate=No turns it off (F9 then teleports as before).
- Pings (co-op menu > Ping where I'm looking, or a key: [Keys] Ping=): marks the spot you look at, up to
  200 m away, for everyone: a marker on their map and a tall blue beam on the spot for 12 seconds, with
  "Arthur: look here (120 m)" and a short sound. You see your own in gold. One ping every 2 seconds, none
  during a story mission or at a card table. Partners need 0.8.0-beta.2.5 too; [Convenience] Ping=No turns it off.
- Give money (co-op menu > Give money to a partner): pick who, type how many dollars ($10,000 at most, and never
  more than you have), and it goes from your cash to theirs. If their game doesn't take it within 10 seconds
  (they left, are in a story mission or at a table, or are on an older version) it comes back to you; your game
  sends it again every 2 seconds meanwhile, so a lost packet doesn't send it back. Money a partner gives the host
  of a mission party isn't shared out as mission money, and gifts don't count as winnings on the scoreboard.
  [Convenience] GiveMoney=No turns it off.
- Emotes (co-op menu > Emote, or a key: [Keys] EmoteMenu=): wave, tip your hat, point, come here, clap,
  laugh, nod yes or wave it off, and your partners see your character do it. They're the game's own
  gestures, on the upper body, so you can keep walking or riding while you do one. Not in a cutscene or
  story mission, at a card table, while down or in a wagon. Partners need 0.8.0-beta.2.5 too; [Convenience]
  EmoteMenu=No turns it off.
- Card and dice tables to play against each other: co-op menu > "Play cards or dice here", pick the game and the
  stakes ($0.50 to $25), and the table opens where you stand. Partners within 10 m get "Join <name>'s <game> table"
  in their co-op menu; up to four sit down on the ground and the hands deal themselves. To each hand you bring up to
  50 times the stakes from your own cash (short of the stakes, you sit that hand out); when it's your move a list
  of moves opens by itself (Esc hides it, the co-op menu's "Your move at the table" brings it back, and after 45
  seconds the table moves for you). What you win or lose is paid in cash when each hand ends and counts on the
  scoreboard. "Leave the table" gets you up; so does walking more than 15 m off, going down or starting a
  mission, or sitting down at one of the game's own tables. Getting up mid-hand forfeits it (you still pay what
  you'd put in, however long the rest of the hand takes); if the player who opened the table gets up, it closes
  and they forfeit too: a hand that's then decided (one player left, or the house's hand at blackjack) is paid,
  any other hand being played is off. While you sit, co-op leaves you be as at the game's own tables (no mission
  party fades, F9 waits). Everyone at the table needs 0.8.0-beta.2.5; each game has its switch in [Games], and
  HighCard=Yes there adds a sample game to try it with.
- Liar's dice, the game from the first Red Dead, at the mod's tables (co-op menu > Play cards or dice here >
  Liar's dice): 2 to 4 players, five dice each that only you can see; the panel shows how many everyone has left.
  In turn you bid how many dice on the whole table show a face ("4 fives"), each bid higher than the last (more
  dice, or the same number of a higher face); ones are wild and count for any face, except when ones are bid.
  Instead of raising you can call "Liar!" (if the bid was too high the bidder loses a die, if not you do) or
  "Spot on" (if it's exactly right everyone else loses a die, if not you do). Then everyone's dice are shown,
  fresh ones are rolled and the loser starts the next round (the caller, after a right Spot on). Everyone puts
  the stakes in at the start and the last player with dice takes the pot; getting up mid-game loses you your dice
  and your stake. Your move's list offers the sensible bids (never ones: with nothing to make up for it, a bid on
  ones is the worst at its count), Liar! and Spot on; after 45 seconds the table makes the smallest raise for
  you. Everyone at the table needs 0.8.0-beta.2.5; [Games] Dice=No turns it off.
- Shooting contests (co-op menu > Start a contest > Shooting): pick 10 or 20 bottles and 30 or 60 seconds, and
  at GO a row of bottles stands on the ground 12 to 18 m ahead of where the starter stood, in the same places for
  everyone within 60 m of them. Everyone shoots their own bottles: each game stands its own, so you only see
  yours, and your partners' characters seem to shoot at nothing (they're breaking the bottles in their game).
  The standings top right show how many each of you has broken and the time left; the most bottles wins, a tie
  goes to whoever got there first, it ends at the time or once everyone has broken all of theirs, and wins
  count on the scoreboard. A story mission or cutscene takes your bottles away (what you broke still counts).
  Partners need 0.8.0-beta.2.5 too; [Games] Shooting=No turns it off.
- Dominoes at the mod's tables (co-op menu > Play cards or dice here > Dominoes): All Fives for 2 to 4 players
  with a double-six set, 7 tiles each for two and 5 each for three or four. Match an open end; the first double
  laid is the spinner, and once both its sides have tiles its top and bottom take them too. Whenever the open
  ends add up to a multiple of 5 you score that many; with nothing to play you draw from the boneyard, and pass
  once it's empty. Playing your last tile scores the others' pips (rounded to the nearest 5); when nobody can
  play, the fewest pips scores the difference. The first to 100 wins the stakes from each of the others. Only you
  see your tiles, each move in the list says what it scores, and after 45 seconds the table plays for you.
  Getting up mid-game forfeits it (with two players the other wins at once). [Games] Dominoes=No turns it off.
- Quick-draw duels (co-op menu > Start a contest > Duel): pick a partner standing within 20 m of you. After the
  countdown you both get "Holster and face <name>" and, three to five seconds later (a different wait each time),
  "DRAW!" with a short sound; until then neither of you can aim or fire. The first to put a shot into the
  other wins: each game times its own shot from its own DRAW, so a slow connection favours nobody. Fire too soon
  and you lose; nobody hits within 30 seconds and it's a draw. "DUEL vs <name>" top right says how it's going and
  who won (and how fast), the loser is knocked off their feet, and wins count on the scoreboard. Anyone else
  nearby just watches. Both of you need 0.8.0-beta.2.5; [Games] Duel=No turns it off.
- Blackjack at the mod's tables (co-op menu > Play cards or dice here > Blackjack): the player who opens the
  table deals and is the house (a round waits while they're short of the stakes). Each round everyone else bets
  1, 2 or 5 times the stakes or sits it out, then in turn hits, stands or doubles down (twice the bet, one more
  card); the dealer draws to 17 and stands on every 17, soft ones too, and a dealer's blackjack ends the round at
  once. Blackjack pays 3 to 2, a win pays even money and a tie gives your bet back. The house wins your losses
  and pays your wins from its own cash, but it never loses more than 10 times the stakes per player in a round
  (nor more than the cash it brought): past that every win is cut by the same share, and the table's panel says
  so. No splitting or insurance in this version. Get up mid-round and your hand stands: a losing hand still pays,
  a winning one gives your bet back. If the house gets up, everyone's hand stands and the dealer's plays out,
  paid as usual. Everyone at the table needs 0.8.0-beta.2.5; [Games] Blackjack=No turns it off.
- Poker at the mod's tables: Texas hold 'em against each other, 2 to 4 players. Pick Poker under "Play cards or
  dice here": the stakes are the big blind and the small blind is half of it; the dealer button moves round the
  table each hand. Each hand you bring up to 50 big blinds of your cash, and you can't lose more than that. On your
  move: check or call, raise (the smallest raise, twice that, the pot, or all in) or fold; after 45 seconds the
  table checks for you, or folds if there's a bet to call. All-ins get side pots, so you only win what you could
  cover, and at the showdown everyone still in shows their cards and the best five of seven wins; the panel names
  the winning hand. Only you see your own cards. Cards are written as two letters, rank then suit (As is the ace of
  spades, Td the ten of diamonds). Getting up mid-hand folds your hand (what you put
  in stays in the pot). Everyone at the table needs 0.8.0-beta.2.5; [Games] Poker=No turns it off.

## 0.8.0-beta.2 (beta, 25 Oct 2026) - protocol 13
- Betas now update in-game to the next beta (F10 twice > Update), and to the
  full 0.8.0 when it's out. 0.8.0-beta.1 can't see betas, so this one is a
  one-time manual install.
- Pausing no longer breaks the session. While the pause menu (or alt-tab, or a
  loading screen) was open the game couldn't tell the server it was still
  there, so after 10 s the server dropped you: the host lost the host role for
  good and their game then showed the partner's people on top of their own ("two
  of each NPC"), with the story, clock and weather now the partner's. Now the
  mod keeps your seat for as long as your game is running, frozen or not (a
  game that has hung keeps it too, until you close it): nobody is dropped or
  demoted, your partners just see you (and your NPCs, if your game runs the
  area) stand still, and if you stay paused for more than 20 s their games run
  their own surroundings until you're back. Shots and hogties they land on
  your NPCs meanwhile count when you're back; their explosions from that time
  don't go off in your game on resume, and NPC shots at your stand-in don't
  hurt you. If the server really did lose you (a dead connection for more than
  10 s) the mod says so and rejoins cleanly, and the player set up as the host
  (PreferHost=Yes) gets the host role back when they rejoin within two minutes,
  unless the player who stood in prefers hosting too (dedicated servers need
  this version's relay.py for that). NPC copies the mod lost track of are
  removed within a second. A partner still on 0.7.x is still dropped by the
  server when they pause.
- When the player whose game runs the area gets off their horse (their own or
  any other they rode), you no longer see that horse twice (their stand-in's
  horse waiting where they got off, and a copy of the real one): the waiting
  horse stands for it (new in 0.8.0-beta.1).

## 0.8.0-beta.1 (beta, 24 Oct 2026) - protocol 13
- "No UPnP router" should be rarer: the mod now asks for the router on every
  network adapter (a VPN or virtual adapter could swallow the question before),
  asks the router directly too, and asks twice. RDR2Coop.log says what answered.
- You see what your partners carry: a body or a hogtied person over their
  shoulder, a carcass or a pelt on their shoulder, and what's on their horse,
  ridden or waiting for them (the biggest thing, when it carries several). It's
  only a copy, for show (nothing to loot or skin), and a hogtied person shows as
  a limp body. Both of you need 0.8.0; [Visuals] Carrying=No in RDR2Coop.ini
  turns it off.
- Groundwork: players' poses, shots, aim, actions, what they carry and how wet
  or dirty they are travel with their position (older versions skip it), and
  RDR2Coop.ini has a [Visuals], [Gameplay] and [Convenience] switch for each
  new part.
- Your partners' clothes get wet when they've been in the water and muddy as
  they get dirty (clean again after a bath), and their character sits on
  benches and chairs, leans on rails, sits or kneels at the campfire and lies
  on a bedroll when they do, getting up when they do. Wear= and Emotes= in
  [Visuals] turn these off, both ways.
- The host's story missions are on the guests' maps: between missions, a
  yellow marker named "<host>'s mission" shows where each story mission the
  host can start begins ([Gameplay] Markers; guests on 0.7.x don't see them).
  In a mission party, a guest who gets to one first can call the host over:
  the host sees a prompt and only moves if he presses Y ([Gameplay]
  MissionSteps). The markers inside a
  mission the host is playing can't be shared (each mission keeps them to
  itself), and guests can't talk, open or pick things up for the host. The old
  marker scan ([Testing] ObjectiveScan), which crashed the game, is gone.
- The update check offers a release to players on its beta.
- A party list at the top right of the screen: everyone in the session with
  their health, DOWN when they need reviving, how far away they are and which
  way (N, NE...), and whether they're in a cutscene or far away. It shows for
  a few seconds when you press F10 and when someone joins, leaves or goes down;
  PartyList= under [Keys] in RDR2Coop.ini sets a key that keeps it on. It
  hides during cutscenes, in the pause menu and on the map. Partners on 0.7.x
  show without their health. [Convenience] PartyList=No turns it off.
- A partner's horse waits where they leave it: when they get off, it stays
  standing there (nobody else can ride it, and it doesn't kick), and when they
  come back they get on that same horse. It goes when they ride another horse,
  after 10 minutes unridden, or when they leave or ride out of range.
  Horses=No under [Visuals] in RDR2Coop.ini turns it off.
- Partners' names show over their heads when they're on screen within 40 m,
  smaller the further away they are; not during cutscenes or while the screen
  is black. Names=No under [Visuals] turns them off.
- Your partners' characters crouch when they crouch, jump when they jump,
  climb and vault after them (or are put up beside them if they can't
  follow), swim after them instead of drowning, and go limp when they're
  knocked over or fall a long way. Partners on older versions don't send
  how they move, so theirs don't crouch or jump. [Visuals] Stance=No in
  RDR2Coop.ini turns it off.
- Shared waypoints: set a waypoint on your map and your partners see it on
  theirs as "<your name>'s waypoint" (it goes when you clear it). The host's
  waypoint is also your GPS route whenever you haven't set one of your own;
  yours always wins. Waypoint=No under [Convenience] in RDR2Coop.ini turns it off.
- Your partners aim and fire in your game: their character raises its gun to
  where they aim (locked on or free aim, standing or walking), and each shot
  they fire shows a muzzle flash with a gunshot. Their shooting hits exactly
  as before; their character in your game only shows it and fires no bullets
  of its own. Partners need 0.8.0 too; [Visuals] Aiming=No turns it off.
- Your dynamite, fire bottles and fire or dynamite arrows go off in your
  partners' games too, not only in yours (explosive rounds should as well: not
  tried yet). Where your partner's game runs the area, the blast is real there:
  its people are hurt or thrown, and they know it was you. Everywhere else it
  is seen and heard. Your partners' explosions never hurt you, your horse or
  another player (the fire a fire bottle leaves still burns, like any fire), and
  in a story mission they don't hurt your allies either. Explosions=No in
  RDR2Coop.ini [Gameplay] turns it off.
- Crimes count for whoever did them ([Gameplay] Law): shoot someone in a
  partner's world and their game doesn't take the blame. If a witness saw you
  kill someone, or shoot or kill a lawman, your own game gives you the wanted
  level and bounty for it, in that region, and the lawmen there who saw it
  come after you. Wounding townsfolk, animals and horses don't count yet, and
  story missions keep their own law.
- Trains: every kind of train in the game now shows up for your partners. The
  mod knew 12 of the game's 85, so handcarts, the Saint Denis trolleys, mine
  carts and the story's own trains (Leviticus Cornwall's, for one) were left
  out; a train that matches none (cars uncoupled) shows as the nearest kind.
  A partner riding in an engine or a handcart sits in it in your game instead
  of running alongside, and if you take the driving seat of a train in a
  partner's world, their train keeps your speed, so everyone sees you drive it
  (not during their story mission; they need 0.8.0 too, or your train and
  theirs part ways). [Gameplay] Trains=No turns this off.
- Text chat: press T, type a message (up to 200 letters) and press Enter, and
  everyone in the session sees it with your name, on the left of the screen,
  for 10 seconds (partners need 0.8.0 too: 0.7.x doesn't show it). One message
  a second; it doesn't open during cutscenes. T is free in story mode
  ([Keys] Chat= picks another key, [Convenience] Chat=No
  turns chat off). Chat is written to RDR2Coop.log too, so it is in the logs
  you send with a bug report.
- Ride double: next to a partner on their horse, press F (the mount button) to sit
  behind them, and dismount to get off; you're also put down when they get off or
  leave. They see you sitting behind them (partners on older versions see you on
  foot beside their horse). RideDouble=No under [Convenience] turns it off;
  RideDouble= under [Keys] gives it a key of its own.
- Doors match where you share a partner's world: when their game locks a door,
  unlocks it or holds it open (a shop shut for the night, a mission door), yours
  does the same, and goes back to how your own game had it once you leave (it
  never locks one as you go). Open a door there, or kick one in, and it opens in
  their game too, except a locked door in their story mission: that one is theirs
  to open. A door never locks you inside a building: it locks once you're out.
  Crates, fences, tables and other things smashed there break in the other
  players' games too. [Gameplay] Doors=No in RDR2Coop.ini turns it off.
- Poker, blackjack, dominoes and five finger fillet work in co-op, in town and
  in camp: while you play, co-op leaves you be. The mission party doesn't fade
  you or bring you over to the host, you don't go down, F9 and the Y/N questions
  wait, the host's enRage sends nobody after you, the table's players stay put,
  and co-op's messages wait until you get up (keys you press still answer). The
  games' own fades no longer fade your partners, and the host's card winnings
  aren't shared as mission money. Partners see you sitting at the table, and F10
  shows "(playing poker)" by your name. MiniGames= in [Gameplay] turns it off.
- Mini-games and activities ([Gameplay] MiniGames): while you fish, play five
  finger fillet (or poker, blackjack, dominoes), do the epilogue chores (milking,
  mucking out, building), take a hotel bath, sit in the barber's chair or watch a
  show, co-op leaves your game alone: it runs the world around you itself (your
  fish and your opponents stay, nobody is cleared away), the mission party
  doesn't fade you out or bring you back to the host, and the host's clock waits
  until you're done (mini-games slow the clock down). Your partners get a line
  saying what you're doing ("Arthur is fishing"). A partner holding a fishing
  rod shows it, but readying a cast doesn't count as aiming and a cast isn't a
  shot. Arm wrestling and horseshoes aren't in story mode.
- The co-op menu's server list has a search: press F, type part of a name and
  Enter, and only matching games show (empty shows them all again).

## 0.7.2 (24 Oct 2026) - protocol 13
- The update check works: it couldn't reach GitHub ("Could not create SSL/TLS
  secure channel" in RDR2Coop.log), because the game's .NET only offers old
  TLS versions (seen under Proton, and possible on Windows); the mod now
  turns on TLS 1.2. 0.7.0 and 0.7.1 can't fetch this update themselves:
  install it the usual way once.
- The update puts the new mod where the old one is (it looked for a scripts
  folder inside the scripts folder: "Could not find a part of the path").

## 0.7.1 (24 Oct 2026) - protocol 13
- Your partners now see the weapon in your hand: draw a revolver, rifle, bow
  or lasso and it shows on your character in their game. Your shooting works
  exactly as before. Players on 0.7.0 or older don't send it, so they still
  look empty-handed.

## 0.7.0 (24 Oct 2026) - protocol 13
- Plays with 0.6.3 to 0.6.6 (same protocol); the time, weather, horse and
  map-marker parts need both players on 0.7.0.
- The mod tells you when a newer version is out, and F10 twice > "Update to x"
  installs it: it downloads that release's mod file, checks it is signed with
  the mod's release key (anything else is refused), and puts it in place;
  Insert or a restart loads it, your settings stay. UpdateCheck=No in
  RDR2Coop.ini turns it off. This version itself is installed the usual way.
- The map shows every partner wherever they are: a marker with their name
  follows them across the map (it was only there within 400 m, where their
  character is in your game).
- Time of day follows the host's game, and so does the weather while you're in
  the host's area (off on your own you keep your region's weather).
- Partners see your own horse as it is: breed, coat, saddle and tack.
- TraceJoins also logs around story cutscenes starting and ending.
- Tested 24 Oct with a fake partner through the prologue and Chapter 2: mission
  party with a member through a cutscene, host switching both ways mid-mission,
  horse look, clock and weather. The mission-marker scan stays off: it crashed
  again when a mission started (markers from the story data are next).

## 0.6.6 (24 Oct 2026) - protocol 13
- Fixed "System.OverflowException: Arithmetic operation resulted in an overflow
  at RDR2.Game.get_IsCutsceneActive()" every tick on Windows (it could work
  once, then fail on the next launch). That ScriptHookRDR2DotNet 2.2.0 check is
  broken (it reads the address of the game's cutscene slot as if it were the
  cutscene); the mod now only uses its own reading of it, which was already
  there. The build refuses the broken check from now on.

## 0.6.5 (24 Oct 2026) - protocol 13
- Works with ScriptHookRDR2DotNet builds made after its 2.2.0 release (they
  still call themselves 2.2.0, but dropped Global.GetInt): the mod was aborted
  on its first tick with "Method not found: 'Int32 RDR2.Global.GetInt()'".
  Every release is now checked against that later version too
  (tools/check-shdn-newer.sh).

## 0.6.4 (24 Oct 2026) - protocol 13
- An error inside the mod no longer gets it stopped by ScriptHookRDR2DotNet
  (which left F10 doing nothing): it is written to scripts/RDR2Coop.log, shown
  on screen, and the mod carries on. If the mod can't start at all, F10 says
  why. The log's first line names the ScriptHookRDR2DotNet version and Windows
  version, so a report says what the player runs.
- README: "If F10 does nothing" (laptop Fn key, the right ScriptHookRDR2DotNet,
  "Aborted script" after Insert or quitting is normal, which logs to send).

## 0.6.3 (23 Oct 2026) - protocol 13
- F10 twice is now the co-op menu: start or end a mission party (host), "Host
  your own game here" after joining someone else's room, and the server
  browser. The mission party is no longer on F8, which is ScriptHookRDR2DotNet's
  console key (MissionParty= in the ini sets a shortcut if you want one).
- Your own game no longer shows in your online list (joining it switched your
  own server off).
- Joining someone else's room keeps your own room's password (HostPassword=):
  "Host your own game here" then only needs Enter (or type a new password).
- The co-op menu, the enRage nemesis panel and name tags no longer flicker
  (the text is kept in memory until the game has drawn it).
- Game names from the online list can't use the game's text formatting codes.
- The browser always searches your network on the standard port (27460).
- relay.py --public works (Cloudflare refused Python's default user agent).

## 0.6.2 (23 Oct 2026) - protocol 13
- The server browser's password box says what it's for: "Room password for
  '<game>'", with Enter to join and Esc to go back (the game's keyboard can't
  show a title of its own).

## 0.6.1 (23 Oct 2026) - protocol 13
- The online room list is live: the server browser (F10 twice) now also shows
  games whose host set Public=Yes, and the installers ask hosts whether to list
  theirs. ListServer=off keeps the browser to your home network.
- relay.py --public lists a standalone server there too.
- The Windows and Linux installer downloads on GitHub include the enRage co-op
  patch (offered only to a host who has enRage).

## 0.6.0 (23 Oct 2026) - protocol 13
- Server browser: F10 twice in game lists co-op games on your home network (and,
  once the online room list is live, games whose host chose Public=Yes). Pick one, Enter, type its password (the
  game's own keyboard) and you're in; the choice is saved for next time.
  Joining from the browser leaves your current game and stops your own server.
- Hosts: RoomName= names your game in the browser. Public=Yes will list it
  online once the online room list is live (ListServer=).
- relay.py: --name for the browser, --public --list-server URL to list it online.
- Still plays with 0.4.x/0.5.0 and their servers (the browser just won't find
  servers older than 0.6.0 on your network).
- Fixed: the host's game could stop all script mods (a ScriptHook "exception
  occurred" box) when a guest dropped out and joined again. The cause was the
  mission-marker scan, which checked map-marker handles the game never gave out;
  it's now off (ObjectiveScan=Yes under [Testing] turns it back on), so guests
  don't see the host's mission markers for now.
- Log lines from the built-in server no longer get lost now and then.
- TraceJoins=Yes under [Testing] logs each step of the mod for 20 s after anyone
  joins or leaves (for finding crashes like that one).

## 0.5.0 (23 Oct 2026) - protocol 13
- No separate server needed: RunServer=Yes in the host's RDR2Coop.ini runs the
  co-op server inside their game. It asks the router to open the port (UPnP,
  UPnP=No to skip) and shows the address to give friends (F10 too), or says
  what to do when the router doesn't answer or the internet address is shared
  (CGNAT). If the host's game closes, the session ends until it's back.
- The installers ask whether to run the server in your game; the Windows one
  also lets RDR2 through Windows Firewall on the co-op port.
- Upgrading an older RDR2Coop.ini puts new settings in its [Coop] section.
- The separate server (relay.py) still works the same, for always-on servers;
  0.5.0 plays with 0.4.x and its server.

## 0.4.2 (23 Oct 2026) - protocol 13
- The server is its own small download ("Server"), separate from the Linux
  installer, with steps for Windows, macOS and Linux.
- The readme covers installing by hand on Linux too (protontricks for .NET 4.8),
  so the installer download is optional everywhere.
- No changes to how the mod plays; 0.4.x versions play together.

## 0.4.1 (23 Oct 2026) - protocol 13
- Downloads split: the main file is now just the mod (RDR2Coop.dll, settings,
  readme). The Linux installer and the server kit are a separate optional
  download, and ScriptHookRDR2DotNet is no longer bundled (get it from its
  official release; the Linux installer still fetches it for you).
- No changes to how the mod plays; 0.4.0 and 0.4.1 play together.

## 0.4.0 (23 Oct 2026) - protocol 13
- NPCs and horses look exactly as in the area owner's game: full clothes,
  hats, faces, and a horse's saddle and tack (sent once per NPC, again only
  when it changes).
- Hits carry the shooter's weapon, so the host's enRage counts a partner's
  non-fatal shots, punches and knife wounds as grudges, not only kills.
- Name tags over the host's enRage nemeses (with rank) and people met by
  name, within 30 m, on partners' screens.
- Server: update relay.py (protocol 13).

## 0.3.0 (23 Oct 2026) - protocol 12
- enRage co-op patch support: when the host's enRage sends a nemesis after you
  while you're far from the host, he and his crew ride up behind you in your
  own game, and how it ends (killed, slipped away, got the better of you)
  goes back to the host's enRage. Experimental.

## 0.2.0 (23 Oct 2026) - protocol 12
- enRage co-op patch support: the host's enRage news (deaths, world events)
  reaches every partner, and a grudge notice reaches the partner who caused it.
- Guests: F11 lists the nemeses in the host's enRage world who hold a grudge
  against you (needs the enRage co-op patch 0.2.0 on the host).
- Shares each partner's name with the enRage co-op patch (per-player grudges).
- Server: routes the new Notice message. Update relay.py (protocol 12).
- The Windows installer exe is no longer in the download (Nexus quarantines
  executables): Windows players install by hand (README-PLAYERS.txt).

## 0.1.0 (23 Oct 2026) - protocol 11
First release.
- Story-mode co-op for up to 4 players; the host's world, missions and save are shared.
- Shared NPCs, animals, wagons, coaches and trains; partners' shots count in the host's world.
- Mission party (F8) with cutscene follow, mission map markers, shared mission money.
- Downed instead of killed in a partner's area; revive (F7).
- Outfits and looks shared.
- Installers for Windows (RDR2CoopSetup.exe) and Linux (setup.py); self-hosted server.
