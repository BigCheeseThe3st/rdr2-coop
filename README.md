# RDR2 Co-op (story mode)

Two-player co-op for Red Dead Redemption 2 story mode, built on
ScriptHookRDR2DotNet V2 so it runs alongside enRage (Nexus mod 10967).

The **host** runs the real game: story, missions, save and enRage. The
**guest** joins them; enRage must be off on the guest.

## Stages
1. **(proven in-game 21 Oct 2026 with tools/FakePartner.cs as the guest)** Each player sees the other as a ped and horse in their own
   world, with a friendly map blip; F9 teleports to your partner, F10 shows
   the link status. NPCs, missions and enRage are NOT shared yet; each world
   is still separate.
2. Guest's world mirrors the host's NPCs; the guest's shots count on the host.
3. Missions playable together (host does cutscenes and dialogue).

## Server
`server/relay.py` (Python 3, no dependencies) is the co-op server: anyone can run one for their
group. See `server/README-SERVER.md` (quick start, `setup-server.sh` service install, port forwarding).
The first player in hosts (PreferHost=Yes takes over when the host leaves); every game runs the area
around its player and shares it when players are within 150 m (the host's game first).

## Setup (each player, Manjaro + Steam/Proton)
1. Install RDR2 on Steam. Do NOT launch it yet.
2. `./prepare-prefix.sh <steam library>` builds the game's prefix: .NET Framework 4.8
   (with GE-Proton10-34) and the Rockstar Games Launcher (with Proton Experimental;
   click through its setup). Keep RDR2 on Proton Experimental in Steam.
3. Unpack the download (`rdr2-coop-<date>.tar.gz`) and run `python3 setup.py` in it. It finds
   the game, checks the prefix, asks your name, the server, the room password and whether you
   usually host, and fetches ScriptHookRDR2 + ScriptHookRDR2DotNet itself. The usual host can
   also install enRage with the unofficial co-op patch: it asks for the official enRage zip
   from Nexus and patches that (your official copy is kept). Guests' enRage is switched off.
   `python3 setup.py --uninstall` takes it all out again.
   Manual install (no installer): see README-PLAYERS.txt.
4. Steam launch options: `WINEDLLOVERRIDES="dinput8=n,b" %command%` (the installer reminds you).
5. Load a story save. A subtitle says when you've joined.

Settings live in `scripts/RDR2Coop.ini` (name, server, port, password,
PreferHost, AppearAs, [Net] timings, keys). Log: `scripts/RDR2Coop.log` in the game folder.

## Build
`./build.sh` compiles `dist/scripts/RDR2Coop.dll` with mono's csc against the
.NET 4.8 reference assemblies, builds `build/FakePartner.exe` and runs the tests
(no game needed). `tools/make-release.sh` makes the player download in `release/`
(needs the private enRage build in ~/enrage-private for the patch).
`tools/publish-nexus.py [coop|enrage-patch] --go` uploads them to the Nexus page as new file versions with their changelog
(API key in ~/.config/rdr2-coop/nexus-api-key; without --go it only shows what it would do). Solo test: start the game as host, then
`mono build/FakePartner.exe 127.0.0.1 27450 walk|run|ride`. Insert reloads scripts in-game.

## Licence
MIT (see LICENSE; third-party notes in NOTICE). Building it yourself: BUILDING.md.
