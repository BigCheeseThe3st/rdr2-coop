// RDR2 Co-op dedicated servers on demand (the author, 24 Oct: dedicated sessions for people who can't
// host, started from the Discord's #find-a-partner with their own password). Three doors:
//   POST /discord            Discord interactions (checked with Discord's signature): the pinned post's
//                            buttons and the start form. Passwords arrive here, never in a public message.
//   GET  /v1/agent/poll      the dedicated-server machine's helper (server/rooms/rooms_agent.py) collects jobs
//   POST /v1/agent/report    and reports results and the rooms running (both need AGENT_SECRET)
// One Durable Object keeps the state (src/logic.js decides); an alarm each minute keeps the pinned
// post honest, so it says "offline" when the Pi goes quiet. Secrets: DISCORD_TOKEN, AGENT_SECRET.
// Each server gets a voice channel in Matchmaking (the author: "locked to whoever is allowed to join the DS"):
// its starter is let in, anyone else presses its button and types the server's password.
// The same alarm runs the help bot (src/helpdesk.js; secret ANTHROPIC_API_KEY, off without it).
import { empty, requestStart, requestStop, poll, report, statusText, statusButtons, voicePlan, checkVoice } from './logic.js'
import { emptyLedger, record, due, announcement, reminder } from './kofi.js'
import { BUG_FORM, formValues, bugReport } from './bugform.js'
import Anthropic from '@anthropic-ai/sdk'
import { emptyHelp, newMessages, allowed, counted, redact, clean, looksLikeQuestion, transcript, ask, HANDOFF, MAX_BOT_REPLIES, TEXT_FILE, IMAGE_TYPES, imageType } from './helpdesk.js'

const API = 'https://discord.com/api/v10'
const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const hex = h => new Uint8Array((h.match(/../g) || []).map(b => parseInt(b, 16)))
const reply = content => json({ type: 4, data: { content, flags: 64 } })   // only the person who clicked sees it
const VIEW = String(1 << 10), CONNECT = String(1 << 20), VOICE = '36701696'   // VOICE: view, connect, speak, video, voice activity
const UA = 'DiscordBot (https://github.com/BigCheeseThe3st/rdr2-coop, 1.0)'
const b64 = bytes => { let t = ''; for (let i = 0; i < bytes.length; i += 0x8000) t += String.fromCharCode(...bytes.subarray(i, i + 0x8000)); return btoa(t) }
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

