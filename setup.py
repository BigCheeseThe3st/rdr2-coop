#!/usr/bin/env python3
"""RDR2 Co-op installer for Steam (Proton) on Linux.

    python3 setup.py              install or update, asking a few questions
    python3 setup.py --uninstall  take the co-op mod (and the enRage patch) out

Puts in the game folder: ScriptHookRDR2 (downloaded if missing) and the co-op
mod with its own .NET (RDR2Coop.asi + RDR2Coop/); no .NET Framework needed.
A host who uses enRage gets the ScriptHookRDR2DotNet version of the mod instead
(that one needs ScriptHookRDR2DotNet and .NET 4.8 in the Proton prefix) with the
unofficial co-op patch applied to their own copy of enRage.
"""
import getpass
import glob
import hashlib
import os
import re
import shutil
import struct
import zlib
import subprocess
import sys
import tempfile
import urllib.request
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
APP_ID = "1174180"
CACHE = Path.home() / ".cache" / "rdr2-coop"

SHR_URL = "https://www.dev-c.com/files/ScriptHookRDR2_1.0.1491.17.zip"
SHR_PAGE = "https://www.dev-c.com/rdr2/scripthookrdr2/"
# Runs inside the game, so only the exact file we tested is installed
# (same checksum from two separate downloads, 21 and 23 Oct 2026).
SHR_SHA256 = "a3be69dcd33e6cffe316d7c17ee5c4f7fcedf0c7b7fa8cf68177398e6594c39f"
SHDN_URL = "https://github.com/Halen84/ScriptHookRDR2DotNet-V2/releases/download/2.2.0/ScriptHookRDR2DotNet-V2.rar"
SHDN_SHA256 = "61682e6236618849fa96a5f4a305681c838120158857db57d72da5311f7e97f8"


PATCH_DIR = HERE / "enrage-coop-patch"
DOTNET48_RELEASE = 528040


# ---------------------------------------------------------------- questions
def ask(question, default=""):
    shown = f" [{default}]" if default else ""
    answer = input(f"{question}{shown}: ").strip()
    return answer or default


def yes(question, default=True):
    while True:
        answer = input(f"{question} [{'Y/n' if default else 'y/N'}]: ").strip().lower()
        if not answer:
            return default
        if answer in ("y", "yes", "n", "no"):
            return answer.startswith("y")


def say(text=""):
    print(text)


def fail(text):
    print(f"\n{text}")
    sys.exit(1)


# ---------------------------------------------------------------- finding the game
def steam_roots():
    home = Path.home()
    for p in (home / ".local/share/Steam", home / ".steam/steam",
              home / ".var/app/com.valvesoftware.Steam/.local/share/Steam"):
        if p.is_dir():
            yield p.resolve()


def libraries():
    seen = []
    for root in steam_roots():
        for lib in [root] + vdf_paths(root / "steamapps/libraryfolders.vdf"):
            lib = Path(lib)
            if lib.is_dir() and lib.resolve() not in seen:
                seen.append(lib.resolve())
    return seen


def vdf_paths(vdf):
    try:
        return re.findall(r'"path"\s+"([^"]+)"', vdf.read_text(errors="replace"))
    except OSError:
        return []


def find_games():
    return [lib for lib in libraries() if (lib / "steamapps/common/Red Dead Redemption 2/RDR2.exe").is_file()]


def dotnet48_ready(prefix):
    """Whether Wine's registry in the game's prefix says .NET 4.8 is installed."""
    reg = prefix / "pfx/system.reg"
    try:
        text = reg.read_text(errors="replace")
    except OSError:
        return False
    m = re.search(r'\[Software\\\\Microsoft\\\\NET Framework Setup\\\\NDP\\\\v4\\\\Full\][^\[]*?"Release"=dword:([0-9a-fA-F]+)', text)
    return bool(m) and int(m.group(1), 16) >= DOTNET48_RELEASE


