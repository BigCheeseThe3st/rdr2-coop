#!/bin/sh
# Builds dist/scripts/RDR2Coop.dll for ScriptHookRDR2DotNet V2 and runs the
# network tests. Needs mono (csc) and lib/ScriptHookRDRNetAPI.dll.
set -e
cd "$(dirname "$0")"
API=lib/ScriptHookRDRNetAPI.dll
if [ ! -f "$API" ]; then
  mkdir -p lib build
  curl -sL -o build/shdn.rar https://github.com/Halen84/ScriptHookRDR2DotNet-V2/releases/download/2.2.0/ScriptHookRDR2DotNet-V2.rar
  bsdtar -xf build/shdn.rar -C lib ScriptHookRDRNetAPI.dll
fi
mkdir -p build dist/scripts
# The mod's version (VERSION, see CHANGELOG.md) baked in, for the log and F10.
printf 'namespace RDR2Coop { static class BuildInfo { public const string Version = "%s"; } }\n' "$(tr -d ' \n' < VERSION)" > build/BuildInfo.cs
# Compile against .NET Framework 4.8's reference assemblies, not mono's own
# mscorlib: mono has newer overloads (e.g. String.Split(char)) that the game's
# real .NET 4.8 lacks, which compile fine and then fail with MissingMethodException.
REF=/usr/lib/mono/4.8-api
csc -nologo -warnaserror+ -target:library -langversion:7.3 -optimize+ -unsafe -nostdlib \
  -r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll \
  -r:$REF/System.Xml.dll -r:$REF/System.Windows.Forms.dll -r:$REF/System.Drawing.dll -r:"$API" \
  -out:dist/scripts/RDR2Coop.dll build/BuildInfo.cs src/Net/*.cs src/Mod/*.cs
csc -nologo -warnaserror+ -target:exe -langversion:7.3 \
  -out:build/NetTests.exe src/Net/*.cs tests/*.cs
csc -nologo -warnaserror+ -target:exe -langversion:7.3 \
  -out:build/FakePlayer.exe src/Net/*.cs tools/Common.cs tools/FakePlayer.cs
mono build/NetTests.exe
(cd server && python3 -m unittest -q test_relay)
cp RDR2Coop.ini dist/scripts/RDR2Coop.ini
echo "built dist/scripts/RDR2Coop.dll"
