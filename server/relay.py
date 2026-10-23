#!/usr/bin/env python3
"""RDR2 co-op relay server.

Every game connects here; the host's game stays the only real world. The
server only routes packets (it never reads positions or NPC data):

  State  -> every other player
  World  -> every other player (each game runs the area around its own player;
            the clients decide whose NPCs to show)
  Hit    -> the player named in `to` (the game that owns the NPC that was hit,
            or the player whose character an NPC hit)
  Party  -> every other player (mission party invites and answers)
  Appearance -> every other player (a player's clothes)
  Objectives -> every other player (where the host's story mission wants players to go)
  Action -> the player named in `to` (something done to their NPC, e.g. a hogtie)
  Notice -> one player (`to`) or everyone else (enRage news, nemesis lists)
  EntityLook -> every other player (an NPC's or horse's full look)
  Roster -> sent by the server whenever someone joins or leaves

Joining: Hello(version, prefer_host, name) -> Challenge(nonce) -> Auth(HMAC-SHA256
of the nonce with the room password) -> Welcome(your id, host id). Roles are
automatic: whoever joins an empty-hosted room hosts (their game is the world),
everyone else is a guest; a guest who prefers hosting is promoted when the host
leaves. The server stamps the sender id on every packet it forwards.

  python3 relay.py --password-file password.txt [--port 27460] [--bind 0.0.0.0]
                   [--name "Room name"] [--public --list-server URL]

--public lists the room in the in-game server browser's online list (it
re-announces every minute and drops off a few minutes after the relay stops).

Python 3.6+, no other dependencies; Linux, Windows or macOS. See README-SERVER.md.
"""
import argparse
import hashlib
import hmac
import json
import os
import signal
import socket
import struct
import sys
import threading
import time
import urllib.error
import urllib.request

MAGIC = b"RDCO"
VERSION = 13
MAX_PLAYERS = 4
TIMEOUT_S = 10.0
CHALLENGE_TTL_S = 10.0
MAX_PENDING = 256      # half-open logins held at once
BROADCAST = 255
MAX_PACKET = 1400
MAX_NAME = 32
# Server browser, home network: a game broadcasts a query (QUERY, padded so the
# answer is never bigger than the question: no use for amplification attacks)
# and servers answer ANSWER + protocol version, players, max players, room name.
QUERY, ANSWER, QUERY_SIZE = b"RDCQ", b"RDCA", 160
DEFAULT_LIST_SERVER = "https://rdr2coop-rooms.beese.workers.dev"   # the online room list (list-server/)

HELLO, CHALLENGE, AUTH, WELCOME, REJECT, STATE, BYE, WORLD, HIT, ROSTER, PARTY, APPEARANCE, OBJECTIVES, ACTION, NOTICE, ENTITY_LOOK = range(1, 17)
GUEST, HOST = 0, 1


def header(ptype, sender, to):
    return MAGIC + bytes((ptype, sender, to))


def short_string(b):
    b = b[:MAX_NAME]
    return bytes((len(b),)) + b


def read_string(data, pos):
    n = data[pos]
    s = data[pos + 1:pos + 1 + n]
    if len(s) != n:
        raise ValueError("truncated string")
    return s.decode("utf-8", "replace"), pos + 1 + n


class Player:
    def __init__(self, pid, addr, name, role, prefer_host, now):
        self.id, self.addr, self.name, self.role = pid, addr, name, role
        self.prefer_host = prefer_host
        self.last_heard = now


