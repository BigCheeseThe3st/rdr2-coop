// Dedicated servers on demand: what the sessions worker decides, kept apart from the network so it
// can be tested. State is one plain object: jobs waiting for the Pi, the rooms the Pi last reported,
// when the Pi was last heard from, and who asked for what (with their Discord reply token).

export const OFFLINE_MS = 45_000        // no poll from the Pi for this long: dedicated servers are offline
export const RESEND_MS = 60_000         // a job sent to the Pi with no result after this is sent again
export const JOB_TTL_MS = 5 * 60_000    // a job (and the password in it) is forgotten after this
export const START_GAP_MS = 60_000      // one start request per person per minute
export const TRIES = 5                  // wrong voice-chat passwords allowed per person...
export const TRY_WINDOW_MS = 10 * 60_000 // ...in this long

export const empty = () => ({ jobs: [], rooms: [], capacity: 4, agentSeen: 0, lastStart: {}, seq: 0, locks: {}, tries: {} })

// The one word kept out of room names (the author: "bad-word filter for the N word please but nothing else"),
// seen through spaces, punctuation and 1/3/4/@ for i/e/a.
export const slur = (s) => /nigg+(?:e+r|a|u+h|a+h|a+z)/.test(String(s ?? "").toLowerCase().replace(/[1!|]/g, "i").replace(/3/g, "e").replace(/[4@]/g, "a").replace(/[^a-z]/g, ""));

/** The server name shown in the in-game browser: printable, no game formatting codes, short. */
export function cleanName(name) {
  return String(name ?? '').replace(/[~<>\u0000-\u001f\u007f]/g, '').trim().slice(0, 30)
}

/** The form's two fields, checked. */
export function checkForm(name, password) {
  const n = cleanName(name), p = String(password ?? '')
  if (n.length < 2) return { ok: false, why: 'Give the server a name (2 to 30 letters).' }
  if (slur(n)) return { ok: false, why: "That name isn't allowed. Please pick another." }
  if (p.length < 4 || p.length > 64) return { ok: false, why: 'The password needs 4 to 64 characters.' }
  if (/[\u0000-\u001f\u007f]/.test(p)) return { ok: false, why: 'The password can only use normal characters.' }
  return { ok: true, name: n, password: p }
}

export const online = (s, now) => now - s.agentSeen < OFFLINE_MS

/** A start request from the Discord form. */
export function requestStart(s, req, now) {
  const f = checkForm(req.name, req.password)
  if (!f.ok) return { ok: false, why: f.why }
  if (!online(s, now)) return { ok: false, why: 'Dedicated servers are offline right now. Please try again later.' }
  if (s.rooms.some(r => r.owner === req.owner) || s.jobs.some(j => j.owner === req.owner && j.action === 'start'))
    return { ok: false, why: 'You already have a dedicated server. Stop it first (Stop my server).' }
  const busy = s.rooms.length + s.jobs.filter(j => j.action === 'start').length
  if (busy >= s.capacity) return { ok: false, why: `All ${s.capacity} dedicated servers are in use. Try again in a while.` }
  const last = s.lastStart[req.owner]
  if (last !== undefined && now - last < START_GAP_MS) return { ok: false, why: 'Please wait a minute between requests.' }
  s.lastStart[req.owner] = now
  const job = { id: `j${++s.seq}-${now}`, action: 'start', owner: req.owner, owner_name: String(req.owner_name ?? '').slice(0, 40),
                name: f.name, password: f.password, salt: req.salt, hash: req.hash, token: req.token, created: now, sent: 0 }
  s.jobs.push(job)
  return { ok: true, name: f.name }
}

/** "Stop my server". */
export function requestStop(s, owner, now) {
  const pending = s.jobs.find(j => j.owner === owner && j.action === 'start')
  if (pending) { s.jobs = s.jobs.filter(j => j !== pending); return { ok: true, why: 'Your server request was cancelled.' } }
  const room = s.rooms.find(r => r.owner === owner)
  if (!room) return { ok: false, why: "You don't have a dedicated server running." }
  if (!s.jobs.some(j => j.action === 'stop' && j.port === room.port))
    s.jobs.push({ id: `j${++s.seq}-${now}`, action: 'stop', owner, port: room.port, created: now, sent: 0 })
  return { ok: true, why: `Stopping '${room.name}'.` }
}

/** What the Pi gets when it polls: jobs not yet sent, or sent long ago with no result. */
export function poll(s, now) {
  s.agentSeen = now
  s.jobs = s.jobs.filter(j => now - j.created < JOB_TTL_MS)
  const out = []
  for (const j of s.jobs) {
    if (j.sent && now - j.sent < RESEND_MS) continue
    j.sent = now
    out.push(j.action === 'start' ? { id: j.id, action: 'start', owner: j.owner, owner_name: j.owner_name, name: j.name, password: j.password }
                                  : { id: j.id, action: 'stop', port: j.port })
  }
  return out
}