# ---------------------------------------------------------------- files
def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def download(url, dest, expected_sha256, referer=None):
    dest.parent.mkdir(parents=True, exist_ok=True)
    if dest.is_file() and sha256(dest) == expected_sha256:
        return dest
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 (X11; Linux x86_64)",
                                               **({"Referer": referer} if referer else {})})
    say(f"  downloading {url.rsplit('/', 1)[-1]} ...")
    with urllib.request.urlopen(req, timeout=60) as r, open(dest.with_suffix(".part"), "wb") as f:
        shutil.copyfileobj(r, f)
    dest.with_suffix(".part").rename(dest)
    if sha256(dest) != expected_sha256:
        dest.unlink()
        fail(f"{dest.name} isn't the file this installer was tested with (checksum differs), so it wasn't installed.\n"
             f"The download site may have changed it; ask whoever gave you the co-op mod.")
    return dest


def set_ini(path, section_values):
    """Sets key=value lines in an ini, keeping its comments and other settings."""
    text = path.read_text(errors="replace") if path.exists() else ""
    for key, value in section_values.items():
        pattern = re.compile(rf"^{re.escape(key)}=.*$", re.M)
        if pattern.search(text):
            text = pattern.sub(lambda m: f"{key}={value}", text, count=1)   # value taken literally (backslashes)
        elif re.search(r"^\[Coop\][ \t]*$", text, re.M):   # new setting in an older file: into [Coop], where the mod reads it
            text = re.sub(r"^\[Coop\][ \t]*$", lambda m: f"{m.group(0)}\n{key}={value}", text, count=1, flags=re.M)
        else:
            text += f"\n{key}={value}\n"
    path.write_text(text)


# ---------------------------------------------------------------- patching
def apply_patch(old, patch):
    """Applies an RDCOPAT1 patch: bsdiff's control/diff/extra blocks, each
    raw-deflate compressed (so the Windows installer can read it too)."""
    if patch[:8] != b"RDCOPAT1":
        raise ValueError("not an RDCOPAT1 patch")
    new_size, lc, ld, le = struct.unpack("<qqqq", patch[8:40])
    pos = 40
    ctrl = zlib.decompress(patch[pos:pos + lc], -15); pos += lc
    diff = zlib.decompress(patch[pos:pos + ld], -15); pos += ld
    extra = zlib.decompress(patch[pos:pos + le], -15)
    new = bytearray(new_size)
    old_pos = new_pos = ci = di = ei = 0
    while new_pos < new_size:
        x, y, z = (offtin(ctrl[ci + 8 * k:ci + 8 * k + 8]) for k in range(3))
        ci += 24
        if x < 0 or y < 0 or new_pos + x + y > new_size:
            raise ValueError("corrupt patch")
        for i in range(x):
            o = old_pos + i
            new[new_pos + i] = (diff[di + i] + (old[o] if 0 <= o < len(old) else 0)) & 0xFF
        di += x
        new_pos += x
        old_pos += x
        new[new_pos:new_pos + y] = extra[ei:ei + y]
        ei += y
        new_pos += y
        old_pos += z
    return bytes(new)


def offtin(b):
    """bsdiff's sign-magnitude 64-bit integers."""
    v = struct.unpack("<Q", b)[0]
    return -(v & 0x7FFFFFFFFFFFFFFF) if v & 0x8000000000000000 else v


# ---------------------------------------------------------------- steps
def install_scripthook(game):
    if not (game / "ScriptHookRDR2.dll").is_file() or not (game / "dinput8.dll").is_file():
        z = download(SHR_URL, CACHE / "ScriptHookRDR2_1.0.1491.17.zip", SHR_SHA256, referer=SHR_PAGE)
        if not zipfile.is_zipfile(z):
            z.unlink()
            fail(f"ScriptHookRDR2 download failed. Get it from {SHR_PAGE}, put the zip in {CACHE} and run again.")
        with zipfile.ZipFile(z) as zf:
            for name in ("bin/ScriptHookRDR2.dll", "bin/dinput8.dll"):
                (game / Path(name).name).write_bytes(zf.read(name))
        say("  ScriptHookRDR2 1.0.1491.17 installed")


def install_shdn(game):
    """ScriptHookRDR2DotNet V2: only the enRage (.NET 4.8) version of the mod needs it."""
    if not (game / "ScriptHookRDRDotNet.asi").is_file():
        # From its official GitHub release (zlib licence); the release is a .rar.
        if not shutil.which("bsdtar"):
            fail("bsdtar is needed to unpack ScriptHookRDR2DotNet: sudo pacman -S libarchive (or your distro's package)")
        rar = download(SHDN_URL, CACHE / "ScriptHookRDR2DotNet-V2-2.2.0.rar", SHDN_SHA256)
        with tempfile.TemporaryDirectory() as t:
            subprocess.run(["bsdtar", "-xf", str(rar), "-C", t], check=True)
            for name in ("ScriptHookRDRDotNet.asi", "ScriptHookRDRDotNet.ini", "ScriptHookRDRNetAPI.dll"):
                shutil.copy2(Path(t) / name, game / name)
        say("  ScriptHookRDR2DotNet V2 2.2.0 installed")


