// A whole run against a fake Discord and GitHub: a new form ticket is filed once with its tracker post,
// a closed issue marks the ticket and the post and counts for the reporter, nothing happens twice, and a
// run stays under the free plan's 50 requests.
import test from 'node:test'
import assert from 'node:assert/strict'
import { sync } from '../src/index.js'
import { bugReport } from '../../rooms-server/src/bugform.js'

const env = { GUILD_ID: 'G', BUG_CHANNEL: 'BC', TRACKER_FORUM: 'TF', BOT_ID: 'BOT', REPO: 'o/r', PEST_ROLE: 'PR', PEST_COUNT: '1', DISCORD_TOKEN: 'x', GITHUB_TOKEN: 'y' }

function world() {
  const kv = new Map()
  env.STATE = { get: async k => kv.get(k) ?? null, put: async (k, v) => { kv.set(k, v) } }
  const w = { kv, calls: [], issues: [], tickets: { T1: { id: 'T1', parent_id: 'BC', name: 'Horse floats', owner_id: 'BOT' } },
              messages: { T1: [{ id: '10', author: { id: 'BOT' }, content: bugReport('Ann', '7', { version: '0.9.0-beta.6', setup: 'windows-host', kind: 'Missions', what: 'It floats.' }) }] },
              posts: {}, roles: [] }
  globalThis.fetch = async (url, opt = {}) => {
    const u = new URL(url), m = opt.method || 'GET', body = opt.body && JSON.parse(opt.body), p = u.pathname.replace('/api/v10', '').replace('/repos/o/r', '')
    w.calls.push(`${m} ${p}`)
    const ok = data => new Response(data === null ? null : JSON.stringify(data), { status: data === null ? 204 : 200 })
    if (p === '/guilds/G/threads/active') return ok({ threads: Object.values(w.tickets) })
    if (p === '/channels/TF' && m === 'GET') return ok({ available_tags: ['Windows', 'Hosting', 'Missions', 'Beta', 'On GitHub', 'Fixed'].map(n => ({ id: 'tag-' + n, name: n })) })
    if (p === '/issues' && m === 'POST') { const n = w.issues.length + 1; w.issues.push({ number: n, state: 'open', html_url: `https://github.com/o/r/issues/${n}`, ...body }); return ok(w.issues[n - 1]) }
    if (p === '/issues' && m === 'GET') return ok(w.issues.filter(i => i.state === 'closed'))
    if (p === '/channels/TF/threads' && m === 'POST') { w.posts.P1 = { id: 'P1', name: body.name, applied_tags: body.applied_tags, thread_metadata: { archived: true }, content: body.message.content }; return ok(w.posts.P1) }
    let r
    if ((r = /^\/channels\/(\w+)\/messages$/.exec(p))) {
      if (m === 'POST') { (w.messages[r[1]] ??= []).push({ id: String(100 + w.calls.length), author: { id: 'BOT' }, content: body.content }); return ok({}) }
      return ok([...(w.messages[r[1]] || [])].reverse())
    }
    if ((r = /^\/channels\/(\w+)$/.exec(p))) {
      const t = w.tickets[r[1]] || w.posts[r[1]]
      if (m === 'PATCH') Object.assign(t, body, body.archived === false ? { thread_metadata: { archived: false } } : {})
      return ok(t)
    }
    if (p === '/guilds/G/members/7') return ok({ roles: w.roles })
    if (p === '/guilds/G/members/7/roles/PR' && m === 'PUT') { w.roles.push('PR'); return ok(null) }
    throw new Error(`unexpected ${m} ${p}`)
  }
  return w
}

test('a form ticket is filed once, with a tagged tracker post and the links kept', async () => {
  const w = world()
  await sync(env, Date.parse('2026-10-26T12:00:00Z'))
  assert.equal(w.issues.length, 1)
  assert.match(w.issues[0].body, /by Ann \(private ticket\)/)
  assert.match(w.messages.T1.at(-1).content, /Copied to GitHub as #1/)
  assert.equal(w.posts.P1.name, '#1 · Horse floats')
  assert.deepEqual(w.posts.P1.applied_tags, ['tag-Windows', 'tag-Hosting', 'tag-Missions', 'tag-Beta', 'tag-On GitHub'])
  assert.deepEqual(JSON.parse(w.kv.get('i:1')), { ticket: 'T1', reporter: '7', post: 'P1' })
  assert.ok(w.calls.length < 50, `${w.calls.length} requests`)
  const before = w.calls.length
  await sync(env)
  assert.equal(w.issues.length, 1, 'not filed twice')
  assert.ok(w.calls.length - before <= 3, 'a quiet run is a few requests: ' + w.calls.slice(before).join(', '))
})

test('a closed issue marks the ticket and the post, counts for the reporter, once', async () => {
  const w = world()
  await sync(env)
  Object.assign(w.issues[0], { state: 'closed', state_reason: 'completed' })
  await sync(env)
  assert.equal(w.tickets.T1.name, '✅ Horse floats')
  assert.match(w.messages.T1.find(x => /was closed as done/.test(x.content)).content, /#1/)
  assert.deepEqual(w.posts.P1.applied_tags, ['tag-Windows', 'tag-Hosting', 'tag-Missions', 'tag-Beta', 'tag-Fixed'])
  assert.equal(w.kv.get('z:7'), '1')
  assert.deepEqual(w.roles, ['PR'], 'Bug Zapper at PEST_COUNT')
  assert.equal(JSON.parse(w.kv.get('i:1')).done, true)
  const told = w.messages.T1.length
  await sync(env)
  assert.equal(w.messages.T1.length, told, 'told once')
  assert.equal(w.kv.get('z:7'), '1', 'counted once')
})

test('a ticket filed before the links were kept is recognised, not filed again', async () => {
  const w = world()
  w.messages.T1.push({ id: '20', author: { id: 'BOT' }, content: 'Copied to GitHub as #4: <https://github.com/o/r-old/issues/4>.' })
  await sync(env)
  assert.equal(w.issues.length, 0)
  assert.equal(w.kv.get('t:T1'), '4')
})
