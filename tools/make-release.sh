#!/bin/sh
# Builds the Nexus uploads in release/upload/:
#   RDR2-Coop-<v>.zip                       main file: the mod (RDR2Coop.asi + RDR2Coop/ with its own .NET 9) + settings + readme
#   RDR2-Coop-ScriptHookDotNet-<v>.zip      optional: the ScriptHookRDR2DotNet (.NET 4.8) build, for enRage hosts
#   RDR2-Coop-Server-<v>.zip                optional: the server (relay + guide + Linux service)
#   RDR2-Coop-Linux-Installer-<v>.zip       optional: setup.py + prefix script
#   RDR2-Coop-Full-<v>.zip                  GitHub release: all of them in one (incl. the enRage patch)
#   RDR2-Coop-Windows-<v>.zip               GitHub release: mod + server + enRage patch + Windows installer exe
#   RDR2-Coop-enRage-Patch-<pv>.zip         optional: the enRage co-op patch (binary patch)
# All unpack into a folder called RDR2-Coop, so they land together. The main file
# carries the .NET runtime (Microsoft, MIT; ~35 MB zipped), so its first Nexus
# upload may wait in quarantine. No enRage code or files ship.
#   tools/make-release.sh
#   SKIP_BUILD=1 tools/make-release.sh      packs whatever dist/ holds (testing the zips)
# Versions: VERSION (mod + installer) and ENRAGE_PATCH_VERSION, each with its
# entry in CHANGELOG.md / CHANGELOG-ENRAGE-PATCH.md. A mod version that was
# already built is refused (bump it, or FORCE=1); the enRage patch keeps its
# version only while the patched DLL is byte-identical.
# Needs: ~/enrage-private (built: cd src && dotnet build -c Release), the
# official DLL it was made from, and bsdiff4 in ~/.venvs/rdr2-release.
set -e
cd "$(dirname "$0")/.."
ENRAGE=$HOME/enrage-private
OFFICIAL=${OFFICIAL:-$ENRAGE/backups/20261024/NemesisMod.official.dll}   # enRage 0.9.5.2
PATCHED=$ENRAGE/src/bin/Release/net48/NemesisMod.dll
VERSION=$(tr -d ' \n' < VERSION)
PATCH_VERSION=$(tr -d ' \n' < ENRAGE_PATCH_VERSION)
PATCHED_SHA=$(sha256sum "$PATCHED" | cut -d' ' -f1)
grep -q "^## $VERSION " CHANGELOG.md || { echo "CHANGELOG.md has no '## $VERSION' entry"; exit 1; }
grep -q "^## $PATCH_VERSION " CHANGELOG-ENRAGE-PATCH.md || { echo "CHANGELOG-ENRAGE-PATCH.md has no '## $PATCH_VERSION' entry"; exit 1; }
BUILT=release/built-versions.txt
mkdir -p release && touch "$BUILT"
if [ -z "${FORCE:-}" ] && grep -qx "coop $VERSION" "$BUILT"; then
  echo "coop $VERSION was built before: bump VERSION + CHANGELOG.md (or FORCE=1)"; exit 1
fi
if grep -q "^enrage-patch $PATCH_VERSION " "$BUILT" && ! grep -qx "enrage-patch $PATCH_VERSION $PATCHED_SHA" "$BUILT"; then
  echo "the enRage patch changed since $PATCH_VERSION was built: bump ENRAGE_PATCH_VERSION + its changelog"; exit 1
fi
N=RDR2-Coop                              # top folder in every zip
MAIN=release/.stage/main/$N
SHDN=release/.stage/shdn/$N
TOOLS=release/.stage/tools/$N
SERVER=release/.stage/server/$N
ADDON=release/.stage/addon/$N
UPLOAD=release/upload

