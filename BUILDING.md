# Building RDR2 Co-op from source

Everything compiled in the download comes from this repository:

| File in the download | Source | Built by |
|---|---|---|
| `RDR2Coop.asi` (the loader: hosts .NET 9 inside the game, replaces ScriptHookRDR2DotNet's native part) | `host/RDR2Coop.cpp` | `host/build.sh` (zig, cross-compiled; `host/` says the version) |
| `RDR2Coop/RDR2Coop.Core.dll` (script scheduling, natives, memory, keys, log) | `api/Core` (port of ScriptHookRDR2DotNet V2's core) | `api/Core/RDR2Coop.Core.csproj` |
| `RDR2Coop/RDR2Coop.Api.dll` (the RDR2, RDR2.Native, RDR2.Math, RDR2.UI API) | `api/Api` (port of ScriptHookRDR2DotNet V2's scripting_v3) | `api/Api/RDR2Coop.Api.csproj` |
| `RDR2Coop/RDR2Coop.dll` (the mod, .NET 9) | `src/Net`, `src/Mod` | `src/RDR2Coop.csproj` (target `net9.0`) |
| `RDR2Coop/runtime/` (the .NET 9 runtime, laid out as a dotnet install) | Microsoft's runtime pack (MIT; its licence files are in there) | `host/make-runtime.sh` |
| `shdn/scripts/RDR2Coop.dll` (the mod for ScriptHookRDR2DotNet, .NET Framework 4.8; enRage hosts) | `src/Net`, `src/Mod` | `build.sh` (mono csc) / `src/RDR2Coop.csproj` (target `net48`) |
| `RDR2CoopSetup.exe` (Windows installer) | `tools/WinSetup` | `tools/WinSetup/RDR2CoopSetup.csproj` |
| `setup.py`, `server/relay.py`, `server/setup-server.sh` (installer and server downloads) | as is (Python / shell, not compiled) | - |

`api/` is a port of ScriptHookRDR2DotNet V2 by crosire and Halen84 (zlib licence:
`api/LICENSE`, `api/NOTICE`; shipped as `RDR2Coop/NOTICE.txt`). Not in the download
(third-party, players get them from their authors):
- ScriptHookRDR2 by Alexander Blade, https://www.dev-c.com/rdr2/scripthookrdr2/
- ScriptHookRDR2DotNet V2 2.2.0 (only the enRage version of the mod needs it),
  https://github.com/Halen84/ScriptHookRDR2DotNet-V2/releases/tag/2.2.0
The installers fetch them if missing and check their SHA-256.

## Build (Linux, the way releases are made)

Needs the .NET 9 SDK, zig (the version named in `host/`; it cross-compiles the
loader for Windows) and mono (for the .NET Framework 4.8 build and its tests).

```
./build.sh
```
Builds both versions of the mod, the loader and the runtime folder into `dist/`,
laid out as the game folder: `dist/RDR2Coop.asi`, `dist/RDR2Coop/`,
`dist/scripts/RDR2Coop.ini`, `dist/shdn/scripts/RDR2Coop.dll`. It runs the tests
on the way (below). Only the mod: `dotnet build src/RDR2Coop.csproj -c Release`
(both targets; `-f net9.0` or `-f net48` for one).

## Release

`tools/make-release.sh` builds the download zips in `release/upload`, and for the
in-game update (src/Mod/Updater.cs) signs the two mod builds with the release key
(`~/.config/rdr2-coop/release-signing.pem`, or `RELEASE_KEY=`) and writes `latest.txt`.
Put `RDR2Coop.dll` + `RDR2Coop.dll.sig` (the ScriptHookRDR2DotNet build; the name
must not change, every older install updates from it), `RDR2Coop.net9.dll` +
`RDR2Coop.net9.dll.sig` (the main build) and `latest.txt` on every GitHub release
with the zips: players' mods find the newest version through
`releases/latest/download/latest.txt`. The one-press update replaces only the mod
DLL; a release that changes the loader, Core, Api or the runtime says "download the
zip / run the installer again" in its CHANGELOG line. Keep a backup of the key:
without it no release can be signed, and players would have to update by hand to a
mod built with a new key.

`SKIP_BUILD=1 tools/make-release.sh` packs whatever `dist/` holds (to test the zips).

## Tests

`./build.sh` runs them all: `tests/NetTests` (mod and network protocol) under mono
and under dotnet, `api/CoreTests` (the script host) under dotnet, and the server's
`python3 -m unittest test_relay` in `server/`.
