// RDR2 Co-op room list: hosts who choose to be listed (Public=Yes) announce
// their room every minute; the in-game browser reads the list. Rooms that stop
// announcing drop off after TTL. The list lives in one Durable Object: kept in
// its storage (the object is shut down seconds after each request on the free
// plan, losing memory) and cached in memory while it runs.
//
//   POST /v1/announce {name, port, players, max, version, ipv4?}
//   POST /v1/remove   {port}
//   GET  /v1/rooms    -> {rooms: [{name, address, port, players, max, version}]}
//
// A room's address is the address the announcement came from, so nobody can
// list someone else's machine. shortcut: a host announcing over IPv6 sends its
// IPv4 (from its router) and that can't be checked; the worst a fake entry does
// is send a few small join packets somewhere when a player picks it. Upgrade:
// an IPv4-only hostname for announcements.

const TTL_MS = 180_000;
const MAX_ROOMS = 1000;
const MAX_PER_ADDRESS = 3;

export default {
  async fetch(request, env) {
    const stub = env.ROOMS.get(env.ROOMS.idFromName("rooms"));
    return stub.fetch(request);
  },
};

const json = (body, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json", "cache-control": "no-store" } });

const isIpv4 = (s) => typeof s === "string" && /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/.test(s) && s.split(".").every((n) => +n <= 255);

// Private, shared (CGNAT), loopback and link-local addresses can't be joined over the internet.
function isPublicIpv4(s) {
  if (!isIpv4(s)) return false;
  const [a, b] = s.split(".").map(Number);
  return !(a === 10 || a === 127 || a === 0 || a >= 224 || (a === 172 && b >= 16 && b < 32) || (a === 192 && b === 168) ||
           (a === 169 && b === 254) || (a === 100 && b >= 64 && b < 128));
}

// The one word kept out of room names (George: "bad-word filter for the N word please but nothing else"),
// seen through spaces, punctuation and 1/3/4/@ for i/e/a.
const slur = (s) => /nigg+(?:e+r|a|u+h|a+h|a+z)/.test(String(s ?? "").toLowerCase().replace(/[1!|]/g, "i").replace(/3/g, "e").replace(/[4@]/g, "a").replace(/[^a-z]/g, ""));
const cleanName = (s) => { const n = typeof s === "string" ? s.replace(/[\p{C}]/gu, "").trim().slice(0, 32) : ""; return slur(n) ? "Co-op room" : n; };
const int = (v, lo, hi) => (Number.isInteger(v) && v >= lo && v <= hi ? v : null);

export class RoomList {
  constructor(ctx) {
    this.storage = ctx.storage;
    this.rooms = new Map(); // "address:port" -> room
    ctx.blockConcurrencyWhile(async () => {
      for (const [k, r] of await this.storage.list()) this.rooms.set(k, r);
    });
  }

  async sweep(now) {
    const gone = [...this.rooms].filter(([, r]) => now - r.seen > TTL_MS).map(([k]) => k);
    for (const k of gone) this.rooms.delete(k);
    for (let i = 0; i < gone.length; i += 128) await this.storage.delete(gone.slice(i, i + 128));
  }

  async fetch(request) {
    const url = new URL(request.url);
    const now = Date.now();
    await this.sweep(now);

    if (request.method === "GET" && url.pathname === "/v1/rooms") {
      const rooms = [...this.rooms.values()]
        .sort((a, b) => b.players - a.players || a.name.localeCompare(b.name))
        .slice(0, 200)
        .map(({ name, address, port, players, max, version }) => ({ name, address, port, players, max, version }));
      return json({ rooms });
    }

    if (request.method !== "POST" || !["/v1/announce", "/v1/remove"].includes(url.pathname)) return json({ error: "not found" }, 404);
    const body = await request.json().catch(() => null);
    if (!body || typeof body !== "object") return json({ error: "bad request" }, 400);

    const from = request.headers.get("CF-Connecting-IP") || "";
    const address = isIpv4(from) ? from : body.ipv4;
    if (!isPublicIpv4(address)) return json({ error: "a public IPv4 address is needed to be listed" }, 400);
    const port = int(body.port, 1024, 65535);
    if (port === null) return json({ error: "bad port" }, 400);
    const key = `${address}:${port}`;

    if (url.pathname === "/v1/remove") {
      this.rooms.delete(key);
      await this.storage.delete(key);
      return json({ ok: true });
    }

    const max = int(body.max, 1, 4), players = int(body.players, 0, 4), version = int(body.version, 1, 100000);
    const name = cleanName(body.name);
    if (max === null || players === null || players > max || version === null || !name) return json({ error: "bad room" }, 400);
    if (!this.rooms.has(key)) {
      if (this.rooms.size >= MAX_ROOMS) return json({ error: "the list is full, try again later" }, 503);
      let mine = 0;
      for (const r of this.rooms.values()) if (r.address === address) mine++;
      if (mine >= MAX_PER_ADDRESS) return json({ error: "too many rooms from this address" }, 429);
    }
    const room = { name, address, port, players, max, version, seen: now };
    this.rooms.set(key, room);
    await this.storage.put(key, room);
    return json({ ok: true, address, ttl: TTL_MS / 1000 });
  }
}