[ -n "${SKIP_BUILD:-}" ] || { ./build.sh; tools/check-shdn-newer.sh; }
# build.sh's dist/ is the game-file part of the release (the layout in BUILDING.md); each piece comes from its own build.
for f in "dist/RDR2Coop.asi (the loader, host/)" "dist/RDR2Coop/RDR2Coop.Core.dll (api/Core)" "dist/RDR2Coop/RDR2Coop.Core.runtimeconfig.json (api/Core)" \
         "dist/RDR2Coop/RDR2Coop.Api.dll (api/Api)" "dist/RDR2Coop/RDR2Coop.dll (the mod, net9)" "dist/RDR2Coop/NOTICE.txt (build.sh)" \
         "dist/RDR2Coop/ScriptHookRDRNetAPI.dll (api/Compat)" "dist/RDR2Coop/System.Windows.Forms.dll (api/Compat)" "dist/RDR2Coop/System.Diagnostics.EventLog.dll (build.sh)" \
         "dist/RDR2Coop/runtime/host/fxr/*/hostfxr.dll (host/make-runtime.sh)" \
         "dist/RDR2Coop/runtime/shared/Microsoft.NETCore.App/*/Microsoft.NETCore.App.deps.json (host/make-runtime.sh)" \
         "dist/RDR2Coop/runtime/shared/Microsoft.NETCore.App/*/Microsoft.NETCore.App.runtimeconfig.json (host/make-runtime.sh)" \
         "dist/scripts/RDR2Coop.ini (build.sh)" "dist/shdn/scripts/RDR2Coop.dll (the mod, net48)"; do
  ls ${f%% *} >/dev/null 2>&1 || { echo "build.sh didn't produce $f: nothing packed"; exit 1; }
done
[ "$(ls dist/scripts)" = RDR2Coop.ini ] || { echo "dist/scripts must hold only RDR2Coop.ini (ScriptHookRDR2DotNet loads every DLL there): $(ls dist/scripts | tr '\n' ' ')"; exit 1; }
rm -rf release/.stage "$UPLOAD" && mkdir -p "$MAIN" "$SHDN/shdn/scripts" "$TOOLS" "$SERVER/server" "$ADDON/enrage-coop-patch" "$UPLOAD"

# The one-press update (src/Mod/Updater.cs) installs only a mod signed with the release
# key, and learns the newest version from latest.txt: all go on the GitHub release
# beside the zips: RDR2Coop.dll + .sig (the ScriptHookRDR2DotNet build, name unchanged so
# every older install keeps updating), RDR2Coop.net9.dll + .sig (this layout), latest.txt.
KEY=${RELEASE_KEY:-$HOME/.config/rdr2-coop/release-signing.pem}
[ -f "$KEY" ] || { echo "no release signing key at $KEY"; exit 1; }
python3 - "$KEY" <<'PY' || { echo "the release key isn't the one in src/Net/UpdateCheck.cs: players would refuse this release"; exit 1; }
import base64, subprocess, sys
mod = subprocess.check_output(['openssl', 'rsa', '-in', sys.argv[1], '-noout', '-modulus'], stderr=subprocess.DEVNULL).decode().strip().split('=', 1)[1]
sys.exit(0 if base64.b64encode(bytes.fromhex(mod)).decode() in open('src/Net/UpdateCheck.cs').read() else 1)
PY
openssl rsa -in "$KEY" -pubout -out release/.stage/release-key.pub 2>/dev/null
sign() {   # sign <file in dist> <asset name>
  cp "$1" "$UPLOAD/$2"
  openssl dgst -sha256 -sign "$KEY" -out "$UPLOAD/$2.sig" "$UPLOAD/$2"
  openssl dgst -sha256 -verify release/.stage/release-key.pub -signature "$UPLOAD/$2.sig" "$UPLOAD/$2" >/dev/null \
    || { echo "the release signature on $2 doesn't verify"; exit 1; }
}
sign dist/shdn/scripts/RDR2Coop.dll RDR2Coop.dll
sign dist/RDR2Coop/RDR2Coop.dll RDR2Coop.net9.dll
PROTO=$(sed -n 's/.*public const int Version = \([0-9]*\);.*/\1/p' src/Net/Protocol.cs)
# The RDR2Coop.Api this mod was built against (bump <Version> there whenever api/ changes): the .NET 9 updater
# refuses the one-press update and points at the zip when the installed Api is older (src/Mod/Updater.cs).
API=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' api/Api/RDR2Coop.Api.csproj)
[ -n "$API" ] || { echo "no <Version> in api/Api/RDR2Coop.Api.csproj"; exit 1; }
NOTES=$(awk -v v="## $VERSION " 'index($0, v) == 1 { on = 1; next } /^## / { on = 0 } on' CHANGELOG.md \
  | awk '/^- / { if (n++) exit; sub(/^- /, ""); printf "%s", $0; next } n && /^  / { sub(/^ +/, " "); printf "%s", $0 }')   # the first entry, unwrapped
printf '%s\n%s\n%s\napi %s\n' "$VERSION" "$PROTO" "$NOTES" "$API" > "$UPLOAD/latest.txt"
echo "signed RDR2Coop.dll + RDR2Coop.net9.dll for the in-game update; latest.txt says $VERSION (protocol $PROTO, api $API)"

