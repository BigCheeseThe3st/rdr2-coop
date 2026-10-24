# RDR2 Co-op (story mode)

Play Red Dead Redemption 2's story mode with up to 3 friends (unofficial mod,
early access). The host's world, missions and save are shared: ride together,
fight side by side, join mission parties and revive each other. Built on
ScriptHookRDR2DotNet V2, so it runs alongside other script mods such as enRage.
Nexus page: https://www.nexusmods.com/reddeadredemption2/mods/11238

## Download
From [Releases](https://github.com/BigCheeseThe3st/rdr2-coop/releases):
- **Windows:** `RDR2-Coop-Windows-<version>.zip`: the mod, the server, the
  unofficial enRage co-op patch and `RDR2CoopSetup.exe`, which installs everything
  (see README-WINDOWS.txt inside).
- **Linux:** `RDR2-Coop-Full-<version>.zip`: the mod, the server, the Linux
  installer (`python3 setup.py`) and the unofficial enRage co-op patch.
- `RDR2-Coop-<version>.zip`, `RDR2-Coop-Server-<version>.zip`,
  `RDR2-Coop-Linux-Installer-<version>.zip`: the same, as separate pieces
  (the mod and server are also on Nexus).
- Optional, host only: `RDR2-Coop-enRage-Patch-<version>.zip`, the unofficial
  enRage co-op patch (shares the host's enRage world with the party). Unpack it
  into the same folder, add your own enRage 0.9.5.1 (Nexus mod 10967) and run
  the installer. Not made or supported by enRage's author.

You also need ScriptHookRDR2 (https://www.dev-c.com/rdr2/scripthookrdr2/) and
ScriptHookRDR2DotNet V2 (https://github.com/Halen84/ScriptHookRDR2DotNet-V2/releases);
the Linux installer fetches both if they're missing.

## Install
- **Windows:** unpack the Windows zip and double-click `RDR2CoopSetup.exe` (it's
  unsigned: "More info", then "Run anyway"). Or by hand: "INSTALL" in README-PLAYERS.txt.
- **Linux (Steam + Proton):** unpack the Full zip and run `python3 setup.py` in
  it. It finds the game, sets up the script hooks, asks your name, the server,
  the room password and whether you usually host. On a brand-new install it offers
  to give the game's Proton prefix .NET 4.8 first. `python3 setup.py --uninstall`
  takes it all out again. Then set the Steam launch options to
  `WINEDLLOVERRIDES="dinput8=n,b" %command%`.

Load a story save; a subtitle says when you've joined. Settings live in
`scripts/RDR2Coop.ini`, the log in `scripts/RDR2Coop.log` in the game folder.

Everyone plays their own save, and the host's story is the one you share: the player whose
story you want to play hosts, and the others come along (their own saves don't change). A save
still in Chapter 1 keeps you in the snowy mountains with the map locked, so to join someone, be
past Chapter 1 (finish it, or load a save from the start of Chapter 2). A host in Chapter 1 is
fine: the mission party (F10 twice) brings friends to them.

## Server
The host's game runs it: `RunServer=Yes` in their RDR2Coop.ini (`src/Net/Relay.cs`,
with UPnP from `src/Net/Upnp.cs`); friends point `Server=` at the host, or find it with
the in-game server browser (F10 twice): servers on their network answer a broadcast, and
hosts with `Public=Yes` appear in the online room list (`list-server/`, a Cloudflare
Worker; `src/Net/RoomList.cs` is the game's side).

For an always-on room there's the standalone `server/relay.py` (Python 3, no
dependencies; Windows, macOS or Linux): `python3 relay.py --password YOUR-PASSWORD`
(Windows: `py relay.py ...`; `--name`, `--public` for the browser), and forward UDP
port 27460 for friends over the internet. See `server/README-SERVER.md` (Windows
steps, `setup-server.sh` service install, security).

The first player in hosts (PreferHost=Yes takes over when the host leaves); every
game runs the area around its player and shares it when players are within 150 m
(the host's game first).

## Build
`./build.sh` compiles `dist/scripts/RDR2Coop.dll` with mono's csc against the
.NET 4.8 reference assemblies, builds `build/FakePartner.exe` and runs the tests
(no game needed). `tools/make-release.sh` makes the downloads in `release/upload`
(needs the private enRage build in ~/enrage-private for the patch).
`tools/publish-nexus.py [coop|server|installer|enrage-patch] --go` uploads them to the Nexus page as new file versions with their changelog
(API key in ~/.config/rdr2-coop/nexus-api-key; without --go it only shows what it would do). Solo test: start the game as host, then
`mono build/FakePartner.exe 127.0.0.1 27450 walk|run|ride`. Insert reloads scripts in-game.

## Help, ideas, bugs
Discord: [discord.gg/NAYzjAaQDs](https://discord.gg/NAYzjAaQDs) for help (we can look at your screen together), people to play with, and news.
Questions and ideas: [Discussions](https://github.com/BigCheeseThe3st/rdr2-coop/discussions).
Bugs: the Discord's #bug-reports (each report is copied here automatically), or [Issues](https://github.com/BigCheeseThe3st/rdr2-coop/issues), with `scripts/RDR2Coop.log` from your game folder.

## Licence
MIT (see LICENSE; third-party notes in NOTICE). Building it yourself: BUILDING.md.
