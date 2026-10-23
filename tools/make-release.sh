#!/bin/sh
# Builds the two Nexus uploads in release/upload/:
#   RDR2-Coop-<version>.zip                 main file: mod, Linux installer, server
#   RDR2-Coop-enRage-Patch-<version>.zip    optional file: the enRage co-op patch
# Both unpack into a folder called RDR2-Coop, so the patch lands in the same
# folder as the mod. The enRage part is only a binary patch (useless without the
# official NemesisMod.dll): no enRage code or files go in either download.
#   tools/make-release.sh
# Versions come from VERSION and ENRAGE_PATCH_VERSION; each needs its entry in
# CHANGELOG.md / CHANGELOG-ENRAGE-PATCH.md, and a version that was already built
# is refused (bump it, or FORCE=1 to rebuild the same one).
# Needs: ~/enrage-private (built: cd src && dotnet build -c Release), the
# official DLL it was made from, and bsdiff4 in ~/.venvs/rdr2-release.
set -e
cd "$(dirname "$0")/.."
ENRAGE=$HOME/enrage-private
OFFICIAL=${OFFICIAL:-$ENRAGE/backups/20261023/NemesisMod.official.dll}
PATCHED=$ENRAGE/src/bin/Release/net48/NemesisMod.dll
VERSION=$(tr -d ' \n' < VERSION)
PATCH_VERSION=$(tr -d ' \n' < ENRAGE_PATCH_VERSION)
grep -q "^## $VERSION " CHANGELOG.md || { echo "CHANGELOG.md has no '## $VERSION' entry"; exit 1; }
grep -q "^## $PATCH_VERSION " CHANGELOG-ENRAGE-PATCH.md || { echo "CHANGELOG-ENRAGE-PATCH.md has no '## $PATCH_VERSION' entry"; exit 1; }
BUILT=release/built-versions.txt
if [ -z "${FORCE:-}" ] && grep -qx "coop $VERSION\|enrage-patch $PATCH_VERSION" "$BUILT" 2>/dev/null; then
  echo "coop $VERSION or enrage-patch $PATCH_VERSION was built before: bump the version + changelog (or FORCE=1)"; exit 1
fi
N=RDR2-Coop                              # top folder in both zips
OUT=release/.stage/main/$N
ADDON=release/.stage/addon/$N            # optional add-on, unpacked into the same folder
UPLOAD=release/upload

./build.sh
rm -rf release/.stage "$UPLOAD" && mkdir -p "$OUT/scripts" "$OUT/scripthookdotnet" "$OUT/server" "$ADDON/enrage-coop-patch" "$UPLOAD"
cp dist/scripts/RDR2Coop.dll RDR2Coop.ini "$OUT/scripts/"
cp setup.py prepare-prefix.sh README-PLAYERS.txt "$OUT/"
cp CHANGELOG.md "$OUT/CHANGELOG.txt"
echo "$VERSION" > "$OUT/VERSION.txt"
# No Windows installer exe in the download (Nexus quarantines executables);
# its source stays in tools/WinSetup.
cp vendor/scripthookdotnet/ScriptHookRDRDotNet.asi vendor/scripthookdotnet/ScriptHookRDRDotNet.ini \
   vendor/scripthookdotnet/ScriptHookRDRNetAPI.dll vendor/scripthookdotnet/LICENSE.txt "$OUT/scripthookdotnet/"
cp server/relay.py server/setup-server.sh server/rdr2coop-relay.service server/README-SERVER.md "$OUT/server/"
chmod +x "$OUT/setup.py" "$OUT/prepare-prefix.sh" "$OUT/server/setup-server.sh" "$OUT/server/relay.py"

ENRAGE_VERSION=$(strings -el "$OFFICIAL" | grep -m1 -E '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$')
# bsdiff's three blocks re-packed with raw deflate (RDCOPAT1): both installers
# (Python on Linux, .NET on Windows) can read that without extra libraries.
~/.venvs/rdr2-release/bin/python - "$OFFICIAL" "$PATCHED" "$ADDON/enrage-coop-patch/NemesisMod.coop.patch" <<'EOF'
import bz2, struct, sys, zlib, bsdiff4
old, new, out = sys.argv[1:]
d = bsdiff4.diff(open(old, "rb").read(), open(new, "rb").read())
def off(b):
    v = struct.unpack("<Q", b)[0]
    return -(v & 0x7FFFFFFFFFFFFFFF) if v & 0x8000000000000000 else v
