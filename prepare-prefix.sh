#!/bin/sh
# Builds RDR2's Proton prefix before the game's first launch (none of this
# counts as Steam play time). Needs GE-Proton10-34 in
# ~/.local/share/Steam/compatibilitytools.d and Proton Experimental. winetricks is
# used if installed, else fetched (one script) next to this file: SteamOS (Steam
# Deck) doesn't ship it and its system folders are read-only.
#   ./prepare-prefix.sh <steam library>   e.g. ~/.local/share/Steam
# The recipe proven on 21 Oct 2026:
#  - .NET 4.8 is installed with GE-Proton10-34: Proton Experimental (Wine 11)
#    cannot run Microsoft's .NET installers (they exit with status 67);
#  - the Rockstar Games Launcher is installed with Proton Experimental: under
#    GE-Proton its installer fails ("make sure all instances ... closed");
#  - the game then runs under Proton Experimental (Steam's default).
set -e
LIB="${1:?usage: $0 <steam library, e.g. ~/.local/share/Steam>}"
GE="$HOME/.local/share/Steam/compatibilitytools.d/GE-Proton10-34"
C="$LIB/steamapps/compatdata/1174180"
EXP="$HOME/.local/share/Steam/steamapps/common/Proton - Experimental"
RGL="$LIB/steamapps/common/Red Dead Redemption 2/Redistributables/Rockstar-Games-Launcher.exe"
[ -x "$GE/proton" ] || { echo "GE-Proton10-34 not found in $GE"; exit 1; }
[ -x "$EXP/proton" ] || { echo "Proton Experimental not installed (Steam > Library > Tools)"; exit 1; }
[ -f "$RGL" ] || { echo "install RDR2 in $LIB first (do not launch it)"; exit 1; }
if [ -e "$C" ]; then
  echo "$C already exists; move it away first if you want a fresh prefix"; exit 1
fi
mkdir -p "$C"
STEAM_COMPAT_CLIENT_INSTALL_PATH="$HOME/.local/share/Steam" STEAM_COMPAT_DATA_PATH="$C" \
  SteamAppId=1174180 SteamGameId=1174180 PROTONFIXES_DISABLE=1 "$GE/proton" run wineboot -u
export WINEPREFIX="$C/pfx" WINE="$GE/files/bin/wine" WINESERVER="$GE/files/bin/wineserver" WINEDEBUG=-all
if ! command -v winetricks >/dev/null 2>&1; then
  HERE="$(cd "$(dirname "$0")" && pwd)"
  if [ ! -x "$HERE/winetricks" ]; then
    echo "winetricks isn't installed: fetching it from github.com/Winetricks/winetricks ..."
    curl -fsSL -o "$HERE/winetricks" https://raw.githubusercontent.com/Winetricks/winetricks/master/src/winetricks \
      || { echo "FAILED: couldn't download winetricks (no internet?)"; exit 1; }
    chmod +x "$HERE/winetricks"
  fi
  PATH="$HERE:$PATH"
fi
command -v cabextract >/dev/null 2>&1 || echo "note: cabextract isn't installed; if winetricks stops and asks for it, tell us"
echo "installing .NET Framework 4.8 (10-20 minutes)..."
winetricks -q --unattended dotnet48
winetricks -q win10
"$WINESERVER" -w
grep -q '"Release"=dword:00080eb1' "$WINEPREFIX/system.reg" || { echo "FAILED: .NET 4.8 not registered"; exit 1; }

echo "Rockstar Games Launcher setup is opening: click through it, keep the default folder."
( cd "$(dirname "$RGL")" && STEAM_COMPAT_CLIENT_INSTALL_PATH="$HOME/.local/share/Steam" STEAM_COMPAT_DATA_PATH="$C" \
  SteamAppId=1174180 SteamGameId=1174180 "$EXP/proton" run ./Rockstar-Games-Launcher.exe )
[ -f "$WINEPREFIX/drive_c/Program Files/Rockstar Games/Launcher/Launcher.exe" ] || { echo "FAILED: launcher not installed"; exit 1; }
echo "prefix ready: .NET 4.8, Windows 10, Rockstar launcher. Leave RDR2 on Proton Experimental in Steam."
