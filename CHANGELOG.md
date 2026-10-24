# RDR2 Co-op - changelog

Servers and players must run the same protocol version; when it changes, update
the server's relay.py together with the mod.

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
