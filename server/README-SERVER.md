# Running your own RDR2 co-op server

Most groups don't need this: the host's game can run the server itself
(`RunServer=Yes` in RDR2Coop.ini, see README-PLAYERS.txt). This separate server
is for an always-on room on a Pi, VPS or spare PC, which keeps running when the
host's game closes.

The server is a small relay: every player's game connects to it, and it passes
messages between them. It never runs the game itself, needs almost nothing
(about 20 MB of memory, under 1% of a Raspberry Pi's CPU, ~50 KB/s each way per
player), and holds no game data. One server = one room of up to 4 players with
one password.

## Quick start (any computer with Python 3.6+)

```
python3 relay.py --password pick-a-long-password      (Windows: py relay.py ...)
```

Or keep the password in a file: `python3 relay.py --password-file password.txt`.
Players on the same network find it in the in-game server browser (F10 twice).
`--name "Room name"` names it there; `--public --list-server URL` also lists it
in the browser's online list (anyone can see it and its address; they still
need the password).

It listens on UDP port 27460 (`--port` to change it). Leave the window open
while you play. Works on Linux, Windows and macOS.

**Windows:** install Python 3 from python.org (tick "Add python.exe to PATH"),
open a Command Prompt in the server folder (type `cmd` in Explorer's address
bar) and run `py relay.py --password pick-a-long-password`. The first time,
Windows Firewall asks about Python: click Allow (tick Public too if friends
join over the internet). Stop it with Ctrl+C or by closing the window.

## As a background service (Linux with systemd)

```
sudo ./setup-server.sh
```

It asks for the port and a password (or makes a strong one up), then:
- runs the relay as its own user `rdr2coop` with no login and no access to your
  files, restarting it after crashes and reboots;
- keeps the password in `/etc/rdr2coop/password` (readable only by root and the relay);
- opens the port in ufw or firewalld if you use one (it asks first);
- prints what to tell your players.

`sudo ./setup-server.sh --update` installs a newer `relay.py`;
`sudo ./setup-server.sh --uninstall` removes everything it added.

## Letting friends in over the internet

- **Home connection:** in your router, forward **UDP** port 27460 (or yours) to
  the machine running the relay. Give your friends your public IP address, or
  better a free dynamic DNS name (duckdns.org, noip.com) that follows your IP.
- **VPS / cloud machine:** no forwarding; just allow UDP 27460 in its firewall.
- **Only on your home network:** nothing to forward; players use the server's
  local IP.

Players put the address, port and password into the Linux installer
(`setup.py`) or `scripts/RDR2Coop.ini` (`Server=`, `Port=`, `Password=`).

## Security

- Use a long password (12+ random characters). Logins are rate-limited, but a
  short password can still be guessed, and anyone who gets in can affect the
  players' games (damage NPCs, move party members in missions).
- Only share the address and password with people you play with.
- The relay only routes packets: it doesn't read or store anything, and logs
  only joins, leaves and wrong passwords (`journalctl -u rdr2coop-relay`).
- Server and players must run the same co-op version (the server says
  "version mismatch" otherwise): update `relay.py` when you update the mod.