def install_coop(game, settings):
    """The mod with its own loader and .NET (RDR2Coop.asi + RDR2Coop/); needs only ScriptHookRDR2."""
    if not (HERE / "RDR2Coop.asi").is_file() or not (HERE / "RDR2Coop").is_dir():
        fail("The mod itself isn't here: unpack the main RDR2 Co-op download into this same folder, then run this again.")
    if HERE == game.resolve():   # unpacked straight into the game folder: the files are already in place (deleting first would destroy them)
        say("  RDR2Coop.asi and RDR2Coop/ are already in the game folder")
    else:
        shutil.rmtree(game / "RDR2Coop", ignore_errors=True)   # a clean copy, so an older runtime doesn't pile up beside the new one
        shutil.copytree(HERE / "RDR2Coop", game / "RDR2Coop")
        shutil.copy2(HERE / "RDR2Coop.asi", game / "RDR2Coop.asi")
    old = game / "scripts/RDR2Coop.dll"
    if old.is_file():   # the ScriptHookRDR2DotNet version: two copies would both join the room and fight
        old.unlink()
        say("  old ScriptHookRDR2DotNet copy of the mod (scripts/RDR2Coop.dll) removed; your settings are kept")
    write_ini(game, HERE / "scripts/RDR2Coop.ini", settings)


def install_coop_shdn(game, settings):
    """The ScriptHookRDR2DotNet (.NET 4.8) version, for an enRage host: the enRage link only works there."""
    src = HERE / "shdn/scripts"
    if not (src / "RDR2Coop.dll").is_file():
        fail("The ScriptHookRDR2DotNet version of the mod isn't here: unpack the optional RDR2-Coop-ScriptHookDotNet download into this same folder, then run this again.")
    (game / "RDR2Coop.asi").unlink(missing_ok=True)   # only one copy of the mod may run
    shutil.rmtree(game / "RDR2Coop", ignore_errors=True)
    (game / "scripts").mkdir(exist_ok=True)
    shutil.copy2(src / "RDR2Coop.dll", game / "scripts/RDR2Coop.dll")
    write_ini(game, src / "RDR2Coop.ini", settings)


def write_ini(game, default_ini, settings):
    scripts = game / "scripts"
    scripts.mkdir(exist_ok=True)
    ini = scripts / "RDR2Coop.ini"
    if not ini.exists() or "Server=" not in ini.read_text(errors="replace"):
        shutil.copy2(default_ini, ini)
    set_ini(ini, settings)
    say(f"  co-op mod installed; settings in {ini}")


def enrage_in(path, base, work):
    """The folder holding the official enRage inside a zip or folder (a zip is
    unpacked into `work`), or None with the reason said."""
    if path.is_file() and zipfile.is_zipfile(path):
        with zipfile.ZipFile(path) as zf:
            zf.extractall(work)
        path = Path(work)
    if not path.is_dir():
        return None
    for dll in path.rglob("NemesisMod.dll"):
        if sha256(dll) == base:
            return dll.parent
        say(f"  {dll} is a different enRage version")
    return None


