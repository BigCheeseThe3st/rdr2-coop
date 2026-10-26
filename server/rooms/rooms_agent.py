#!/usr/bin/env python3
"""Dedicated co-op servers on demand (the author, 24 Oct: a few dedicated sessions for people who can't host,
started from the Discord's #find-a-partner with their own password).

Runs on the Pi as a user service. Every few seconds it asks the sessions worker (Cloudflare) for
jobs, which come from the Discord buttons: start a room with the name and password the player chose,
or stop one. Each room is relay.py as the user unit rdr2coop-room@<port>, listed in the in-game
browser. Rooms nobody is in for idle_minutes, or that ran max_hours, are stopped. Everything it knows
goes back to the worker, which tells the player and keeps the list in Discord up to date.
Only outgoing HTTPS: nothing reaches this Pi from the internet except the room ports themselves.

Config (~/.config/rdr2coop-rooms/config.json, mode 600):
  {"url": "https://...workers.dev", "secret": "...", "ports": [27461, 27462, 27463, 27464],
   "idle_minutes": 30, "max_hours": 12}
"""
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request

HOME = os.path.expanduser("~")
CONF_DIR = os.path.join(HOME, ".config", "rdr2coop-rooms")
STATUS_DIR = os.path.join(HOME, ".cache", "rdr2coop-rooms")
STATE = os.path.join(CONF_DIR, "rooms.json")   # port -> {owner, owner_name, name, started, job}
MIN_FREE_MB = 200   # the Pi also stands in for the trading Pi: no new room below this much free memory


def load(path, default):
    try:
        with open(path) as f:
            return json.load(f)
    except (OSError, ValueError):
        return default


def save(path, data):
    tmp = path + ".tmp"
    with open(tmp, "w") as f:
        json.dump(data, f)
    os.replace(tmp, path)


def clean_name(name):
    """What the in-game browser shows: printable, no game formatting codes, short."""
    name = re.sub(r"[~<>\x00-\x1f\x7f]", "", str(name or "")).strip()
    return name[:30] or "Co-op room"


def write_room_files(port, name, password):
    os.makedirs(CONF_DIR, exist_ok=True)
    old = os.umask(0o077)
    try:
        with open(os.path.join(CONF_DIR, f"{port}.pass"), "w") as f:
            f.write(password)
        with open(os.path.join(CONF_DIR, f"{port}.env"), "w") as f:
            # systemd EnvironmentFile: one line, the name quoted (the unit passes it as one argument)
            f.write('ROOM_NAME="%s (dedicated)"\n' % clean_name(name).replace('"', "'"))
    finally:
        os.umask(old)


def remove_room_files(port):
    for ext in ("pass", "env"):
        try:
            os.remove(os.path.join(CONF_DIR, f"{port}.{ext}"))
        except OSError:
            pass


def unit(action, port):
    return subprocess.run(["systemctl", "--user", action, f"rdr2coop-room@{port}.service"],
                          capture_output=True, text=True, timeout=30)


def running(port):
    return unit("is-active", port).stdout.strip() == "active"


def room_status(port):
    return load(os.path.join(STATUS_DIR, f"{port}.json"), {})


