#!/bin/sh
# Assembles RDR2Coop/runtime: .NET 9 laid out like a real dotnet install, which is what hostfxr's
# dotnet_root expects:
#   runtime/host/fxr/<ver>/hostfxr.dll
#   runtime/shared/Microsoft.NETCore.App/<ver>/   the whole shared framework, incl. its deps.json + runtimeconfig.json
# Every file comes from one NuGet pack, Microsoft.NETCore.App.Runtime.win-x64 (it ships hostfxr and
# hostpolicy too). The version is pinned here; bump VER and nothing else (the loader finds the
# version folder at run time).
#   host/make-runtime.sh [outdir]      default outdir: dist  ->  <outdir>/RDR2Coop/runtime
set -e
cd "$(dirname "$0")"
VER=9.0.15
OUT=${1:-../dist}/RDR2Coop/runtime
PACK=${NUGET_PACKAGES:-$HOME/.nuget/packages}/microsoft.netcore.app.runtime.win-x64/$VER/runtimes/win-x64
# Not in the NuGet cache yet: RuntimePack.csproj only exists to download it (needs network once).
[ -d "$PACK" ] || dotnet restore RuntimePack.csproj -p:RuntimeVersion=$VER -nologo -v q
[ -d "$PACK" ] || { echo "runtime pack $VER not found at $PACK"; exit 1; }
FX=$OUT/shared/Microsoft.NETCore.App/$VER
rm -rf "$OUT" && mkdir -p "$OUT/host/fxr/$VER" "$FX"
cp "$PACK/native/hostfxr.dll" "$OUT/host/fxr/$VER/"
cp "$PACK/lib/net9.0/"* "$PACK/native/"* "$FX/"
rm "$FX/hostfxr.dll"   # a real install keeps it in host/fxr only
rm "$FX/createdump.exe"   # only writes crash dumps when asked to; an .exe in the zip gets Nexus uploads quarantined (.NET starts fine without it, deps.json entry or not)
cp "$PACK/../../LICENSE.TXT" "$PACK/../../THIRD-PARTY-NOTICES.TXT" "$OUT/"   # the pack's licence terms travel with the files
# hostfxr refuses the folder without the two json files (0x80008096): fail here, not in a player's game.
for f in Microsoft.NETCore.App.deps.json Microsoft.NETCore.App.runtimeconfig.json coreclr.dll hostpolicy.dll System.Private.CoreLib.dll; do
  [ -f "$FX/$f" ] || { echo "runtime pack lacks $f"; exit 1; }
done
echo "runtime $VER -> $OUT ($(du -sh "$OUT" | cut -f1), $(ls "$FX" | wc -l) files)"
