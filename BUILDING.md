# Building RDR2 Co-op from source

Everything compiled in the download comes from this repository:

| File in the download | Source | Built by |
|---|---|---|
| `scripts/RDR2Coop.dll` (the mod) | `src/Net`, `src/Mod` | `src/RDR2Coop.csproj` |
| `setup.py`, `server/relay.py`, `server/setup-server.sh` (installer download) | as is (Python / shell, not compiled) | - |

Not in the download (third-party, players get them from their authors):
- ScriptHookRDR2DotNet V2 2.2.0 by crosire and Halen84 (zlib licence),
  https://github.com/Halen84/ScriptHookRDR2DotNet-V2/releases/tag/2.2.0
- ScriptHookRDR2 by Alexander Blade, https://www.dev-c.com/rdr2/scripthookrdr2/
The Linux installer fetches both if missing and checks their SHA-256.

## With the .NET SDK (Windows or Linux)

Needs the .NET SDK 6 or newer (it fetches the .NET Framework 4.8 reference
assemblies itself).

1. Put `ScriptHookRDRNetAPI.dll` from the ScriptHookRDR2DotNet V2 2.2.0 release in `lib/`.
2. Build:
   ```
   dotnet build src/RDR2Coop.csproj -c Release
   ```
   Output: `src/bin/Release/net48/RDR2Coop.dll`.

## With Mono (Linux, the way releases are made)

```
./build.sh
```
Downloads `lib/ScriptHookRDRNetAPI.dll` if missing, builds `dist/scripts/RDR2Coop.dll`
against the .NET Framework 4.8 reference assemblies, and runs the network tests.
`tools/make-release.sh` builds the download zips, and for the in-game update (src/Mod/Updater.cs)
signs `RDR2Coop.dll` with the release key (`~/.config/rdr2-coop/release-signing.pem`, or
`RELEASE_KEY=`) and writes `latest.txt`. Put `RDR2Coop.dll`, `RDR2Coop.dll.sig` and `latest.txt`
on every GitHub release with the zips: players' mods find the newest version through
`releases/latest/download/latest.txt`. Keep a backup of the key: without it no release can be
signed, and players would have to update by hand to a mod built with a new key.

(`tools/WinSetup` holds a Windows installer that isn't shipped:
`dotnet build tools/WinSetup/RDR2CoopSetup.csproj -c Release`.)

## Tests

- Mod and network protocol: `./build.sh` runs `build/NetTests.exe` (Mono).
- Server: `cd server && python3 test_relay.py`.
