RDR2 Co-op - play Red Dead Redemption 2's story with friends (unofficial mod)
==========================================================================

You need:
  - RDR2 (Steam, Rockstar Games Launcher or Epic).
  - ScriptHookRDR2 (https://www.dev-c.com/rdr2/scripthookrdr2/) and
    ScriptHookRDR2DotNet V2 (https://github.com/Halen84/ScriptHookRDR2DotNet-V2/releases).
    Most script-mod users have both already (enRage needs them too); the Linux
    installer sets them up if not.
  - A co-op server: one person in your group runs it (the "Server" download,
    see RUNNING THE SERVER below) and gives everyone its address, port and
    room password.

INSTALL (Windows and Linux, 5 minutes)
  1. ScriptHookRDR2 (https://www.dev-c.com/rdr2/scripthookrdr2/): put
     dinput8.dll and ScriptHookRDR2.dll from its bin folder in the game folder
     (the one with RDR2.exe).
  2. ScriptHookRDR2DotNet V2 (link above): put ScriptHookRDRDotNet.asi,
     ScriptHookRDRDotNet.ini and ScriptHookRDRNetAPI.dll in the game folder.
  3. Put RDR2Coop.dll and RDR2Coop.ini from this download's scripts folder in
     the game's scripts folder (make it if it isn't there).
  4. Edit scripts/RDR2Coop.ini: Name, Server, Port, Password, and
     PreferHost=Yes if you usually host.
  5. Linux only (Steam + Proton):
     Steam > Red Dead Redemption 2 > Properties:
       Launch options:  WINEDLLOVERRIDES="dinput8=n,b" %command%
       Compatibility:   Proton Experimental
     and, if script mods have never run in this game, .NET Framework 4.8 in
     its Proton prefix:  protontricks 1174180 dotnet48
  Linux users can instead use the optional "Linux installer" download
  (python3 setup.py), which does all of this and asks for the settings.

RUNNING THE SERVER (one person per group)
  1. Unpack the "Server" download anywhere and install Python 3 (python.org;
     on Windows tick "Add python.exe to PATH"). Nothing else is needed.
  2. In a terminal in the server folder:
       Windows:        py relay.py --password YOUR-ROOM-PASSWORD
       Linux / macOS:  python3 relay.py --password YOUR-ROOM-PASSWORD
     Leave that window open while you play. It uses UDP port 27460.
     Windows: when Windows Firewall asks about Python the first time, click
     Allow (tick Public as well if friends join over the internet).
  3. Friends outside your home: forward UDP port 27460 on your router to that
     computer and give them your public IP address. On the same home network,
     use the computer's local IP.
  4. Everyone (the host too) puts the address, 27460 and the password in
     scripts/RDR2Coop.ini. Same computer as the server: Server=127.0.0.1
  More (running it as a Linux service, VPS, security): server/README-SERVER.md

IN GAME
  Load your story save; a subtitle says when you've joined.
  Insert reloads the mod, F10 shows co-op status, F8 starts a mission party,
  F7 revives a downed partner, F9 teleports to your partner, F11 (guests) lists the host's enRage nemeses who have a grudge against you.

ENRAGE (optional, host only)
  The unofficial enRage co-op patch is a separate download: unpack it in the
  same place, put the official enRage 0.9.5.1 zip there too and run the Linux
  installer (see README-ENRAGE-PATCH.txt). It can't be installed by hand: it
  has to be applied to your own enRage file, which only setup.py does.

UNINSTALL
  Windows: delete RDR2Coop.dll and RDR2Coop.ini from the game's scripts folder.
  Linux:   python3 setup.py --uninstall (also puts your official enRage back).

Credits: ScriptHookRDR2 by Alexander Blade; ScriptHookRDR2DotNet V2 by crosire
and Halen84. Neither is included; get them from the links above.
The optional enRage co-op patch is unofficial and needs your own enRage
download; all credit for enRage to its author.
