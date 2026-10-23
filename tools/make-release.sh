#!/bin/sh
# Builds the Nexus uploads in release/upload/:
#   RDR2-Coop-<v>.zip                       main file: just the mod (DLL + settings + readme)
#   RDR2-Coop-Server-<v>.zip                optional: the server (relay + guide + Linux service)
#   RDR2-Coop-Linux-Installer-<v>.zip       optional: setup.py + prefix script
#   RDR2-Coop-Full-<v>.zip                  GitHub release: all of them in one (incl. the enRage patch)
#   RDR2-Coop-Windows-<v>.zip               GitHub release: mod + server + Windows installer exe
#   RDR2-Coop-enRage-Patch-<pv>.zip         optional: the enRage co-op patch (binary patch)
# All unpack into a folder called RDR2-Coop, so they land together. The main file
# stays minimal (no scripts, no third-party binaries) so it doesn't wait on
# Nexus's quarantine for things it doesn't need. No enRage code or files ship.
#   tools/make-release.sh
# Versions: VERSION (mod + installer) and ENRAGE_PATCH_VERSION, each with its
# entry in CHANGELOG.md / CHANGELOG-ENRAGE-PATCH.md. A mod version that was
# already built is refused (bump it, or FORCE=1); the enRage patch keeps its
# version only while the patched DLL is byte-identical.
# Needs: ~/enrage-private (built: cd src && dotnet build -c Release), the
# official DLL it was made from, and bsdiff4 in ~/.venvs/rdr2-release.
set -e
cd "$(dirname "$0")/.."
ENRAGE=$HOME/enrage-private
OFFICIAL=${OFFICIAL:-$ENRAGE/backups/20261023/NemesisMod.official.dll}
PATCHED=$ENRAGE/src/bin/Release/net48/NemesisMod.dll
VERSION=$(tr -d ' \n' < VERSION)
PATCH_VERSION=$(tr -d ' \n' < ENRAGE_PATCH_VERSION)
PATCHED_SHA=$(sha256sum "$PATCHED" | cut -d' ' -f1)
grep -q "^## $VERSION " CHANGELOG.md || { echo "CHANGELOG.md has no '## $VERSION' entry"; exit 1; }
grep -q "^## $PATCH_VERSION " CHANGELOG-ENRAGE-PATCH.md || { echo "CHANGELOG-ENRAGE-PATCH.md has no '## $PATCH_VERSION' entry"; exit 1; }
BUILT=release/built-versions.txt
touch "$BUILT"
if [ -z "${FORCE:-}" ] && grep -qx "coop $VERSION" "$BUILT"; then
  echo "coop $VERSION was built before: bump VERSION + CHANGELOG.md (or FORCE=1)"; exit 1
fi
if grep -q "^enrage-patch $PATCH_VERSION " "$BUILT" && ! grep -qx "enrage-patch $PATCH_VERSION $PATCHED_SHA" "$BUILT"; then
  echo "the enRage patch changed since $PATCH_VERSION was built: bump ENRAGE_PATCH_VERSION + its changelog"; exit 1
fi
N=RDR2-Coop                              # top folder in every zip
MAIN=release/.stage/main/$N
TOOLS=release/.stage/tools/$N
SERVER=release/.stage/server/$N
ADDON=release/.stage/addon/$N
UPLOAD=release/upload

./build.sh
rm -rf release/.stage "$UPLOAD" && mkdir -p "$MAIN/scripts" "$TOOLS" "$SERVER/server" "$ADDON/enrage-coop-patch" "$UPLOAD"

# Main file: only what goes in the game's scripts folder, and how.
cp dist/scripts/RDR2Coop.dll RDR2Coop.ini "$MAIN/scripts/"
cp README-PLAYERS.txt "$MAIN/"
cp CHANGELOG.md "$MAIN/CHANGELOG.txt"
echo "$VERSION" > "$MAIN/VERSION.txt"

# Server (runs anywhere with Python 3, standard library only).
cp server/relay.py server/setup-server.sh server/rdr2coop-relay.service server/README-SERVER.md "$SERVER/server/"
chmod +x "$SERVER/server/setup-server.sh" "$SERVER/server/relay.py"

# Linux installer.
cp setup.py prepare-prefix.sh "$TOOLS/"
chmod +x "$TOOLS/setup.py" "$TOOLS/prepare-prefix.sh"
cat > "$TOOLS/README-INSTALLER.txt" <<'EOF'
RDR2 Co-op - Linux installer (optional download)
================================================
Unpack this in the same place as the main RDR2 Co-op download: it goes into
the same RDR2-Coop folder.

  setup.py           Linux (Steam + Proton) installer: python3 setup.py
                     Installs ScriptHookRDR2 + ScriptHookRDR2DotNet if missing
                     (downloads them from their official sites, checksum-checked),
                     the co-op mod and its settings, and the enRage co-op patch for
                     a host who has that optional download too.
                     python3 setup.py --uninstall takes it out again.
  prepare-prefix.sh  Gives a fresh Proton prefix .NET 4.8 (setup.py offers it).
EOF

