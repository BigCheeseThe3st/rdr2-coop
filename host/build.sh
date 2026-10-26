#!/bin/sh
# Builds RDR2Coop.asi (the loader, host/RDR2Coop.cpp) as a Windows x64 DLL with zig.
#   host/build.sh [outdir]      default outdir: dist (the game-folder image)
# The .lib/.pdb side files zig emits stay in build/host.
set -e
cd "$(dirname "$0")"
OUT=${1:-../dist}
ZIG=${ZIG:-$HOME/.local/zig/zig-x86_64-linux-0.17.0/zig}
[ -x "$ZIG" ] || { echo "zig not found at $ZIG (set ZIG=/path/to/zig)"; exit 1; }
VERSION=$(tr -d ' \n' < ../VERSION)
mkdir -p ../build/host "$OUT"
# -fms-extensions: __try/__except for the pool guard (RDR2Coop_worldGetAll).
# No -static: with zig that produces an archive, not a DLL. libc++ and the mingw CRT are linked in statically by default.
"$ZIG" c++ -target x86_64-windows-gnu -shared -O2 -std=c++17 -fms-extensions -Wall -Wextra -Werror \
  -DRDR2COOP_VERSION="\"$VERSION\"" -o ../build/host/RDR2Coop.asi RDR2Coop.cpp
python3 -I check-pe.py ../build/host/RDR2Coop.asi
cp ../build/host/RDR2Coop.asi "$OUT/RDR2Coop.asi"
echo "built $OUT/RDR2Coop.asi ($VERSION)"