def copy_official_enrage(scripts, base, version):
    """Copies the player's official enRage into the game: from the installer's
    own folder if they put it there (zip or unpacked), else from a path they give."""
    with tempfile.TemporaryDirectory() as t:
        src = None
        for i, z in enumerate(sorted(HERE.glob("*.zip"))):
            src = src or enrage_in(z, base, Path(t) / str(i))
        src = src or enrage_in(HERE, base, t)
        if src:
            say("  using the enRage found in the installer's folder")
        else:
            say(f"\n  The co-op patch needs your own copy of enRage {version} from Nexus:")
            say(f"  https://www.nexusmods.com/reddeadredemption2/mods/10967 (Files tab, version {version})")
            say("  (or put the zip in this installer's folder and run it again)")
            guesses = sorted(glob.glob(str(Path.home() / "Downloads" / "*[eE]n[rR][aA][gG][eE]*.zip")) +
                             glob.glob(str(Path.home() / "Downloads" / "*Nemesis*.zip")))
            answer = ask("  Path to the enRage zip (or its unpacked folder)", guesses[-1] if guesses else "")
            src = enrage_in(Path(answer).expanduser(), base, Path(t) / "asked") if answer else None
        if not src:
            say(f"  no enRage {version} found: enRage left as it is")
            return False
        dll = scripts / "NemesisMod.dll"
        if dll.is_file():   # some other enRage version: kept, not lost
            shutil.copy2(dll, scripts / "NemesisMod.dll.before-coop")
        # Only enRage's own files, never whatever else sits next to it.
        for f in src.iterdir():
            target = scripts / f.name
            if f.name in ("NemesisMod.dll", "Newtonsoft.Json.dll") or (f.suffix == ".ini" and not target.exists()):
                shutil.copy2(f, target)
    return True


def install_enrage_patch(scripts):
    """enRage + the co-op patch, built from the player's own official enRage."""
    info = dict(line.split("=", 1) for line in (PATCH_DIR / "patch.txt").read_text().split() if "=" in line)
    base, patched, version = info["base_sha256"], info["patched_sha256"], info["enrage_version"]
    say(f"  enRage co-op patch {info.get('patch_version', '?')}")
    dll = scripts / "NemesisMod.dll"
    official = scripts / "NemesisMod.dll.official"

    current = sha256(dll) if dll.is_file() else None
    if current == patched:
        say("  enRage already has the co-op patch")
        return True
    if current != base and official.is_file() and sha256(official) == base:
        current = base
        shutil.copy2(official, dll)
    if current != base and not copy_official_enrage(scripts, base, version):
        return False
    shutil.copy2(dll, official)
    new = apply_patch(dll.read_bytes(), (PATCH_DIR / "NemesisMod.coop.patch").read_bytes())
    if hashlib.sha256(new).hexdigest() != patched:
        say("  the patched enRage didn't come out right: enRage left as the official version")
        return False
    dll.write_bytes(new)
    # Opening enRage's menu would otherwise freeze the host's world for everyone.
    nem_ini = scripts / "NemesisMod.ini"
    if nem_ini.exists():
        set_ini(nem_ini, {"PauseWorld": "False"})
    else:   # enRage writes the rest of its settings around this on first start
        nem_ini.write_text("[Menu]\nPauseWorld=False\n")
    say(f"  enRage {version} + unofficial co-op patch installed (official copy kept as {official.name})")
    return True


def uninstall(game):
    scripts = game / "scripts"
    (game / "RDR2Coop.asi").unlink(missing_ok=True)
    shutil.rmtree(game / "RDR2Coop", ignore_errors=True)
    for f in ("RDR2Coop.dll", "RDR2Coop.ini", "RDR2Coop.log", "RDR2Coop.Host.log", "RDR2Coop.Core.log"):
        (scripts / f).unlink(missing_ok=True)
    off = scripts / "NemesisMod.dll.off-for-coop"
    if off.is_file():
        shutil.move(off, scripts / "NemesisMod.dll")
        say("  enRage switched back on")
    official = scripts / "NemesisMod.dll.official"   # last: it wins over a patched copy
    before = scripts / "NemesisMod.dll.before-coop"   # a different enRage version they had
    if official.is_file():
        shutil.move(official, scripts / "NemesisMod.dll")
        say("  official enRage put back")
    if before.is_file():
        shutil.move(before, scripts / "NemesisMod.dll")
        say("  your previous enRage version put back")
    say("  co-op mod removed (ScriptHookRDR2 and ScriptHookRDR2DotNet are left for your other mods)")


# ---------------------------------------------------------------- main
def pick_game():
    games = find_games()
    if not games:
        path = Path(ask("RDR2 not found in your Steam libraries. Path to the 'Red Dead Redemption 2' folder")).expanduser()
        if not (path / "RDR2.exe").is_file():
            fail("RDR2.exe isn't in that folder.")
        return path.parents[2], path
    if len(games) > 1:
        for i, lib in enumerate(games, 1):
            say(f"  {i}. {lib}")
        lib = games[int(ask("Which Steam library", "1")) - 1]
    else:
        lib = games[0]
    return lib, lib / "steamapps/common/Red Dead Redemption 2"


