// RDR2 Co-op dedicated servers on demand (George, 24 Oct: dedicated sessions for people who can't
// host, started from the Discord's #find-a-partner with their own password). Three doors:
//   POST /discord            Discord interactions (checked with Discord's signature): the pinned post's
//                            buttons and the start form. Passwords arrive here, never in a public message.
//   GET  /v1/agent/poll      the Mumble Pi's helper (server/rooms/rooms_agent.py) collects jobs
//   POST /v1/agent/report    and reports results and the rooms running (both need AGENT_SECRET)
// One Durable Object keeps the state (src/logic.js decides); an alarm each minute keeps the pinned
// post honest, so it says "offline" when the Pi goes quiet. Secrets: DISCORD_TOKEN, AGENT_SECRET.
// Each server gets a voice channel in Matchmaking (George: "locked to whoever is allowed to join the DS"):
// its starter is let in, anyone else presses its button and types the server's password.
import { empty, requestStart, requestStop, poll, report, statusText, statusButtons, voicePlan, checkVoice } from './logic.js'

const API = 'https://discord.com/api/v10'
const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const hex = h => new Uint8Array((h.match(/../g) || []).map(b => parseInt(b, 16)))
const reply = content => json({ type: 4, data: { content, flags: 64 } })   // only the person who clicked sees it
const VIEW = String(1 << 10), CONNECT = String(1 << 20), VOICE = '36701696'   // VOICE: view, connect, speak, video, voice activity
const sha = async t => [...new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(t)))].map(b => b.toString(16).padStart(2, '0')).join('')

async function fromDiscord(req, body, env) {
  const sig = req.headers.get('x-signature-ed25519'), ts = req.headers.get('x-signature-timestamp')
  if (!sig || !ts || !env.DISCORD_PUBLIC_KEY) return false
  try {
    const key = await crypto.subtle.importKey('raw', hex(env.DISCORD_PUBLIC_KEY), { name: 'Ed25519' }, false, ['verify'])
    return await crypto.subtle.verify({ name: 'Ed25519' }, key, hex(sig), new TextEncoder().encode(ts + body))
  } catch { return false }
}

function sameSecret(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string' || a.length !== b.length || !b) return false
  let d = 0
  for (let i = 0; i < a.length; i++) d |= a.charCodeAt(i) ^ b.charCodeAt(i)
  return d === 0
}

const FORM = { type: 9, data: { custom_id: 'rooms:form', title: 'Start a dedicated server', components: [
  { type: 1, components: [{ type: 4, custom_id: 'name', label: 'Server name (shown in the in-game browser)', style: 1, min_length: 2, max_length: 30, required: true }] },
  { type: 1, components: [{ type: 4, custom_id: 'password', label: 'Password (give it only to your friends)', style: 1, min_length: 4, max_length: 64, required: true }] }] } }
const VOICE_FORM = port => ({ type: 9, data: { custom_id: `rooms:vform:${port}`, title: "Join this server's voice chat", components: [
  { type: 1, components: [{ type: 4, custom_id: 'password', label: "The server's password", style: 1, min_length: 4, max_length: 64, required: true }] }] } })

async function interaction(req, env) {
  const body = await req.text()
  if (!(await fromDiscord(req, body, env))) return new Response('bad signature', { status: 401 })
  const i = JSON.parse(body)
  if (i.type === 1) return json({ type: 1 })                       // Discord checking the address
  const user = i.member?.user ?? i.user ?? {}
  const who = { owner: String(user.id ?? ''), owner_name: i.member?.nick || user.global_name || user.username || '' }
  const room = env.SESSIONS.get(env.SESSIONS.idFromName('main'))
  const call = (path, data) => room.fetch('https://do' + path, { method: 'POST', body: JSON.stringify(data) }).then(r => r.json())
  if (i.type === 3 && i.data?.custom_id === 'rooms:start') return json(FORM)
  if (i.type === 3 && i.data?.custom_id === 'rooms:stop') return reply((await call('/stop', who)).why)
  const vb = /^rooms:voice:(\d+)$/.exec(i.data?.custom_id ?? ''), vf = /^rooms:vform:(\d+)$/.exec(i.data?.custom_id ?? '')
  if (i.type === 3 && vb) return json(VOICE_FORM(vb[1]))
  if (i.type === 5 && vf) {
    const password = (i.data.components ?? []).flatMap(r => r.components ?? []).find(c => c.custom_id === 'password')?.value
    const r = await call('/voice', { user: who.owner, port: vf[1], password })
    return reply(r.ok ? `You're in: <#${r.channel}>. Click it to join the voice chat.` : r.why)
  }
  if (i.type === 5 && i.data?.custom_id === 'rooms:form') {
    const v = Object.fromEntries((i.data.components ?? []).flatMap(r => r.components ?? []).map(c => [c.custom_id, c.value]))
    const r = await call('/start', { ...who, name: v.name, password: v.password, token: i.token })
    return reply(r.ok ? `Starting **${r.name}**... I'll reply here when it's ready (usually under a minute).` : r.why)
  }
  return reply("That button isn't in use any more.")
}

