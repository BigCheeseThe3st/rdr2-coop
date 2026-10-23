# RDR2 Co-op - changelog

Servers and players must run the same protocol version; when it changes, update
the server's relay.py together with the mod.

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