async function interaction(req, env, ctx) {
  const body = await req.text()
  if (!(await fromDiscord(req, body, env))) return new Response('bad signature', { status: 401 })
  const i = JSON.parse(body)
  if (i.type === 1) return json({ type: 1 })                       // Discord checking the address
  const user = i.member?.user ?? i.user ?? {}
  const who = { owner: String(user.id ?? ''), owner_name: i.member?.nick || user.global_name || user.username || '' }
  const room = env.SESSIONS.get(env.SESSIONS.idFromName('main'))
  const call = (path, data) => room.fetch('https://do' + path, { method: 'POST', body: JSON.stringify(data) }).then(r => r.json())
  if (i.type === 3 && i.data?.custom_id === 'rooms:start') return json(FORM)
  if (i.type === 3 && i.data?.custom_id === 'bugs:open') return json(BUG_FORM)
  if (i.type === 5 && i.data?.custom_id === 'bugs:form') {
    ctx.waitUntil(openBugTicket(env, i, who, formValues(i.data.components)))
    return json({ type: 5, data: { flags: 64 } })   // "thinking": the ticket takes three calls, the answer is edited in
  }
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

// The form's answers (src/bugform.js) become the reporter's private ticket; they're told where it is (only they see the answer).
async function openBugTicket(env, i, who, v) {
  const h = { Authorization: `Bot ${env.DISCORD_TOKEN}`, 'Content-Type': 'application/json', 'User-Agent': UA }
  const api = (method, path, body) => fetch(API + path, { method, headers: h, body: body && JSON.stringify(body) })
  let text
  try {
    const t = await api('POST', `/channels/${env.BUG_CHANNEL}/threads`, { name: String(v.title ?? 'Bug').slice(0, 100), type: 12, invitable: true, auto_archive_duration: 10080 })
    if (!t.ok) throw new Error('thread ' + t.status)
    const id = (await t.json()).id
    await api('PUT', `/channels/${id}/thread-members/${who.owner}`)
    const m = await api('POST', `/channels/${id}/messages`, { content: bugReport(who.owner_name, who.owner, v), allowed_mentions: { users: [who.owner] } })
    if (!m.ok) throw new Error('message ' + m.status)
    text = `Your ticket is open: <#${id}>. Only you, anyone you add and the moderators can see it. Please add your logs there.`
  } catch (e) {
    console.log('bug ticket failed', e.message)
    text = "Couldn't open the ticket. Open a private thread here yourself (Threads > Create, tick Private) and paste your report."
  }
  await fetch(`${API}/webhooks/${env.APP_ID}/${i.token}/messages/@original`, { method: 'PATCH', headers: h, body: JSON.stringify({ content: text }) })
}

export default {
  async fetch(req, env, ctx) {
    const url = new URL(req.url)
    if (url.pathname === '/discord' && req.method === 'POST') return interaction(req, env, ctx)
    if (url.pathname === '/kofi' && req.method === 'POST') {           // Ko-fi's webhook: a form with one JSON field, "data"
      const d = JSON.parse((await req.formData().catch(() => null))?.get('data') ?? 'null')
      if (!d || !env.KOFI_TOKEN || !sameSecret(String(d.verification_token ?? ''), env.KOFI_TOKEN)) return new Response('no', { status: 401 })
      const room = env.SESSIONS.get(env.SESSIONS.idFromName('main'))
      return room.fetch('https://do/kofi', { method: 'POST', body: JSON.stringify(d) })
    }
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
    else if (path === '/kofi') {
      const ledger = (await this.state.storage.get('kofi')) ?? emptyLedger()
      console.log('ko-fi:', record(ledger, data, now))
      await this.state.storage.put('kofi', ledger)
      return json({ ok: true })
    }
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
    await this.kofiMonthly(Date.now())
    await this.helpdesk(Date.now()).catch(e => console.log('help: failed', e?.message))
    await this.state.storage.setAlarm(Date.now() + 60_000)
  }

  discord(method, path, body) {
    return fetch(API + path, { method, headers: { Authorization: `Bot ${this.env.DISCORD_TOKEN}`, 'Content-Type': 'application/json',
      'User-Agent': UA }, body: body === undefined ? undefined : JSON.stringify(body) })
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

  // On the 1st (any minute after), last month's Ko-fi total goes to #announcements and the author gets a reminder.
  async kofiMonthly(now) {
    const ledger = await this.state.storage.get('kofi')
    if (!ledger || !this.env.ANNOUNCE_CHANNEL) return
    for (const key of due(ledger, now)) {
      const m = ledger.months[key]
      const r = await this.discord('POST', `/channels/${this.env.ANNOUNCE_CHANNEL}/messages`, { content: announcement(key, m), flags: 4, allowed_mentions: { parse: [] } })
      if (!r.ok) { console.log('ko-fi post failed', r.status); return }
      m.reported = true
      await this.state.storage.put('kofi', ledger)
      const dm = this.env.OWNER_ID && await this.discord('POST', '/users/@me/channels', { recipient_id: this.env.OWNER_ID })
      if (dm?.ok) await this.discord('POST', `/channels/${(await dm.json()).id}/messages`, { content: reminder(key, m), flags: 4 })
    }
  }

  // The help bot: new messages in #help tickets and the chat channels, answered from the script
  // (src/helpdesk-kb.js) or handed to the helpers. At most 3 model calls a minute; the rest wait.
  async helpdesk(now) {
    const env = this.env
    if (!env.ANTHROPIC_API_KEY || !env.HELP_CHANNEL) return
    const h = (await this.state.storage.get('help')) ?? emptyHelp()
    try {
      if (!h.versions || now - h.versions.at > 3600_000) h.versions = (await this.versions(now)) ?? h.versions
      const r = await this.discord('GET', `/guilds/${env.GUILD_ID}/threads/active`)
      if (!r.ok) return console.log('help: threads', r.status)
      const client = new Anthropic({ apiKey: env.ANTHROPIC_API_KEY, maxRetries: 1, timeout: 60_000 })
      let budget = 3
      for (const t of (await r.json()).threads ?? []) {
        if (t.parent_id !== env.HELP_CHANNEL) continue
        const st = h.threads[t.id] ??= { replies: 0, quiet: false }
        st.at = now
        if (!st.quiet && t.last_message_id !== st.seen && budget > 0) budget -= await this.ticket(h, st, t, client, now)
      }
      for (const ch of String(env.CHAT_CHANNELS || '').split(',').filter(Boolean))
        if (budget > 0) budget -= await this.chat(h, h.chat[ch] ??= {}, ch, client, now)
      for (const [id, st] of Object.entries(h.threads)) if (now - st.at > 7 * 86400_000) delete h.threads[id]   // closed a week ago
      for (const [id, ts] of Object.entries(h.users)) if (!ts.some(t => now - t < 3600_000)) delete h.users[id]
      for (const [id, c] of Object.entries(h.staff)) if (now - c.at > 86400_000) delete h.staff[id]
    } finally { await this.state.storage.put('help', h) }
  }

  // One ticket: the model reads it when a player wrote something new, until a staff member joins in,
  // it hands off, or it has answered MAX_BOT_REPLIES times. Returns the model calls used.
  async ticket(h, st, t, client, now) {
    const r = await this.discord('GET', `/channels/${t.id}/messages?limit=40`)
    if (!r.ok) return 0
    const msgs = await r.json(), fresh = newMessages(msgs, st.last, this.env.APP_ID, now)
    st.seen = t.last_message_id
    if (!fresh.length) { st.last ??= msgs[0]?.id ?? '0'; return 0 }
    st.last ??= msgs.find(m => BigInt(m.id) < BigInt(fresh[0].id))?.id ?? '0'   // first look: these stay new until answered
    const latest = fresh[fresh.length - 1]
    for (const m of fresh) if (await this.isStaff(h, m.author.id, now)) { st.quiet = true; st.last = latest.id; return 0 }   // a person has it
    if (st.replies >= MAX_BOT_REPLIES) { await this.handoff(st, t, latest, HANDOFF, `${MAX_BOT_REPLIES} answers and still open`); return 0 }
    if (!allowed(h, latest.author.id, now)) { st.seen = undefined; return 0 }   // over the limits: look again next minute
    counted(h, latest.author.id, now)
    const { files, images } = await this.attachments(fresh)
    const history = [...msgs].reverse().filter(m => m.type === 0 || m.type === 19)
    let d
    try {
      d = await ask(client, { where: `#help ticket "${t.name}"`, versions: h.versions, images,
        text: 'The ticket so far (oldest first):\n' + transcript(history, this.env.APP_ID, id => this.staffKnown(h, id), files) })
    } catch (e) {
      this.modelError(e)
      st.seen = undefined
      if ((st.fails = (st.fails ?? 0) + 1) >= 3) await this.handoff(st, t, latest, HANDOFF, "the bot couldn't answer")
      return 1
    }
    console.log('help: ticket', t.id, d.action, d.topic, JSON.stringify(d.usage ?? {}))
    st.last = latest.id
    if (d.action === 'none') return 1
    if (d.action === 'handoff') { await this.handoff(st, t, latest, d.reply || HANDOFF, d.topic); return 1 }
    const id = await this.post(t.id, d.reply, latest.id)
    if (id) { st.seen = id; st.replies++ }
    return 1
  }

  async handoff(st, t, latest, text, why) {
    st.quiet = true; st.last = latest.id
    const id = await this.post(t.id, text, latest.id)
    if (id) st.seen = id
    if (this.env.HELPERS_CHANNEL) await this.post(this.env.HELPERS_CHANNEL, `Ticket for a person: <#${t.id}> (${clean(why).slice(0, 200) || 'see the ticket'})`)
  }

  // A chat channel: someone asking for help is pointed to #help with one thing to try.
  async chat(h, st, ch, client, now) {
    const r = await this.discord('GET', `/channels/${ch}/messages?limit=20`)
    if (!r.ok) return 0
    const msgs = await r.json(), fresh = newMessages(msgs, st.last, this.env.APP_ID, now)
    if (!fresh.length) { st.last ??= msgs[0]?.id ?? '0'; return 0 }
    st.last ??= msgs.find(m => BigInt(m.id) < BigInt(fresh[0].id))?.id ?? '0'
    const before = [...msgs].reverse()
    let used = 0
    for (const m of fresh) {
      const answered = msgs.some(x => BigInt(x.id) > BigInt(m.id) && (x.author?.id === this.env.APP_ID || this.staffKnown(h, x.author?.id)))
      if (!answered && looksLikeQuestion(m.content) && !(await this.isStaff(h, m.author.id, now)) && allowed(h, m.author.id, now)) {
        if (used >= 2) break
        counted(h, m.author.id, now); used++
        const context = before.filter(x => BigInt(x.id) < BigInt(m.id)).slice(-6)
        let d
        try {
          d = await ask(client, { where: 'a public chat channel', versions: h.versions,
            text: 'Earlier messages, for context:\n' + transcript(context, this.env.APP_ID, id => this.staffKnown(h, id)) +
                  '\n\nThe message to decide on:\n' + transcript([m], this.env.APP_ID, () => false) })
        } catch (e) { this.modelError(e); break }   // this message again next minute
        console.log('help: chat', m.id, d.action, d.topic, JSON.stringify(d.usage ?? {}))
        if (d.action === 'answer' && d.reply) await this.post(ch, d.reply, m.id)   // no hand-offs in public
      }
      st.last = m.id
    }
    return used
  }

  staffKnown(h, id) { return id === this.env.OWNER_ID || !!h.staff[id]?.s }

  async isStaff(h, uid, now) {
    if (uid === this.env.OWNER_ID) return true
    const c = h.staff[uid]
    if (c && now - c.at < 86400_000) return c.s
    const r = await this.discord('GET', `/guilds/${this.env.GUILD_ID}/members/${uid}`)
    if (!r.ok) return c?.s ?? false
    const roles = new Set(`${this.env.STAFF_ROLES || ''},${this.env.HELPER_ROLES || ''}`.split(',').filter(Boolean))
    h.staff[uid] = { s: ((await r.json()).roles ?? []).some(x => roles.has(x)), at: now }
    return h.staff[uid].s
  }

  // Logs and ini files (passwords hidden, long logs cut to their end) and up to two pictures.
  async attachments(msgs) {
    const files = {}, images = []
    for (const a of msgs.flatMap(m => m.attachments ?? [])) {
      const text = TEXT_FILE.test(a.filename) && a.size <= 2_000_000, image = IMAGE_TYPES.includes(a.content_type) && a.size <= 3_500_000 && images.length < 2
      if (!text && !image) continue
      const r = await fetch(a.url, { headers: { 'User-Agent': UA } }).catch(() => null)
      if (!r?.ok) continue
      if (text) { const t = redact(await r.text()); files[a.id] = t.length > 12_000 ? '[start cut]\n' + t.slice(-12_000) : t }
      else { const bytes = new Uint8Array(await r.arrayBuffer()), type = imageType(bytes); if (type) images.push({ media_type: type, data: b64(bytes) }) }
    }
    return { files, images }
  }

  // Stable and beta from the releases' latest.txt, so the bot's version numbers stay right.
  async versions(now) {
    const get = path => fetch(`https://github.com/BigCheeseThe3st/rdr2-coop/releases/${path}/latest.txt`)
      .then(r => (r.ok ? r.text() : '')).then(t => t.split('\n')[0].trim()).catch(() => '')
    const [stable, beta] = await Promise.all([get('latest/download'), get('download/beta-channel')])
    return stable ? { stable, beta: beta && !beta.startsWith(stable + '-') ? beta : '', at: now } : null
  }

  modelError(e) {
    if (e instanceof Anthropic.AuthenticationError) console.log('help: the API key was refused')
    else if (e instanceof Anthropic.RateLimitError) console.log('help: rate limited')
    else if (e instanceof Anthropic.APIError) console.log('help: API error', e.status, e.message)
    else console.log('help: model call failed', e?.message)
  }

  async post(channel, content, replyTo) {
    const r = await this.discord('POST', `/channels/${channel}/messages`, { content: clean(content), flags: 4, allowed_mentions: { parse: [] },
      ...(replyTo ? { message_reference: { message_id: replyTo, fail_if_not_exists: false } } : {}) })
    if (!r.ok) { console.log('help: post failed', r.status); return null }
    return (await r.json()).id
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
