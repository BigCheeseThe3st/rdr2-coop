// Log uploads from beta builds of RDR2 Co-op (the author, 25 Oct: "have peoples logs autosend if they use
// beta versions"). Opt-in in the mod ([Testing] SendLogs=Yes, asked once in game); the mod scrubs addresses
// and paths before sending. POST /v1/upload stores the body in R2 (size-capped, text only); GET /v1/list and
// /v1/get?key= read it back with the author's LOG_READ_TOKEN. Objects expire after 30 days (R2 lifecycle rule 'keep-30-days').
import { keyFor, fieldsFrom } from './logic.js'

const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

function sameSecret(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string' || a.length !== b.length || !b) return false
  let d = 0
  for (let i = 0; i < a.length; i++) d |= a.charCodeAt(i) ^ b.charCodeAt(i)
  return d === 0
}

export default {
  async fetch(req, env) {
    const url = new URL(req.url)
    if (url.pathname === '/v1/upload' && req.method === 'POST') {
      // An install id from the player's name and connection, for uploads that came without one (the same player keeps it).
      const seed = new TextEncoder().encode(`${req.headers.get('cf-connecting-ip') ?? ''}|${req.headers.get('x-coop-name') ?? ''}`)
      const derived = [...new Uint8Array(await crypto.subtle.digest('SHA-256', seed))].slice(0, 6).map(b => b.toString(16).padStart(2, '0')).join('')
      const f = fieldsFrom(req.headers, derived)
      if (!f) return json({ ok: false, why: 'bad fields' }, 400)
      const max = Number(env.MAX_BYTES || 1048576)
      const len = Number(req.headers.get('content-length') || 0)
      if (len > max) return json({ ok: false, why: 'too big' }, 413)
      const body = await req.arrayBuffer()
      if (body.byteLength > max || body.byteLength === 0) return json({ ok: false, why: 'too big or empty' }, 413)
      const key = keyFor(f, Date.now())
      await env.LOGS.put(key, body, { httpMetadata: { contentType: 'text/plain; charset=utf-8' },
        customMetadata: { name: String(f.name ?? '').slice(0, 64), version: f.version, build: f.build.slice(0, 16), why: f.why.slice(0, 64), derived: f.derived ? 'yes' : '', ip: req.headers.get('cf-connecting-ip') ?? '' } })
      return json({ ok: true, id: key })
    }
    if (url.pathname.startsWith('/v1/') && req.method === 'GET') {
      if (!sameSecret((req.headers.get('authorization') || '').replace(/^Bearer /, ''), env.LOG_READ_TOKEN)) return new Response('no', { status: 401 })
      if (url.pathname === '/v1/list') {   // one page; the caller passes back `cursor` until it's gone. Old logs expire by the bucket's lifecycle rule.
        const list = await env.LOGS.list({ prefix: url.searchParams.get('prefix') || 'logs/', cursor: url.searchParams.get('cursor') || undefined, limit: 1000, include: ['customMetadata'] })
        return json({ objects: list.objects.map(o => ({ key: o.key, size: o.size, uploaded: o.uploaded, meta: o.customMetadata })), cursor: list.truncated ? list.cursor : null })
      }
      if (url.pathname === '/v1/get') {
        const o = await env.LOGS.get(url.searchParams.get('key') || '')
        return o ? new Response(o.body, { headers: { 'Content-Type': 'text/plain; charset=utf-8' } }) : new Response('not found', { status: 404 })
      }
    }
    return new Response('RDR2 Co-op beta log store.\n')
  },
}