class Relay:
    """All routing logic, with no sockets, so tests can drive it directly.
    handle() returns a list of (bytes, addr) to send."""

    def __init__(self, password, clock=time.monotonic, log=print, name="RDR2 Co-op server"):
        self.key = password.encode()
        self.name = name
        self.clock = clock
        self.log = log
        self.players = {}        # addr -> Player
        self.pending = {}        # addr -> (nonce, role, name, expires)
        self.hello_times = {}    # ip -> recent hello timestamps
        self.last_sweep = 0.0

    # -- helpers ---------------------------------------------------------
    def host(self):
        return next((p for p in self.players.values() if p.role == HOST), None)

    def free_id(self):
        used = {p.id for p in self.players.values()}
        return next(i for i in range(1, 255) if i not in used)

    def roster_packet(self):
        body = bytes((len(self.players),))
        for p in sorted(self.players.values(), key=lambda p: p.id):
            body += bytes((p.id, p.role)) + short_string(p.name.encode())
        return header(ROSTER, 0, BROADCAST) + body

    def to_all(self, data, except_addr=None):
        return [(data, p.addr) for p in self.players.values() if p.addr != except_addr]

    def reject(self, addr, reason):
        return [(header(REJECT, 0, 0) + short_string(reason.encode()), addr)]

    def rate_limited(self, ip, now):
        times = [t for t in self.hello_times.get(ip, []) if now - t < 10]
        times.append(now)
        self.hello_times[ip] = times
        return len(times) > 20

    # -- main entry ------------------------------------------------------
    def handle(self, data, addr):
        now = self.clock()
        if len(data) >= QUERY_SIZE and data[:4] == QUERY:
            return [(ANSWER + struct.pack("<iBB", VERSION, len(self.players), MAX_PLAYERS) + short_string(self.name.encode()), addr)]
        if len(data) < 7 or len(data) > MAX_PACKET or data[:4] != MAGIC:
            return []
        ptype, to = data[4], data[6]
        body = data[7:]
        player = self.players.get(addr)

        if player is None:
            try:
                if ptype == HELLO:
                    return self.on_hello(body, addr, now)
                if ptype == AUTH:
                    return self.on_auth(body, addr, now)
            except (ValueError, IndexError, struct.error):
                pass
            return []   # strangers get nothing else

        player.last_heard = now
        if ptype == HELLO:
            # Our Welcome was lost; say it again.
            return [(self.welcome(player), addr)]
        if ptype == BYE:
            return self.drop(player, "left")
        if ptype not in (STATE, WORLD, HIT, PARTY, APPEARANCE, OBJECTIVES, ACTION, NOTICE, ENTITY_LOOK):
            return []

        out = header(ptype, player.id, to) + body
        if ptype in (STATE, WORLD, PARTY, APPEARANCE, OBJECTIVES, ENTITY_LOOK) or (ptype == NOTICE and to == BROADCAST):
            return self.to_all(out, except_addr=addr)
        # HIT, ACTION: to the one player named in `to`
        target = next((p for p in self.players.values() if p.id == to and p is not player), None)
        return [(out, target.addr)] if target else []

    def on_hello(self, body, addr, now):
        # Spoofed-address floods: per-address limit, and a cap on half-open logins.
        if self.rate_limited(addr[0], now) or (addr not in self.pending and len(self.pending) >= MAX_PENDING):
            return []
        version, role = struct.unpack_from("<iB", body, 0)
        name, _ = read_string(body, 5)
        name = "".join(ch for ch in name if ch.isprintable())[:32]   # names go into logs and rosters
        if version != VERSION:
            return self.reject(addr, f"version mismatch: server {VERSION}, you {version}")
        nonce = os.urandom(16)
        self.pending[addr] = (nonce, role == 1, name or "player", now + CHALLENGE_TTL_S)
        return [(header(CHALLENGE, 0, 0) + nonce, addr)]

    def on_auth(self, body, addr, now):
        pend = self.pending.pop(addr, None)
        if pend is None or now > pend[3] or len(body) != 32:
            return []
        nonce, prefer_host, name, _ = pend
        if not hmac.compare_digest(body, hmac.new(self.key, nonce, hashlib.sha256).digest()):
            self.log(f"wrong password from {addr[0]}")
            return self.reject(addr, "wrong password")
        if len(self.players) >= MAX_PLAYERS:
            return self.reject(addr, "server full (4 players)")
        role = GUEST if self.host() else HOST
        p = Player(self.free_id(), addr, name, role, prefer_host, now)
        self.players[addr] = p
        self.log(f"{name} joined as {'host' if role == HOST else 'guest'} (id {p.id}) from {addr[0]}")
        return [(self.welcome(p), addr)] + self.to_all(self.roster_packet())

    def welcome(self, p):
        h = self.host()
        return header(WELCOME, 0, p.id) + struct.pack("<iBB", VERSION, p.id, h.id if h else 0)

    def drop(self, player, why):
        self.players.pop(player.addr, None)
        self.log(f"{player.name} (id {player.id}) {why}")
        if player.role == HOST and self.players:
            # Hand the world to whoever prefers hosting, else the longest-joined guest.
            nxt = min(self.players.values(), key=lambda p: (not p.prefer_host, p.id))
            nxt.role = HOST
            self.log(f"{nxt.name} (id {nxt.id}) is now the host")
        return self.to_all(self.roster_packet())

    def expire(self):
        now = self.clock()
        if now - self.last_sweep < 1.0:   # a flood mustn't make every packet scan everything
            return []
        self.last_sweep = now
        for ip in [ip for ip, ts in self.hello_times.items() if now - ts[-1] >= 10]:
            del self.hello_times[ip]
        out = []
        for p in [p for p in self.players.values() if now - p.last_heard > TIMEOUT_S]:
            out += self.drop(p, "timed out")
        for addr in [a for a, v in self.pending.items() if now > v[3]]:
            del self.pending[addr]
        return out


