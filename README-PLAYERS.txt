RDR2 Co-op - play Red Dead Redemption 2's story with friends (unofficial mod)
==========================================================================

You need:
  - RDR2 (Steam, Rockstar Games Launcher or Epic).
  - ScriptHookRDR2 (https://www.dev-c.com/rdr2/scripthookrdr2/). Most
    script-mod users have it already; the installers set it up if not.
  - No .NET to install: the mod brings its own.
  - Nothing else: the host's game runs the co-op server (see PLAYING
    TOGETHER below). A separate server is optional.

INSTALL (Windows and Linux, 5 minutes)
  1. ScriptHookRDR2 (https://www.dev-c.com/rdr2/scripthookrdr2/): put
     dinput8.dll and ScriptHookRDR2.dll from its bin folder in the game folder
     (the one with RDR2.exe).
  2. Put RDR2Coop.asi and the RDR2Coop folder from this download in the game
     folder, and scripts\RDR2Coop.ini in the game's scripts folder (make it if
     it isn't there).
  3. Had the mod before? Delete scripts\RDR2Coop.dll (keep RDR2Coop.ini: your
     settings are in it). Only one copy of the mod may be installed.
  4. Edit scripts/RDR2Coop.ini: Name, Port, Password, and (see below) either
     RunServer=Yes + PreferHost=Yes (the host) or Server= (everyone else).
  5. Linux only (Steam + Proton):
     Steam > Red Dead Redemption 2 > Properties:
       Launch options:  WINEDLLOVERRIDES="dinput8=n,b" %command%
       Compatibility:   Proton Experimental
  Installers that do all of this and ask for the settings: on GitHub,
  https://github.com/BigCheeseThe3st/rdr2-coop/releases (Windows: double-click
  RDR2CoopSetup.exe; Linux: python3 setup.py).

