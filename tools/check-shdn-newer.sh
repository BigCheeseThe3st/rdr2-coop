#!/bin/sh
# The mod is built against ScriptHookRDR2DotNet V2's 2.2.0 release, but players
# also run builds made from its later source, which still call themselves 2.2.0
# and changed some of the API (Global.GetInt and friends went in June 2023, and
# the mod was aborted on the first tick). This builds that later API from source
# and checks the mod only calls what both have, with the same signatures.
set -e
cd "$(dirname "$0")/.."
SHA=03f2264d8d2c1e4c6e79d10cf7ba48d247c6e506
DIR=build/shdn-newer
REF=/usr/lib/mono/4.8-api
LIBS="-nostdlib -r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll -r:$REF/System.Numerics.dll -r:$REF/System.Xml.dll -r:$REF/System.Runtime.Serialization.dll -r:$REF/System.Windows.Forms.dll -r:$REF/System.Drawing.dll"
if [ ! -f $DIR/ScriptHookRDRNetAPI.dll ]; then
  rm -rf $DIR && mkdir -p $DIR/src
  curl -sL https://github.com/Halen84/ScriptHookRDR2DotNet-V2/archive/$SHA.tar.gz | tar -xz -C $DIR/src --strip-components=1
  # Three method bodies don't compile at that commit; only the API (signatures) matters here.
  sed -i 's/!Ped\.Exists(driver)/(driver == null || !driver.Exists())/; s/!Ped\.Exists(ped)/(ped == null || !ped.Exists())/' $DIR/src/source/scripting_v3/RDR2/Entities/Vehicles/Vehicle.cs
  sed -i '59s/.*/throw new System.NotImplementedException();/; 108,109s/.*/throw new System.NotImplementedException();/' $DIR/src/source/scripting_v3/RDR2/Global.cs
  find $DIR/src/source -name '*.cs' > $DIR/files.txt
  csc -nologo -target:library -langversion:9.0 -unsafe -nowarn:1591,0618,0612,1573,0168,0649,0169,0414,0162 $LIBS -r:$REF/Facades/netstandard.dll \
    -out:$DIR/ScriptHookRDRNetAPI.dll @$DIR/files.txt
fi
csc -nologo -target:library -langversion:7.3 -optimize+ -unsafe -define:NET48 $LIBS -r:$DIR/ScriptHookRDRNetAPI.dll \
  -out:$DIR/RDR2Coop.dll build/BuildInfo.cs src/Net/*.cs src/Mod/*.cs
# Every call into ScriptHookRDR2DotNet with its full signature, as each build resolves it.
refs() { MONO_PATH=$2 monodis --memberref $1 | python3 -c '
import sys
l = sys.stdin.read().split("\n")
print("\n".join(sorted({l[i].strip() + "  " + l[i + 1].strip() for i in range(len(l) - 1) if l[i].strip().startswith("Resolved:") and "ScriptHookRDRNetAPI" in l[i]})))'; }
refs dist/shdn/scripts/RDR2Coop.dll lib > $DIR/calls-2.2.0.txt
refs $DIR/RDR2Coop.dll $DIR > $DIR/calls-newer.txt
# monodis prints "<BROKEN CLASS ...>" for what it can't resolve (a native's own name may say BROKEN: HAS_OBJECT_BEEN_BROKEN).
grep -q '<BROKEN' $DIR/calls-2.2.0.txt $DIR/calls-newer.txt && { echo "check-shdn-newer: a signature didn't resolve"; exit 1; }
diff $DIR/calls-2.2.0.txt $DIR/calls-newer.txt || { echo "check-shdn-newer: these calls differ between ScriptHookRDR2DotNet 2.2.0 and later builds"; exit 1; }
echo "check-shdn-newer: all $(wc -l < $DIR/calls-2.2.0.txt) calls match ScriptHookRDR2DotNet 2.2.0 and later builds"