def serve(port, password, bind="0.0.0.0", name="RDR2 Co-op server", list_server=None):
    relay = Relay(password, log=lambda m: print(time.strftime("%H:%M:%S"), m, flush=True), name=name)
    stop = threading.Event()
    if list_server:   # player counts are read without a lock: a stale count for a minute is fine
        threading.Thread(target=keep_listed, args=(list_server, name, port, relay, stop), daemon=True).start()
    try:
        run(relay, port, bind)
    finally:
        stop.set()
        if list_server:
            announce(list_server, "/v1/remove", {"port": port})


def run(relay, port, bind):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind((bind, port))
    sock.settimeout(1.0)
    print(f"relay on udp/{port}, protocol {VERSION}", flush=True)
    while True:
        try:
            data, addr = sock.recvfrom(2048)
            out = relay.handle(data, addr)
        except socket.timeout:
            out = []
        except ConnectionResetError:
            continue
        for pkt, to in out + relay.expire():
            try:
                sock.sendto(pkt, to)
            except OSError:
                pass


def announce(list_server, path, body):
    """POST to the online room list; None when done, else why not."""
    req = urllib.request.Request(list_server.rstrip("/") + path, data=json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=10):
            return None
    except urllib.error.HTTPError as e:
        return e.read().decode(errors="replace")[:200]
    except OSError as e:
        return str(e)


def keep_listed(list_server, name, port, relay, stop):
    last = ""
    while True:
        err = announce(list_server, "/v1/announce", {"name": name, "port": port, "players": len(relay.players),
                                                      "max": MAX_PLAYERS, "version": VERSION})
        if err != last:
            print(time.strftime("%H:%M:%S"), f"listed online as '{name}'" if err is None else f"couldn't list online: {err}", flush=True)
            last = err
        if stop.wait(60):
            return


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--port", type=int, default=27460, help="UDP port (default 27460)")
    ap.add_argument("--bind", default="0.0.0.0", help="address to listen on (default: all)")
    ap.add_argument("--name", default="RDR2 Co-op server", help="room name in the server browser")
    ap.add_argument("--public", action="store_true", help="list the room in the online server browser")
    ap.add_argument("--list-server", default=DEFAULT_LIST_SERVER, help="online room list URL")
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--password")
    g.add_argument("--password-file")
    a = ap.parse_args()
    password = a.password if a.password is not None else open(a.password_file).read().strip()
    if not password:
        sys.exit("empty password")
    if a.public and not a.list_server:
        sys.exit("--public needs --list-server URL (the online room list)")
    signal.signal(signal.SIGTERM, lambda *_: sys.exit(0))   # service stop: still leave the online list
    serve(a.port, password, a.bind, a.name, a.list_server if a.public else None)


if __name__ == "__main__":
    main()