PLAYING TOGETHER
  The host's game runs the server; nobody needs anything else.
  Host, in scripts/RDR2Coop.ini:
       RunServer=Yes
       PreferHost=Yes
       Password=pick-a-password      (tell your friends)
     When the game starts, the mod asks your router to open UDP port 27460
     (UPnP) and shows the address to give your friends (F10 shows it again).
     If it says your router didn't answer: turn UPnP on in the router, or
     forward UDP 27460 to your PC by hand.
     Windows: the first time, Windows asks whether RDR2 may use the network;
     the box can hide behind the game, so alt-tab and click Allow (tick
     Public too). The Windows installer sets this up for you.
  Friends: press F10 twice in game to open the co-op menu. It shows co-op
     games on your home network, and games whose host set Public=Yes online.
     Pick one with the arrow keys (F searches the list by name), press Enter
     and type its password. The choice is saved, so next time you join it
     straight away.
     Or by hand, in scripts/RDR2Coop.ini:
       Server=the address the host gives you
       Password=the host's password
     On the same home network, use the host PC's local address (192.168...).
  Listing your game online (host): Public=Yes (the installers ask), and
     RoomName= for its name. Anyone can then see it and your internet address
     in the browser; they still need your password to join. It drops off a
     few minutes after your game closes.
  If the host's game closes, the session ends for everyone until it's back.
  Nobody can host (the router won't open the port)? Start a free dedicated
     server from the Discord (see HELP below): in #find-a-partner, open
     "Dedicated servers (start one here)", press Start a dedicated server and
     pick a name and a password. Within a minute it's in the co-op menu
     (F10 twice) as "name (dedicated)". Up to 15 run at once, one per person;
     it stops after 30 minutes empty, after 12 hours, or when you press Stop.

RUNNING A SEPARATE SERVER (optional: always on, e.g. a Raspberry Pi or VPS)
  1. Unpack the "Server" download anywhere and install Python 3 (python.org;
     on Windows tick "Add python.exe to PATH"). Nothing else is needed.
  2. In a terminal in the server folder:
       Windows:        py relay.py --password YOUR-ROOM-PASSWORD
       Linux / macOS:  python3 relay.py --password YOUR-ROOM-PASSWORD
     Leave that window open while you play. It uses UDP port 27460.
     Windows: when Windows Firewall asks about Python the first time, click
     Allow (tick Public as well if friends join over the internet).
  3. Friends outside your home: forward UDP port 27460 on your router to that
     computer and give them your public IP address. On the same home network,
     use the computer's local IP.
  4. Everyone (the host too) puts the address, 27460 and the password in
     scripts/RDR2Coop.ini, with RunServer=No. Same computer as the server:
     Server=127.0.0.1
  More (running it as a Linux service, VPS, security): server/README-SERVER.md

IN GAME
  Load your story save; a subtitle says when you've joined.
  Everyone plays their own save, and the host's story is the one you share
  (as a guest your own story missions don't start; the host's do):
  the player whose story you want to play hosts, and the others come along
  (their own saves don't change). A save still in Chapter 1 keeps you in the
  snowy mountains with the map locked, so to join someone, be past Chapter 1
  (finish it, or load a save from the start of Chapter 2). A host in Chapter 1
  is fine: the mission party (F10 twice) brings friends to them.
  The mission party is on by default: as soon as someone else is on the
  server, everyone follows the host into missions. Members are faded out
  while the host is in a cutscene and put next to the host afterwards, or when
  the host gets far away. Far from the host (hunting, say) you are asked first:
  "going to <host> in 30 s" - N keeps you where you are for 5 minutes, Y goes
  at once. The host can switch the party off and on in the co-op menu.
  Guests see where the host's story missions start: yellow markers named
  "<host>'s mission". In a mission party, get to one first and the host can
  press Y to join you there.
  F10 twice opens the co-op menu: the mission party switch (host), host
  your own game again after joining someone else's, and the server browser.
  When a newer version is out, the mod says so and the menu offers "Update to
  x": it installs only releases signed by the mod (press Insert to load it).
  UpdateCheck=No in RDR2Coop.ini turns that off.
  While you fish, play a mini-game (poker, blackjack, dominoes, five finger
  fillet), take a bath, sit in the barber's chair or watch a show, co-op leaves
  your game alone until you're done (no fades or moves, your own world around
  you) and tells your partners what you're doing. MiniGames=No in RDR2Coop.ini
  turns that off.
  Insert reloads the mod, F10 shows co-op status,
  F7 revives a downed partner, F9 teleports to your partner, F11 (guests) lists the host's enRage nemeses who have a grudge against you.
  F10 also shows the party list for a few seconds (top right: everyone's
  health, who's down, how far and which way); it pops up by itself when
  someone joins, leaves or goes down. PartyList= under [Keys] in
  RDR2Coop.ini sets a key that keeps it on.
  F10 twice > Scoreboard shows the session's scoreboard (top right: everyone's
  kills, downs, revives, winnings and contest wins); pick it again to hide it.
  Scoreboard= under [Keys] in RDR2Coop.ini gives it a key.
  F10 twice > Emote: wave, tip your hat, point, come here, clap, laugh, nod
  or wave it off, and your partners see you do it (you can keep walking).
  EmoteMenu= under [Keys] in RDR2Coop.ini gives it a key.
  F10 twice > Start a contest > Race starts a horse race to your map waypoint
  (set one first), any way or on horseback only: everyone within 60 m of you
  is in after a 10-second countdown, and the standings show top right. F10
  twice > Quit the race drops you out of one and off everyone's standings
  (Call off the race, if you started it, ends it for everyone); the other
  contests below quit and call off the same way.
  F10 twice > Start a contest > Hunting: 3, 5 or 10 minutes to get the most
  points for the animals you kill (the bigger the animal, the more points).
  F10 twice > Start a contest > Shooting stands 10 or 20 bottles on the
  ground ahead of you for 30 or 60 seconds: everyone within 60 m shoots
  their own (each game has its own bottles, so partners seem to shoot at
  nothing) and whoever breaks the most wins.
  F10 twice > Start a contest > Duel challenges a partner within 20 m to a
  quick draw: face them, hands off your gun, and on "DRAW!" the first to
  hit the other wins (fire too soon and you lose).
  While you're down or dead, F9 (or F10 twice > Watch <name>) watches a
  partner from behind them; F9 again goes to the next partner, Esc stops,
  and getting up or respawning brings your own view back.
  F10 twice > Ping where I'm looking marks the spot you look at (up to 200 m)
  on your partners' maps and in their world for 12 seconds. Ping= under
  [Keys] in RDR2Coop.ini gives it a key.
  F10 twice > Give money to a partner hands them some of your cash ($10,000
  at most); if their game doesn't take it within 10 seconds you get it back.
  F10 twice > Play cards or dice here opens a table where you stand (pick the
  game and the stakes); partners within 10 m sit down with F10 twice > Join
  <name>'s <game> table. When it's your move a list of moves opens by itself (F10
  twice > Your move at the table brings it back); F10 twice > Leave the table
  gets you up, and so does walking 15 m away.
  Dominoes there is All Fives: match an open end, score whenever the ends
  add up to a multiple of 5, draw when you can't play; the first to 100
  takes the stakes from each of the others.
  Liar's dice: bid how many dice on the whole table show a face (ones are
  wild), or call Liar! or Spot on; whoever's wrong loses a die, and the last
  with dice takes the pot.
  At blackjack the player who opens the table deals and is the house: what
  you win comes from their cash and what you lose goes to it.
  Poker is Texas hold 'em: the stakes are the big blind, and cards show as two
  letters, rank then suit (As = ace of spades, Td = ten of diamonds).
  T opens the chat: type a message and press Enter, and everyone in the
  session sees it for 10 seconds (partners need 0.8.0 too; [Keys] Chat= in
  RDR2Coop.ini changes the key).
  Next to a partner on their horse, F (the mount button) sits you behind them;
  dismount to get off. RideDouble= under [Keys] gives it a key of its own.