export default {
  async fetch(req, env) {
    const url = new URL(req.url)
    if (url.pathname === '/discord' && req.method === 'POST') return interaction(req, env)
    if (url.pathname.startsWith('/v1/agent/')) {
      if (!sameSecret((req.headers.get('authorization') || '').replace(/^Bearer /, ''), env.AGENT_SECRET)) return new Response('no', { status: 401 })
      const room = env.SESSIONS.get(env.SESSIONS.idFromName('main'))
      if (url.pathname === '/v1/agent/poll' && req.method === 'GET') return room.fetch('https://do/poll', { method: 'POST', body: '{}' })
      if (url.pathname === '/v1/agent/report' && req.method === 'POST') return room.fetch('https://do/report', { method: 'POST', body: await req.text() })
    }
    return new Response('RDR2 Co-op dedicated servers. Start one from the Discord: #find-a-partner.\n')
  },
}

export class Sessions {
  constructor(state, env) { this.state = state; this.env = env }

  async load() { return this.s ??= (await this.state.storage.get('s')) ?? empty() }   // one shared copy: no lost updates while awaiting Discord
  async save(s) { await this.state.storage.put('s', s); if (!(await this.state.storage.getAlarm())) await this.state.storage.setAlarm(Date.now() + 60_000) }

  async fetch(req) {
    const path = new URL(req.url).pathname, data = await req.json().catch(() => ({})), now = Date.now(), s = await this.load()
    let out
    if (path === '/start') {
      data.salt = crypto.randomUUID(); data.hash = await sha(data.salt + String(data.password ?? ''))
      out = requestStart(s, data, now)
    }
    else if (path === '/voice') {
      const lock = s.locks?.[data.port]
      out = checkVoice(s, String(data.user), String(data.port), lock ? await sha(lock.salt + String(data.password ?? '')) : '', now)
      if (out.ok && !(await this.discord('PUT', `/channels/${out.channel}/permissions/${data.user}`, { type: 1, allow: VOICE, deny: '0' })).ok)
        out = { ok: false, why: "Couldn't open the voice chat for you. Ask a moderator." }
    }
    else if (path === '/stop') out = requestStop(s, data.owner, now)
    else if (path === '/poll') out = { jobs: poll(s, now) }
    else if (path === '/report') {
      const replies = report(s, data, now)
      await this.voice(s)
      for (const r of replies) {
        const v = r.port && s.locks?.[r.port]?.voice
        await this.tell(r.token, s, r.content + (v ? `\nYour group's voice chat: <#${v}>. You're already let in; friends press its 🔊 button in the pinned post and type the password.` : ''))
      }
      out = { ok: true }
    }
    await this.save(s)
    if (path === '/report' || path === '/start' || path === '/stop') await this.refresh(s, now)
    return json(out ?? { ok: false })
  }

  async alarm() {
    const s = await this.load()
    await this.voice(s)
    await this.refresh(s, Date.now())
    await this.state.storage.setAlarm(Date.now() + 60_000)
  }

  discord(method, path, body) {
    return fetch(API + path, { method, headers: { Authorization: `Bot ${this.env.DISCORD_TOKEN}`, 'Content-Type': 'application/json',
      'User-Agent': 'DiscordBot (https://github.com/BigCheeseThe3st/rdr2-coop, 1.0)' }, body: body === undefined ? undefined : JSON.stringify(body) })
      .catch(() => ({ ok: false, status: 0 }))
  }

  // Voice channels follow the servers: made when one starts (starter and staff let in, everyone else
  // sees it locked), deleted when it ends. Failures are retried on the next report or minute.
  async voice(s) {
    const plan = voicePlan(s)
    for (const p of plan.create) {
      const l = s.locks[p]
      const staff = String(this.env.STAFF_ROLES || '').split(',').filter(Boolean).map(id => ({ id, type: 0, allow: VOICE, deny: '0' }))
      const r = await this.discord('POST', `/guilds/${this.env.GUILD_ID}/channels`, { name: `${l.name} (dedicated)`, type: 2, user_limit: 4,
        parent_id: this.env.MATCH_CATEGORY, permission_overwrites: [{ id: this.env.GUILD_ID, type: 0, allow: VIEW, deny: CONNECT },
          { id: l.owner, type: 1, allow: VOICE, deny: '0' }, { id: this.env.APP_ID, type: 1, allow: VOICE, deny: '0' }, ...staff] })   // the bot too: Discord needs Connect to manage a voice channel
      if (r.ok) l.voice = (await r.json()).id
      else console.log('voice channel create failed', r.status)
    }
    for (const p of plan.remove) {
      const v = s.locks[p].voice
      const r = v ? await this.discord('DELETE', `/channels/${v}`) : { ok: true }
      if (r.ok || r.status === 404 || r.status === 403) delete s.locks[p]   // 403: made before the bot let itself in; a moderator deletes it
      else console.log('voice channel delete failed', r.status)
    }
    if (plan.create.length || plan.remove.length) await this.state.storage.put('s', s)
  }

  // The private reply to the person who asked: their form's reply thread while it lasts (15 minutes).
  async tell(token, s, content) {
    const r = await fetch(`${API}/webhooks/${this.env.APP_ID}/${token}`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content, flags: 64 }) }).catch(() => null)
    if (!r || !r.ok) console.log('reply failed', r && r.status)
  }

  // The pinned post's list of running servers, edited only when it changes.
  async refresh(s, now) {
    const text = statusText(s, now), components = statusButtons(s), key = JSON.stringify([text, components])
    if (key === (await this.state.storage.get('posted')) || !this.env.STATUS_THREAD) return
    const r = await this.discord('PATCH', `/channels/${this.env.STATUS_THREAD}/messages/${this.env.STATUS_THREAD}`,
      { content: text, components, allowed_mentions: { parse: [] } })
    if (r.ok) await this.state.storage.put('posted', key)
    else console.log('status post edit failed', r && r.status)
  }
}