def main():
    version = (HERE / "VERSION.txt").read_text().strip() if (HERE / "VERSION.txt").is_file() else "?"
    say(f"RDR2 Co-op {version} installer\n")
    lib, game = pick_game()
    say(f"Game: {game}\n")

    if "--uninstall" in sys.argv:
        uninstall(game)
        return

    say("A few questions (Enter keeps the suggestion):")
    name = ask("Your name, shown to the other players", getpass.getuser())
    run_server = yes("Run the co-op server in your game (friends connect to you; no separate server needed)?", default=False)
    server = "127.0.0.1" if run_server else ""
    while not server:
        server = ask("Co-op server address (ask whoever runs the server)")
    port = ask("Server port", "27460")
    who = "pick one and tell your friends" if run_server else "ask whoever runs the server"
    while True:   # typed blind, so twice
        password = getpass.getpass(f"Room password ({who}; not shown as you type): ").strip()
        if password and password == getpass.getpass("Room password again: ").strip():
            break
        say("  The two didn't match (or were empty); try again.")
    host = yes("Do you usually host (your story, missions and enRage world are the shared ones)?", default=run_server)
    public = run_server and yes("List your game in the online server browser (anyone can find it; they still need the password)?", default=False)

    settings = {"Name": name, "Server": server, "Port": port, "Password": password,
                "PreferHost": "Yes" if host else "No", "RunServer": "Yes" if run_server else "No",
                "Public": "Yes" if public else "No"}
    scripts = game / "scripts"
    nem = scripts / "NemesisMod.dll"
    # The enRage link only works in the ScriptHookRDR2DotNet version of the mod, so that question comes first.
    use_enrage = host and PATCH_DIR.is_dir() and yes(   # the optional enRage co-op patch download is unpacked here
        "Install enRage (Nexus mod 10967) with the unofficial co-op patch? This uses the ScriptHookRDR2DotNet version of co-op, "
        "which needs .NET Framework 4.8 in the game's Proton prefix (10-20 minutes to build if missing)", default=nem.is_file())

    say("\nInstalling:")
    install_scripthook(game)
    if use_enrage:
        prefix = lib / "steamapps/compatdata" / APP_ID
        if not dotnet48_ready(prefix):
            say("The game's Proton prefix doesn't have .NET Framework 4.8 yet (the ScriptHookRDR2DotNet version needs it).")
            if prefix.exists():
                fail(f"Move {prefix} away (it's the game's Wine folder; it is rebuilt), then run this again.\n"
                     "Your story saves are in Rockstar's cloud / the Profiles folder in there: back that up first.")
            if yes("Build it now with prepare-prefix.sh (10-20 minutes; needs GE-Proton10-34 and Proton Experimental; winetricks is fetched if missing)?"):
                subprocess.run([str(HERE / "prepare-prefix.sh"), str(lib)], check=True)
            else:
                fail("Run ./prepare-prefix.sh <steam library> before playing, then this again.")
        install_shdn(game)
        install_coop_shdn(game, settings)
    else:
        install_coop(game, settings)
    if run_server:
        say(f"  The server starts with your game and opens UDP {port} on your router by itself (UPnP);\n"
            "  F10 in game shows the address to give your friends. No UPnP on your router: forward UDP\n"
            f"  {port} to this PC by hand.")

    if use_enrage:
        install_enrage_patch(scripts)
    elif host and nem.is_file() and not PATCH_DIR.is_dir():
        say("\nYou have enRage: the optional enRage co-op patch (a separate download) shares its world with your guests.")
    elif nem.is_file():
        say("\nenRage runs on the host only for now (a second copy would grow its own separate world).")
        if yes("Switch your enRage off while you play co-op as a guest?"):
            shutil.move(nem, scripts / "NemesisMod.dll.off-for-coop")
            say("  enRage switched off (setup.py --uninstall switches it back on)")

    say("\nDone. In Steam, for Red Dead Redemption 2:")
    say("  Properties > General > Launch options:  WINEDLLOVERRIDES=\"dinput8=n,b\" %command%")
    say("  Properties > Compatibility: Proton Experimental")
    say("In game: Insert reloads the mod, F10 shows co-op status.")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\ncancelled")