# Main file: what goes in the game folder, laid out as it goes there, and how.
cp -r dist/RDR2Coop.asi dist/RDR2Coop dist/scripts "$MAIN/"
cp README-PLAYERS.txt "$MAIN/"
cp CHANGELOG.md "$MAIN/CHANGELOG.txt"
echo "$VERSION" > "$MAIN/VERSION.txt"

# ScriptHookRDR2DotNet build (enRage hosts). Under shdn/ so unpacking it beside the
# main file never puts a second copy of the mod where a hand install would copy both.
cp dist/shdn/scripts/RDR2Coop.dll RDR2Coop.ini "$SHDN/shdn/scripts/"
cat > "$SHDN/README-SCRIPTHOOKDOTNET.txt" <<EOF
RDR2 Co-op $VERSION for ScriptHookRDR2DotNet (optional download, enRage hosts)
=============================================================================
Only for the player who hosts with the unofficial enRage co-op patch: the enRage
link works in this version only. Everyone else uses the main download.
Needs ScriptHookRDR2, ScriptHookRDR2DotNet V2 2.2.0 and .NET Framework 4.8
(Linux: prepare-prefix.sh from the Linux installer download gives the game's
Proton prefix .NET 4.8; the installer runs it when you say yes to enRage).
By hand: copy shdn\scripts\RDR2Coop.dll (and RDR2Coop.ini if you have none) into
the game's scripts folder, and DELETE RDR2Coop.asi and the RDR2Coop folder from the
game folder so only one copy of the mod runs. Or let the installer do it
(RDR2CoopSetup.exe / python3 setup.py: say yes to enRage).
EOF

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
                     Installs ScriptHookRDR2 if missing (downloaded from its
                     official site, checksum-checked), the co-op mod and its
                     settings. Nothing else: the mod brings its own .NET.
                     A host who has the enRage co-op patch download too gets
                     the ScriptHookRDR2DotNet version of the mod instead (the
                     RDR2-Coop-ScriptHookDotNet download, unpacked here): for
                     that setup.py installs ScriptHookRDR2DotNet and offers
                     prepare-prefix.sh.
                     python3 setup.py --uninstall takes it out again.
  prepare-prefix.sh  Gives a fresh Proton prefix .NET 4.8 (10-20 minutes);
                     only the ScriptHookRDR2DotNet version needs it.
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
contains no enRage files. The co-op mod applies it in memory each time the
game starts, leaving your enRage file as it is (the installers, for the
ScriptHookRDR2DotNet version of co-op, apply it to the file and keep your
official copy as scripts/NemesisMod.dll.official).
EOF
cat > "$ADDON/README-ENRAGE-PATCH.txt" <<EOF
Unofficial enRage co-op patch (optional add-on for RDR2 Co-op)
===========================================================
Shares the host's enRage world (nemeses, grudges, ambushes) with the co-op
party: your partners' kills, wounds and knockouts count as yours, and enRage
never turns a co-op player into a nemesis.

Since RDR2 Co-op 0.9.0-beta.8.1 this patch comes with the mod itself (in
RDR2Coop/enrage-coop-patch) and the mod applies it in memory: install the
official enRage $ENRAGE_VERSION from Nexus (mod 10967) as usual, keep
ScriptHookRDR2DotNet out of the game folder, and that's all. Only the player
who usually hosts needs enRage.

This separate download is for the ScriptHookRDR2DotNet version of co-op only
(players who need ScriptHookRDR2DotNet for other mods):
  1. Unpack this in the same place as the co-op zip: it goes into the same
     RDR2-Coop folder (adds the enrage-coop-patch folder).
  2. Put the official enRage $ENRAGE_VERSION zip from Nexus in that folder too
     (or have enRage already installed in the game).
  3. Run the installer (Windows: RDR2CoopSetup.exe; Linux: python3 setup.py),
     say you usually host, and yes to enRage.
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
# The mod's loader applies the patch itself, in memory, to the official enRage (api/Core/EnragePatch.cs): it rides in
# the main file, so an enRage host needs no installer. Same self-check for the loader's patcher.
ENRAGE_OFFICIAL="$OFFICIAL" ENRAGE_PATCH_DIR="$ADDON/enrage-coop-patch" dotnet api/CoreTests/bin/Release/net9.0/CoreTests.dll \
  | grep -q "ok   the real enRage co-op patch" || { echo "the loader's in-memory enRage patch doesn't rebuild the co-op enRage: nothing packed"; exit 1; }
