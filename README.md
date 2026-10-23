# RDR2 Co-op (story mode)

Play Red Dead Redemption 2's story mode with up to 3 friends (unofficial mod,
early access). The host's world, missions and save are shared: ride together,
fight side by side, join mission parties and revive each other. Built on
ScriptHookRDR2DotNet V2, so it runs alongside other script mods such as enRage.
Nexus page: https://www.nexusmods.com/reddeadredemption2/mods/11238

## Download
From [Releases](https://github.com/BigCheeseThe3st/rdr2-coop/releases):
- `RDR2-Coop-Full-<version>.zip`: everything in one: the mod, the server and the
  Linux installer. Start here.
- `RDR2-Coop-<version>.zip`, `RDR2-Coop-Server-<version>.zip`,
  `RDR2-Coop-Linux-Installer-<version>.zip`: the same, as separate pieces
  (the mod and server are also on Nexus).

You also need ScriptHookRDR2 (https://www.dev-c.com/rdr2/scripthookrdr2/) and
ScriptHookRDR2DotNet V2 (https://github.com/Halen84/ScriptHookRDR2DotNet-V2/releases);
the Linux installer fetches both if they're missing.

## Install
- **Windows:** follow "INSTALL" in README-PLAYERS.txt (copy two files into the
  game's scripts folder and fill in RDR2Coop.ini; about 5 minutes).
- **Linux (Steam + Proton):** unpack the Full zip and run `python3 setup.py` in
  it. It finds the game, sets up the script hooks, asks your name, the server,
  the room password and whether you usually host. On a brand-new install it offers
  to give the game's Proton prefix .NET 4.8 first. `python3 setup.py --uninstall`
  takes it all out again. Then set the Steam launch options to
  `WINEDLLOVERRIDES="dinput8=n,b" %command%`.

Load a story save; a subtitle says when you've joined. Settings live in
`scripts/RDR2Coop.ini`, the log in `scripts/RDR2Coop.log` in the game folder.

## Server
Every group needs one: `server/relay.py` (Python 3, no dependencies; Windows,
macOS or Linux): `python3 relay.py --password YOUR-PASSWORD` (Windows:
`py relay.py ...`), and forward UDP port 27460 for friends over the internet.
See `server/README-SERVER.md` (Windows steps, `setup-server.sh` service install,
security). The first player in hosts (PreferHost=Yes takes over when the host
leaves); every game runs the area around its player and shares it when players
are within 150 m (the host's game first).

## Build
`./build.sh` compiles `dist/scripts/RDR2Coop.dll` with mono's csc against the
.NET 4.8 reference assemblies, builds `build/FakePartner.exe` and runs the tests
(no game needed). `tools/make-release.sh` makes the downloads in `release/upload`
(needs the private enRage build in ~/enrage-private for the patch).
`tools/publish-nexus.py [coop|server|installer|enrage-patch] --go` uploads them to the Nexus page as new file versions with their changelog
(API key in ~/.config/rdr2-coop/nexus-api-key; without --go it only shows what it would do). Solo test: start the game as host, then
`mono build/FakePartner.exe 127.0.0.1 27450 walk|run|ride`. Insert reloads scripts in-game.

## Licence
MIT (see LICENSE; third-party notes in NOTICE). Building it yourself: BUILDING.md.