/** The Pi's report: job results, rooms that ended, the rooms running now. Returns replies to send. */
export function report(s, body, now) {
  s.agentSeen = now
  const replies = [], started = []
  for (const r of body.results ?? []) {
    const job = s.jobs.find(j => j.id === r.id)
    if (!job) continue
    s.jobs = s.jobs.filter(j => j !== job)          // done: the password goes with it
    if (job.action !== 'start') continue
    if (r.ok && job.hash) started.push({ port: String(r.port), owner: job.owner, name: job.name, salt: job.salt, hash: job.hash })
    if (!job.token) continue
    replies.push({ token: job.token, port: r.ok ? String(r.port) : null, content: r.ok
      ? `Your dedicated server **${job.name}** is up. In game: press **F10 twice**, pick **${job.name} (dedicated)** and type your password. Give your friends the name and password. It stops after 30 minutes with nobody in it, or after 12 hours.`
      : `Your dedicated server couldn't start: ${String(r.why ?? 'unknown reason').slice(0, 200)}` })
  }
  s.rooms = (body.rooms ?? []).slice(0, 16).map(r => ({ port: Number(r.port), owner: String(r.owner ?? ''), owner_name: String(r.owner_name ?? '').slice(0, 40),
    name: cleanName(r.name), players: Math.max(0, Math.min(4, Number(r.players) || 0)), started: Number(r.started) || 0 }))
  if (Number(body.capacity) > 0) s.capacity = Math.min(16, Number(body.capacity))
  s.locks ??= {}; s.tries ??= {}
  for (const l of started)                         // its voice chat opens to whoever knows the password
    if (s.rooms.some(r => String(r.port) === l.port)) s.locks[l.port] = { owner: l.owner, name: l.name, salt: l.salt, hash: l.hash, voice: null }
  for (const [u, t] of Object.entries(s.tries)) { const keep = t.filter(x => now - x < TRY_WINDOW_MS); if (keep.length) s.tries[u] = keep; else delete s.tries[u] }
  return replies
}

/** Voice channels to make (running servers without one) and to remove (servers that ended). */
export function voicePlan(s) {
  const live = new Set(s.rooms.map(r => String(r.port)))
  const locks = Object.entries(s.locks ?? {})
  return { create: locks.filter(([p, l]) => live.has(p) && !l.voice).map(([p]) => p),
           remove: locks.filter(([p]) => !live.has(p)).map(([p]) => p) }
}

/** Someone typed a password for a server's voice chat (hash: of the lock's salt + what they typed). */
export function checkVoice(s, user, port, hash, now) {
  const lock = s.locks?.[port]
  if (!lock) return { ok: false, why: "That server isn't running any more." }
  s.tries ??= {}
  const tries = (s.tries[user] ?? []).filter(t => now - t < TRY_WINDOW_MS)
  if (tries.length >= TRIES) return { ok: false, why: 'Too many wrong passwords. Try again in a few minutes.' }
  if (hash !== lock.hash) { s.tries[user] = [...tries, now]; return { ok: false, why: "That isn't the server's password." } }
  if (!lock.voice) return { ok: false, why: 'Its voice chat is still being set up. Try again in a minute.' }
  return { ok: true, channel: lock.voice }
}

/** The pinned post's buttons: start/stop, then one per server with a voice chat. */
export function statusButtons(s) {
  const rows = [{ type: 1, components: [{ type: 2, custom_id: 'rooms:start', style: 3, label: 'Start a dedicated server' },
                                       { type: 2, custom_id: 'rooms:stop', style: 4, label: 'Stop my server' }] }]
  const voice = s.rooms.filter(r => s.locks?.[String(r.port)]?.voice)
    .map(r => ({ type: 2, custom_id: `rooms:voice:${r.port}`, style: 2, label: `🔊 ${r.name}` }))
  for (let i = 0; i < voice.length && rows.length < 5; i += 5) rows.push({ type: 1, components: voice.slice(i, i + 5) })
  return rows
}

/** The pinned post in #find-a-partner. */
export function statusText(s, now) {
  const head = '**Dedicated servers**\nCan\'t host yourself (your router won\'t let friends in)? Start a dedicated server here: press **Start a dedicated server**, choose a name and a password, and it appears in the in-game browser (**F10 twice**) within a minute. Whoever joins first hosts the story. Up to ' +
    `${s.capacity} at a time, one each; a server stops after 30 minutes with nobody in it, or after 12 hours.\n\n`
  if (!online(s, now)) return head + '_Dedicated servers are offline right now._'
  if (!s.rooms.length) return head + `**Running now:** none (${s.capacity} free)`
  const lines = s.rooms.map(r => `- **${r.name}**: ${r.players}/4 players${r.owner_name ? ` (started by ${r.owner_name})` : ''}`)
  const voice = s.rooms.some(r => s.locks?.[String(r.port)]?.voice) ? '\nEach server has its own voice chat: press its 🔊 button and type the server\'s password.' : ''
  return head + `**Running now** (${s.capacity - s.rooms.length} free):\n` + lines.join('\n') + voice
}
