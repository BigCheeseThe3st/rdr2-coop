#!/bin/sh
# Builds the mod for both runtimes and runs the network tests under mono and dotnet:
#   dist/shdn/scripts/RDR2Coop.dll      for ScriptHookRDR2DotNet V2 (.NET Framework 4.8; mono csc, 4.8 reference assemblies)
#   dist/RDR2Coop.asi + dist/RDR2Coop/  our own loader (host/), the API port (api/), the mod and the .NET 9 runtime folder
#   dist/scripts/RDR2Coop.ini           the settings (the only thing of ours in scripts\: SHDN loads every DLL there)
# Needs mono (csc), the dotnet 9 SDK, zig (host/build.sh) and lib/ScriptHookRDRNetAPI.dll.
set -e
cd "$(dirname "$0")"
API=lib/ScriptHookRDRNetAPI.dll
if [ ! -f "$API" ]; then
  mkdir -p lib build
  curl -sL -o build/shdn.rar https://github.com/Halen84/ScriptHookRDR2DotNet-V2/releases/download/2.2.0/ScriptHookRDR2DotNet-V2.rar
  bsdtar -xf build/shdn.rar -C lib ScriptHookRDRNetAPI.dll
fi
# dist/ is the game-file part of the release: start clean so nothing stale (e.g. an old dist/scripts/RDR2Coop.dll) ships.
rm -rf dist
mkdir -p build dist/scripts dist/RDR2Coop dist/shdn/scripts
# ScriptHookRDR2DotNet 2.2.0's Game.IsCutsceneActive (and InCutscene, its alias) casts the global's
# address to int: on Windows that overflows and throws every tick. MissionProbe.InMissionScene reads it right.
if grep -n 'Game\.\(IsCutsceneActive\|InCutscene\)' src/Mod/*.cs; then echo "use MissionProbe.InMissionScene, not Game.IsCutsceneActive/InCutscene"; exit 1; fi
# The mod's version (VERSION, see CHANGELOG.md) baked in, for the log and F10.
printf 'namespace RDR2Coop { static class BuildInfo { public const string Version = "%s"; } }\n' "$(tr -d ' \n' < VERSION)" > build/BuildInfo.cs
# Compile against .NET Framework 4.8's reference assemblies, not mono's own
# mscorlib: mono has newer overloads (e.g. String.Split(char)) that the game's
# real .NET 4.8 lacks, which compile fine and then fail with MissingMethodException.
# NET48 is defined so src/ can tell the two builds apart (the SDK defines it for the csproj's net48 target).
REF=/usr/lib/mono/4.8-api
csc -nologo -warnaserror+ -target:library -langversion:7.3 -optimize+ -unsafe -nostdlib -define:NET48 \
  -r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll -r:$REF/System.Numerics.dll \
  -r:$REF/System.Xml.dll -r:$REF/System.Runtime.Serialization.dll -r:$REF/System.Windows.Forms.dll -r:$REF/System.Drawing.dll -r:"$API" \
  -out:dist/shdn/scripts/RDR2Coop.dll build/BuildInfo.cs src/Net/*.cs src/Mod/*.cs
csc -nologo -warnaserror+ -target:exe -langversion:7.3 \
  -r:System.Numerics.dll -out:build/NetTests.exe src/Net/*.cs tests/*.cs
csc -nologo -warnaserror+ -target:exe -langversion:7.3 \
  -r:System.Numerics.dll -out:build/FakePlayer.exe src/Net/*.cs tools/*.cs
# The mono tests take ~40 s (relay timing): run them while the dotnet half builds. They use no ports a build needs.
mono build/NetTests.exe > build/mono-tests.log 2>&1 & MONO=$!
# .NET 9: the API port and the mod against it (one dotnet build via the ProjectReferences), the tests, the loader + runtime.
# api/ and host/ are other packages: until they are merged, say so and build what is here.
NET9=1
if [ -f api/Core/RDR2Coop.Core.csproj ] && [ -f api/Api/RDR2Coop.Api.csproj ]; then
  dotnet build src/RDR2Coop.csproj -f net9.0 -c Release -nologo -v q
else
  echo "api/Core or api/Api missing: the .NET 9 mod build is skipped"; NET9=
fi
dotnet build tests/NetTests.csproj -c Release -nologo -v q
if [ -f api/CoreTests/CoreTests.csproj ]; then dotnet build api/CoreTests/CoreTests.csproj -c Release -nologo -v q; fi
if [ -x host/build.sh ]; then host/build.sh "$PWD/dist"; else echo "host/build.sh missing: no RDR2Coop.asi built"; fi
if [ -x host/make-runtime.sh ]; then host/make-runtime.sh "$PWD/dist"; else echo "host/make-runtime.sh missing: no dist/RDR2Coop/runtime/ built"; fi
if [ -n "$NET9" ]; then
  # Only .pdb-free DLLs and the two json files hostfxr reads go in dist/RDR2Coop/ (the mod is loaded from bytes by Core; its own deps.json is not needed).
  cp src/bin/Release/net9.0/RDR2Coop.dll src/bin/Release/net9.0/RDR2Coop.Api.dll src/bin/Release/net9.0/RDR2Coop.Core.dll dist/RDR2Coop/
  cp api/Core/bin/Release/net9.0/RDR2Coop.Core.runtimeconfig.json api/Core/bin/Release/net9.0/RDR2Coop.Core.deps.json dist/RDR2Coop/
  cat api/NOTICE api/LICENSE > dist/RDR2Coop/NOTICE.txt
  # ScriptHookRDR2DotNet scripts (enRage) on this loader: two type-forwarding stand-ins and the event log library (api/Compat/README.md).
  bash tools/gen-compat-forwards.sh --check
  dotnet build api/Compat/ScriptHookRDRNetAPI/ScriptHookRDRNetAPI.csproj -c Release -nologo -v q
  dotnet build api/Compat/System.Windows.Forms/System.Windows.Forms.csproj -c Release -nologo -v q
  cp api/Compat/ScriptHookRDRNetAPI/bin/Release/net9.0/ScriptHookRDRNetAPI.dll api/Compat/System.Windows.Forms/bin/Release/net9.0/System.Windows.Forms.dll dist/RDR2Coop/
  cp "${NUGET_PACKAGES:-$HOME/.nuget/packages}"/system.diagnostics.eventlog/9.0.0/runtimes/win/lib/net9.0/System.Diagnostics.EventLog*.dll dist/RDR2Coop/
  # enRage's saves need System.Security.Permissions (and it, System.Windows.Extensions), which .NET 9 doesn't carry.
  cp "${NUGET_PACKAGES:-$HOME/.nuget/packages}"/system.security.permissions/9.0.0/lib/net9.0/System.Security.Permissions.dll dist/RDR2Coop/
  cp "${NUGET_PACKAGES:-$HOME/.nuget/packages}"/system.windows.extensions/9.0.0/runtimes/win/lib/net9.0/System.Windows.Extensions.dll dist/RDR2Coop/
fi
wait $MONO || { cat build/mono-tests.log; exit 1; }
tail -n 1 build/mono-tests.log
# Mono and dotnet test runs stay sequential: both pick a random relay port in the same range.
dotnet tests/bin/Release/net9.0/NetTests.dll
if [ -f api/CoreTests/bin/Release/net9.0/CoreTests.dll ]; then dotnet api/CoreTests/bin/Release/net9.0/CoreTests.dll; fi
(cd server && python3 -m unittest -q test_relay)
cp RDR2Coop.ini dist/scripts/RDR2Coop.ini
echo "built dist/shdn/scripts/RDR2Coop.dll (SHDN)${NET9:+ and dist/RDR2Coop.asi + dist/RDR2Coop/ (.NET 9)}"