def free_mb():
    with open("/proc/meminfo") as f:
        return next(int(line.split()[1]) // 1024 for line in f if line.startswith("MemAvailable:"))


def start(job, rooms, ports):
    password = str(job.get("password") or "")
    if not 4 <= len(password) <= 64:
        return {"id": job["id"], "ok": False, "why": "password must be 4 to 64 characters"}
    owner = str(job.get("owner") or "")
    if any(r.get("owner") == owner for r in rooms.values()):
        return {"id": job["id"], "ok": False, "why": "you already have a dedicated server running"}
    free = [p for p in ports if str(p) not in rooms and not running(p)]
    if not free:
        return {"id": job["id"], "ok": False, "why": "all dedicated servers are in use"}
    if free_mb() < MIN_FREE_MB:
        return {"id": job["id"], "ok": False, "why": "the server machine is short of memory right now, try again later"}
    port = free[0]
    write_room_files(port, job.get("name"), password)
    res = unit("start", port)
    if res.returncode != 0:
        remove_room_files(port)
        return {"id": job["id"], "ok": False, "why": "the server didn't start: " + (res.stderr or res.stdout).strip()[:200]}
    rooms[str(port)] = {"owner": owner, "owner_name": str(job.get("owner_name") or "")[:40],
                        "name": clean_name(job.get("name")), "started": time.time(), "job": job["id"]}
    return {"id": job["id"], "ok": True, "port": port}


def stop(port, rooms, why):
    unit("stop", port)
    remove_room_files(port)
    room = rooms.pop(str(port), None)
    if room:
        print(f"stopped room {port} ({room.get('name')}): {why}", flush=True)
    return room


def housekeeping(rooms, conf):
    """Rooms that ended on their own, sat empty too long or ran out of time."""
    ended = []
    now = time.time()
    for port in list(rooms):
        st = room_status(port)
        empty = st.get("empty_since")
        if not running(port):
            ended.append((port, stop(port, rooms, "ended"), "ended"))
        elif empty and now - empty > conf.get("idle_minutes", 30) * 60 and now - rooms[port]["started"] > 120:
            ended.append((port, stop(port, rooms, "nobody in it"), "idle"))
        elif now - rooms[port]["started"] > conf.get("max_hours", 12) * 3600:
            ended.append((port, stop(port, rooms, "time up"), "time"))
    return ended


def call(conf, method, path, body=None):
    req = urllib.request.Request(conf["url"].rstrip("/") + path, method=method,
                                 data=None if body is None else json.dumps(body).encode(),
                                 headers={"Authorization": "Bearer " + conf["secret"], "Content-Type": "application/json",
                                          "User-Agent": "rdr2coop-rooms-agent/1"})
    with urllib.request.urlopen(req, timeout=20) as f:
        return json.loads(f.read() or b"{}")


def main():
    conf = load(os.path.join(CONF_DIR, "config.json"), None)
    if not conf or not conf.get("url") or not conf.get("secret"):
        sys.exit("no config at ~/.config/rdr2coop-rooms/config.json")
    ports = [int(p) for p in conf.get("ports", [27461, 27462, 27463, 27464])]
    os.makedirs(STATUS_DIR, exist_ok=True)
    rooms = load(STATE, {})
    print(f"rooms agent: ports {ports}, {len(rooms)} room(s) carried over", flush=True)
    last_err = ""
    while True:
        try:
            results = []
            for job in call(conf, "GET", "/v1/agent/poll").get("jobs", []):
                if job.get("action") == "start":
                    results.append(start(job, rooms, ports))
                elif job.get("action") == "stop" and str(job.get("port")) in rooms:
                    stop(job["port"], rooms, "stopped by its owner")
                    results.append({"id": job["id"], "ok": True, "port": job["port"]})
                else:
                    results.append({"id": job.get("id"), "ok": False, "why": "nothing to stop"})
            ended = housekeeping(rooms, conf)
            save(STATE, rooms)
            report = {"results": results,
                      "ended": [{"port": int(p), "owner": (r or {}).get("owner"), "name": (r or {}).get("name"), "why": w} for p, r, w in ended],
                      "rooms": [dict(port=int(p), owner=r["owner"], owner_name=r["owner_name"], name=r["name"], started=r["started"],
                                     players=room_status(p).get("players", 0), names=room_status(p).get("names", []))
                                for p, r in rooms.items()],
                      "capacity": len(ports)}
            call(conf, "POST", "/v1/agent/report", report)
            if last_err:
                print("worker reachable again", flush=True)
                last_err = ""
        except (urllib.error.URLError, OSError, ValueError, subprocess.SubprocessError) as e:
            if str(e) != last_err:
                print(f"worker unreachable: {e}", flush=True)
                last_err = str(e)
        time.sleep(conf.get("poll_seconds", 10))


if __name__ == "__main__":
    main()