ENRAGE (optional, host only)
  The unofficial enRage co-op patch comes with the mod, and the mod applies it
  itself, in memory, each time the game starts. Install the official enRage
  0.9.5.2 from Nexus the way its own instructions say (its files go in the
  game's scripts folder) and that's all: no installer, and your enRage file is
  never changed. scripts\RDR2Coop.Core.log says whether the patch went on.
  Another enRage version runs as normal, without the co-op link.
  Don't install ScriptHookRDR2DotNet (ScriptHookRDRDotNet.asi) for enRage:
  when it's there, it runs enRage instead and the co-op link is off.
  The old way still works for players who need ScriptHookRDR2DotNet for
  other mods: the ScriptHookRDR2DotNet version of co-op (the optional
  RDR2-Coop-ScriptHookDotNet download), set up by the installers when you say
  yes to enRage (it needs .NET Framework 4.8; on Linux prepare-prefix.sh).

UNINSTALL
  Windows: run RDR2CoopSetup.exe again and type U, or by hand delete
           RDR2Coop.asi, the RDR2Coop folder and scripts\RDR2Coop.ini (on the
           ScriptHookRDR2DotNet version: scripts\RDR2Coop.dll and the ini).
  Linux:   python3 setup.py --uninstall (also puts your official enRage back).

IF F10 DOES NOTHING
  - On a laptop F10 may be a media key: press Fn+F10, or set ShowStatus= in
    scripts/RDR2Coop.ini to another key.
  - Had the mod before? If it does nothing now, delete scripts\RDR2Coop.dll
    (the old ScriptHookRDR2DotNet copy): with both installed only that one
    runs, and scripts\RDR2Coop.Core.log says so.
  - The mod writes three logs in the game's scripts folder: RDR2Coop.Host.log
    (the loader), RDR2Coop.Core.log (the script host) and RDR2Coop.log (the
    mod). "Aborted script" there is normal after pressing Insert (that key
    reloads the mod) or quitting the game. Anything else: send us the three.
    On the ScriptHookRDR2DotNet version it's ScriptHookRDRDotNet.log in the
    game folder and scripts\RDR2Coop.log, as before.

HELP
  Discord: https://discord.gg/NAYzjAaQDs (help, screen-share fixes, people to play with)
  Questions and ideas: the Discord, https://discord.gg/NAYzjAaQDs
  Bugs: a private thread in the Discord's #bug-reports (copied to GitHub for you), or
       https://github.com/BigCheeseThe3st/rdr2-coop/issues (attach scripts/RDR2Coop.log)

Credits: ScriptHookRDR2 by Alexander Blade (not included; link above). The
mod's script API is a port of ScriptHookRDR2DotNet V2 by crosire and Halen84
(zlib licence, see RDR2Coop\NOTICE.txt), and it ships the .NET runtime
(Microsoft, MIT, see RDR2Coop\runtime\LICENSE.TXT).
The optional enRage co-op patch is unofficial and needs your own enRage
download; all credit for enRage to its author.