# enRage patch: bsdiff's three blocks re-packed with raw deflate (RDCOPAT1),
# which setup.py reads with the standard library only.
ENRAGE_VERSION=$(strings -el "$OFFICIAL" | grep -m1 -E '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$')
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
patched_sha256=$PATCHED_SHA
EOF
cat > "$ADDON/enrage-coop-patch/NOTICE.txt" <<'EOF'
Unofficial co-op patch for enRage (Nexus mod 10967,
https://www.nexusmods.com/reddeadredemption2/mods/10967), all credit for enRage
to its author. Not made or supported by the enRage author. Free.
The patch only works on your own copy of the official enRage download; it
contains no enRage files. The co-op installer (RDR2CoopSetup.exe or setup.py)
applies it and keeps your official copy as scripts/NemesisMod.dll.official.
EOF
cat > "$ADDON/README-ENRAGE-PATCH.txt" <<'EOF'
Unofficial enRage co-op patch (optional add-on for RDR2 Co-op)
===========================================================
Shares the host's enRage world (nemeses, grudges, ambushes) with the co-op
party: your partners' kills, wounds and knockouts count as yours, and enRage
never turns a co-op player into a nemesis.

Needs: RDR2 Co-op with its installer (the Windows or Linux zip from
https://github.com/BigCheeseThe3st/rdr2-coop/releases) and enRage 0.9.5.1
from Nexus (mod 10967). Only the player who usually hosts needs it.

  1. Unpack this in the same place as the co-op zip: it goes into the same
     RDR2-Coop folder (adds the enrage-coop-patch folder).
  2. Put the official enRage 0.9.5.1 zip from Nexus in that folder too
     (or have enRage already installed in the game).
  3. Run the installer (Windows: RDR2CoopSetup.exe; Linux: python3 setup.py),
     say you usually host, and yes to enRage. (The patch has to be applied to
     your own enRage file, which only the installer does.)
Your official enRage is kept; uninstalling the co-op mod puts it back.
Not made or supported by the enRage author; all credit for enRage to them.
EOF

# Self-check: the installer's own patcher must rebuild the patched DLL exactly.
python3 - "$TOOLS" "$OFFICIAL" "$ADDON/enrage-coop-patch" <<'EOF'
import hashlib, sys
sys.path.insert(0, sys.argv[1])
import setup
d = __import__("pathlib").Path(sys.argv[3])
info = dict(l.split("=", 1) for l in (d / "patch.txt").read_text().split())
new = setup.apply_patch(open(sys.argv[2], "rb").read(), (d / "NemesisMod.coop.patch").read_bytes())
assert hashlib.sha256(new).hexdigest() == info["patched_sha256"], "patch mismatch"
print("self-check: patch rebuilds the co-op enRage exactly")
EOF
rm -rf "$TOOLS/__pycache__"

(cd release/.stage/main && zip -qr "../../upload/RDR2-Coop-$VERSION.zip" "$N")
(cd release/.stage/server && zip -qr "../../upload/RDR2-Coop-Server-$VERSION.zip" "$N")
(cd release/.stage/tools && zip -qr "../../upload/RDR2-Coop-Linux-Installer-$VERSION.zip" "$N")
# GitHub: everything in one zip (mod + server + Linux installer + enRage patch;
# the installer only offers the patch to a host who has enRage).
mkdir -p release/.stage/full && cp -r "$MAIN" release/.stage/full/ && cp -r "$SERVER"/. "$TOOLS"/. "$ADDON"/. release/.stage/full/$N/
(cd release/.stage/full && zip -qr "../../upload/RDR2-Coop-Full-$VERSION.zip" "$N")

# GitHub: Windows version: the mod + server + RDR2CoopSetup.exe, with
# ScriptHookRDR2DotNet V2 bundled (zlib licence) since Windows can't unpack its .rar.
dotnet build tools/WinSetup/RDR2CoopSetup.csproj -c Release -nologo -v q > /dev/null
WIN=release/.stage/win/$N
mkdir -p release/.stage/win && cp -r "$MAIN" release/.stage/win/ && cp -r "$SERVER"/. "$WIN"/
cp tools/WinSetup/bin/Release/net48/RDR2CoopSetup.exe tools/WinSetup/bin/Release/net48/RDR2CoopSetup.exe.config "$WIN/"
mkdir -p "$WIN/scripthookdotnet" && cp vendor/scripthookdotnet/* "$WIN/scripthookdotnet/"
cp tools/WinSetup/README-WINDOWS.txt "$WIN/"
(cd release/.stage/win && zip -qr "../../upload/RDR2-Coop-Windows-$VERSION.zip" "$N")
(cd release/.stage/addon && zip -qr "../../upload/RDR2-Coop-enRage-Patch-$PATCH_VERSION.zip" "$N")
grep -qx "coop $VERSION" "$BUILT" || echo "coop $VERSION" >> "$BUILT"
grep -qx "enrage-patch $PATCH_VERSION $PATCHED_SHA" "$BUILT" || echo "enrage-patch $PATCH_VERSION $PATCHED_SHA" >> "$BUILT"
rm -rf release/.stage
ls -la "$UPLOAD"