echo "self-check: the loader's in-memory patch rebuilds the co-op enRage exactly"
mkdir -p "$MAIN/RDR2Coop/enrage-coop-patch"
cp "$ADDON"/enrage-coop-patch/NemesisMod.coop.patch "$ADDON"/enrage-coop-patch/patch.txt "$ADDON"/enrage-coop-patch/NOTICE.txt "$ADDON"/enrage-coop-patch/CHANGELOG.txt "$MAIN/RDR2Coop/enrage-coop-patch/"

[ "$(ls "$MAIN/scripts")" = RDR2Coop.ini ] || { echo "the main zip's scripts/ must hold only RDR2Coop.ini"; exit 1; }
(cd release/.stage/main && zip -qr "../../upload/RDR2-Coop-$VERSION.zip" "$N")
(cd release/.stage/shdn && zip -qr "../../upload/RDR2-Coop-ScriptHookDotNet-$VERSION.zip" "$N")
(cd release/.stage/server && zip -qr "../../upload/RDR2-Coop-Server-$VERSION.zip" "$N")
(cd release/.stage/tools && zip -qr "../../upload/RDR2-Coop-Linux-Installer-$VERSION.zip" "$N")
# GitHub: everything in one zip (mod + ScriptHookRDR2DotNet build + server + Linux installer
# + enRage patch; the installer only offers the patch to a host who has enRage).
mkdir -p release/.stage/full && cp -r "$MAIN" release/.stage/full/ && cp -r "$SHDN"/. "$SERVER"/. "$TOOLS"/. "$ADDON"/. release/.stage/full/$N/
(cd release/.stage/full && zip -qr "../../upload/RDR2-Coop-Full-$VERSION.zip" "$N")

# GitHub: Windows version: the mod + ScriptHookRDR2DotNet build + server + enRage patch +
# RDR2CoopSetup.exe, with ScriptHookRDR2DotNet V2 bundled (zlib licence; the enRage
# option needs it) since Windows can't unpack its .rar.
dotnet build tools/WinSetup/RDR2CoopSetup.csproj -c Release -nologo -v q > /dev/null
WIN=release/.stage/win/$N
mkdir -p release/.stage/win && cp -r "$MAIN" release/.stage/win/ && cp -r "$SHDN"/. "$SERVER"/. "$ADDON"/. "$WIN"/
cp tools/WinSetup/bin/Release/net48/RDR2CoopSetup.exe tools/WinSetup/bin/Release/net48/RDR2CoopSetup.exe.config "$WIN/"
# Nothing we build may carry the build machine's paths (a .pdb path names the home folder).
for f in "$WIN/RDR2CoopSetup.exe" dist/RDR2Coop/RDR2Coop.dll dist/RDR2Coop/RDR2Coop.Core.dll dist/RDR2Coop/RDR2Coop.Api.dll dist/shdn/scripts/RDR2Coop.dll dist/RDR2Coop.asi; do
  if [ -f "$f" ] && { strings -n 6 "$f"; strings -n 6 -el "$f"; } | grep -q "$HOME"; then echo "privacy: $f contains $HOME (a debug path?), not releasing" >&2; exit 1; fi
done
mkdir -p "$WIN/scripthookdotnet" && cp vendor/scripthookdotnet/* "$WIN/scripthookdotnet/"
cp tools/WinSetup/README-WINDOWS.txt "$WIN/"
(cd release/.stage/win && zip -qr "../../upload/RDR2-Coop-Windows-$VERSION.zip" "$N")
(cd release/.stage/addon && zip -qr "../../upload/RDR2-Coop-enRage-Patch-$PATCH_VERSION.zip" "$N")
grep -qx "coop $VERSION" "$BUILT" || echo "coop $VERSION" >> "$BUILT"
grep -qx "enrage-patch $PATCH_VERSION $PATCHED_SHA" "$BUILT" || echo "enrage-patch $PATCH_VERSION $PATCHED_SHA" >> "$BUILT"
rm -rf release/.stage
ls -la "$UPLOAD"
# Players on 0.7.x take any newer version from releases/latest, which skips only pre-releases.
case $VERSION in *-*) echo "$VERSION is a test or beta build: publish its GitHub release as a PRE-RELEASE, or every 0.7.x player is offered it";; esac
