RDR2 Co-op - Windows version (with the installer)
=================================================
1. Unpack this zip anywhere (not inside the game folder).
2. Double-click RDR2CoopSetup.exe. It finds the game (Steam, Rockstar or Epic),
   installs ScriptHookRDR2 (downloaded from dev-c.com, checksum-checked) if
   it's missing, then the co-op mod with its own .NET (nothing else to
   install), and asks your name, the server, its port, the room password and
   whether you usually host. An older copy of the mod (scripts\RDR2Coop.dll)
   is removed; your settings are kept.
   Windows may warn that the app is from an unknown publisher (it isn't
   signed): click "More info" then "Run anyway". The source is on GitHub.
   If you usually host and have enRage 0.9.5.2 (Nexus mod 10967), put its
   official zip in this folder first (or have it installed in the game): the
   installer offers the unofficial enRage co-op patch (enrage-coop-patch
   folder; see README-ENRAGE-PATCH.txt). That version of co-op runs under
   ScriptHookRDR2DotNet V2 (included, see scripthookdotnet\LICENSE.txt), which
   the installer puts in place too.
3. Load a story save; a subtitle says when you've joined.

Run RDR2CoopSetup.exe again to change settings or to uninstall.
Server for your group: see "RUNNING THE SERVER" in README-PLAYERS.txt (the
server folder is included). Install by hand instead: README-PLAYERS.txt.
