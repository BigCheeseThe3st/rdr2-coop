RDR2 Co-op - play Red Dead Redemption 2's story with friends (unofficial mod)
==========================================================================

You need: RDR2 (Steam, Rockstar Games Launcher or Epic), a co-op server
address, its port and its room password (from whoever runs the server, see
server/README-SERVER.md to run your own).

WINDOWS
  No installer: follow MANUAL INSTALL below (5 minutes).

LINUX (Steam + Proton)
  1. Unpack this folder anywhere.
  2. In a terminal here:  python3 setup.py
  3. Steam > Red Dead Redemption 2 > Properties:
       Launch options:  WINEDLLOVERRIDES="dinput8=n,b" %command%
       Compatibility:   Proton Experimental
  A brand new install needs .NET 4.8 in the game's prefix first: the
  installer offers to build it (prepare-prefix.sh).

MANUAL INSTALL (without the installer)
  1. ScriptHookRDR2 (https://www.dev-c.com/rdr2/scripthookrdr2/): put
     dinput8.dll and ScriptHookRDR2.dll from its bin folder in the game folder
     (the one with RDR2.exe).
  2. ScriptHookRDR2DotNet: put the three files from this download's
     scripthookdotnet folder (ScriptHookRDRDotNet.asi, ScriptHookRDRDotNet.ini,
     ScriptHookRDRNetAPI.dll) in the game folder.
  3. Put RDR2Coop.dll and RDR2Coop.ini from this download's scripts folder in
     the game's scripts folder (make it if it isn't there).
  4. Edit scripts/RDR2Coop.ini: Name, Server, Port, Password, and
     PreferHost=Yes if you usually host.
  5. Linux only: the Steam launch options above, and .NET Framework 4.8 in the
     game's Proton prefix (prepare-prefix.sh does it on a fresh install).
  The enRage co-op patch can't be installed by hand: it has to be applied to
  your own enRage file, which only the Linux installer (setup.py) does.
  To remove: delete RDR2Coop.dll and RDR2Coop.ini from the scripts folder.

IN GAME
  Load your story save; a subtitle says when you've joined.
  Insert reloads the mod, F10 shows co-op status, F8 starts a mission party,
  F7 revives a downed partner, F9 teleports to your partner, F11 (guests) lists the host's enRage nemeses who have a grudge against you.

ENRAGE (optional, host only)
  The unofficial enRage co-op patch is a separate download: unpack it into
  this folder, put the official enRage 0.9.5.1 zip here too and run the
  installer again (see README-ENRAGE-PATCH.txt).

UNINSTALL
  Windows: delete RDR2Coop.dll and RDR2Coop.ini from the game's scripts folder.
  Linux:   python3 setup.py --uninstall (also puts your official enRage back).

Credits: ScriptHookRDR2 by Alexander Blade (downloaded from dev-c.com by the
installer); ScriptHookRDR2DotNet V2 (zlib licence, see scripthookdotnet/).
The optional enRage co-op patch is unofficial and needs your own enRage
download; all credit for enRage to its author.
