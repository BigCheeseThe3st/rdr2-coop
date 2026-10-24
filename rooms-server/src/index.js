// RDR2 Co-op dedicated servers on demand (George, 24 Oct: dedicated sessions for people who can't
// host, started from the Discord's #find-a-partner with their own password). Three doors:
//   POST /discord            Discord interactions (checked with Discord's signature): the pinned post's
//                            buttons and the start form. Passwords arrive here, never in a public message.
//   GET  /v1/agent/poll      the Mumble Pi's helper (server/rooms/rooms_agent.py) collects jobs
//   POST /v1/agent/report    and reports results and the rooms running (both need AGENT_SECRET)
// One Durable Object keeps the state (src/logic.js decides); an alarm each minute keeps the pinned
// post honest, so it says "offline" when the Pi goes quiet. Secrets: DISCORD_TOKEN, AGENT_SECRET.
import { empty, requestStart, requestStop, poll, report, statusText } from './logic.js'

const API = 'https://discord.com/api/v10'
const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const hex = h => new Uint8Array((h.match(/../g) || []).map(b => parseInt(b, 16)))
const reply = content => json({ type: 4, data: { content, flags: 64 } })   // only the person who clicked sees it

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

  async load() { return (await this.state.storage.get('s')) ?? empty() }
  async save(s) { await this.state.storage.put('s', s); if (!(await this.state.storage.getAlarm())) await this.state.storage.setAlarm(Date.now() + 60_000) }

  async fetch(req) {
    const path = new URL(req.url).pathname, data = await req.json().catch(() => ({})), now = Date.now(), s = await this.load()
    let out
    if (path === '/start') out = requestStart(s, data, now)
    else if (path === '/stop') out = requestStop(s, data.owner, now)
    else if (path === '/poll') out = { jobs: poll(s, now) }
    else if (path === '/report') {
      const replies = report(s, data, now)
      for (const r of replies) await this.tell(r.token, s, r.content, data)
      out = { ok: true }
    }
    await this.save(s)
    if (path === '/report' || path === '/start' || path === '/stop') await this.refresh(s, now)
    return json(out ?? { ok: false })
  }

  async alarm() {
    const s = await this.load()
    await this.refresh(s, Date.now())
    await this.state.storage.setAlarm(Date.now() + 60_000)
  }

  // The private reply to the person who asked: their form's reply thread while it lasts (15 minutes).
  async tell(token, s, content) {
    const r = await fetch(`${API}/webhooks/${this.env.APP_ID}/${token}`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content, flags: 64 }) }).catch(() => null)
    if (!r || !r.ok) console.log('reply failed', r && r.status)
  }

  // The pinned post's list of running servers, edited only when it changes.
  async refresh(s, now) {
    const text = statusText(s, now)
    if (text === (await this.state.storage.get('posted')) || !this.env.STATUS_THREAD) return
    const r = await fetch(`${API}/channels/${this.env.STATUS_THREAD}/messages/${this.env.STATUS_THREAD}`, { method: 'PATCH',
      headers: { Authorization: `Bot ${this.env.DISCORD_TOKEN}`, 'Content-Type': 'application/json', 'User-Agent': 'DiscordBot (https://github.com/BigCheeseThe3st/rdr2-coop, 1.0)' },
      body: JSON.stringify({ content: text, allowed_mentions: { parse: [] } }) }).catch(() => null)
    if (r && r.ok) await this.state.storage.put('posted', text)
    else console.log('status post edit failed', r && r.status)
  }
}