lc, ld, n = (off(d[8 + 8 * i:16 + 8 * i]) for i in range(3))
blocks = [bz2.decompress(d[32:32 + lc]), bz2.decompress(d[32 + lc:32 + lc + ld]), bz2.decompress(d[32 + lc + ld:])]
def deflate(b):
    c = zlib.compressobj(9, zlib.DEFLATED, -15)
    return c.compress(b) + c.flush()
packed = [deflate(b) for b in blocks]
open(out, "wb").write(b"RDCOPAT1" + struct.pack("<qqqq", n, *map(len, packed)) + b"".join(packed))
EOF
cp CHANGELOG-ENRAGE-PATCH.md "$ADDON/enrage-coop-patch/CHANGELOG.txt"
cat > "$ADDON/enrage-coop-patch/patch.txt" <<EOF
patch_version=$PATCH_VERSION
needs_coop=$VERSION
enrage_version=$ENRAGE_VERSION
base_sha256=$(sha256sum "$OFFICIAL" | cut -d' ' -f1)
patched_sha256=$(sha256sum "$PATCHED" | cut -d' ' -f1)
EOF
cat > "$ADDON/enrage-coop-patch/NOTICE.txt" <<'EOF'
Unofficial co-op patch for enRage (Nexus mod 10967,
https://www.nexusmods.com/reddeadredemption2/mods/10967), all credit for enRage
to its author. Not made or supported by the enRage author. Free.
The patch only works on your own copy of the official enRage download; it
contains no enRage files. setup.py applies it and keeps your official copy as
scripts/NemesisMod.dll.official.
EOF

# Self-check: the installer's own patcher must rebuild the patched DLL exactly.
python3 - "$OUT" "$OFFICIAL" "$ADDON/enrage-coop-patch" <<'EOF'
import hashlib, sys
sys.path.insert(0, sys.argv[1])
import setup
d = __import__("pathlib").Path(sys.argv[3])
info = dict(l.split("=", 1) for l in (d / "patch.txt").read_text().split())
new = setup.apply_patch(open(sys.argv[2], "rb").read(), (d / "NemesisMod.coop.patch").read_bytes())
assert hashlib.sha256(new).hexdigest() == info["patched_sha256"], "patch mismatch"
print("self-check: patch rebuilds the co-op enRage exactly")
EOF
rm -rf "$OUT/__pycache__"
# The add-on unpacks into the base folder: its files sit under the same top folder name.
cat > "$ADDON/README-ENRAGE-PATCH.txt" <<'EOF'
Unofficial enRage co-op patch (optional add-on for RDR2 Co-op)
===========================================================
Shares the host's enRage world (nemeses, grudges, ambushes) with the co-op
party: your partners' kills, wounds and knockouts count as yours, and enRage
never turns a co-op player into a nemesis.

Needs: RDR2 Co-op (main download) and enRage 0.9.5.1 from Nexus (mod 10967).
Only the player who usually hosts needs it.

  1. Unpack this in the same place as the main download: it goes into the
     same RDR2-Coop folder (adds the enrage-coop-patch folder there).
  2. Put the official enRage 0.9.5.1 zip from Nexus in that folder too
     (or have enRage already installed in the game).
  3. Run the installer again (python3 setup.py, Linux), say you usually
     host, and yes to enRage. (The patch has to be applied to your own
     enRage file, which only the installer does.)
Your official enRage is kept; uninstalling the co-op mod puts it back.
Not made or supported by the enRage author; all credit for enRage to them.
EOF
(cd release/.stage/main && zip -qr "../../upload/RDR2-Coop-$VERSION.zip" "$N")
(cd release/.stage/addon && zip -qr "../../upload/RDR2-Coop-enRage-Patch-$PATCH_VERSION.zip" "$N")
grep -qx "coop $VERSION" "$BUILT" 2>/dev/null || echo "coop $VERSION" >> "$BUILT"
grep -qx "enrage-patch $PATCH_VERSION" "$BUILT" 2>/dev/null || echo "enrage-patch $PATCH_VERSION" >> "$BUILT"
rm -rf release/.stage
ls -la "$UPLOAD"
