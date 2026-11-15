#!/usr/bin/env python3
"""Read the beta log uploads (log-server): list today's and yesterday's (UTC), or fetch one.
  tools/fetch-logs.py                 list
  tools/fetch-logs.py --days N        list the last N days (UTC, today included)
  tools/fetch-logs.py <key> [out]     download one (to out, default ./logs/<basename>)
Needs ~/.config/rdr2-coop/logs-read-token (mode 600)."""
import datetime, json, os, sys, urllib.request, urllib.parse
URL = "https://rdr2coop-logs.beese.workers.dev"
TOKEN = open(os.path.expanduser("~/.config/rdr2-coop/logs-read-token")).read().strip()
def get(path):
    return urllib.request.urlopen(urllib.request.Request(URL + path, headers={"Authorization": "Bearer " + TOKEN, "User-Agent": "rdr2coop-fetch-logs/1"}), timeout=60).read()
def objects(days):
    today = datetime.datetime.now(datetime.timezone.utc).date()
    for d in range(days - 1, -1, -1):   # oldest day first, as before
        cursor = ""
        while True:
            page = json.loads(get(f"/v1/list?prefix=logs/{today - datetime.timedelta(days=d)}/" + (f"&cursor={urllib.parse.quote(cursor)}" if cursor else "")))
            yield from page["objects"]
            if not (cursor := page.get("cursor")): break
if len(sys.argv) < 2 or sys.argv[1] == "--days":
    for o in objects(int(sys.argv[2]) if len(sys.argv) > 2 else 2):
        m = o.get("meta") or {}
        print(f'{o["uploaded"][:16]} {o["size"]:>8}  {o["key"]}  [{m.get("name","")} {m.get("build","")} {m.get("why","")}]')
else:
    key = sys.argv[1]; out = sys.argv[2] if len(sys.argv) > 2 else os.path.join("logs", os.path.basename(key))
    os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
    open(out, "wb").write(get("/v1/get?key=" + urllib.parse.quote(key, safe=""))); print("saved", out)
