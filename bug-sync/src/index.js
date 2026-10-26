// RDR2 Co-op bug-report sync. Bug reports are private tickets in #bug-reports (the reporter, anyone
// they add, the moderators), mostly opened by the pinned post's form (rooms-server/src/bugform.js).
// Every five minutes each new ticket is copied to a GitHub issue (the bot replies with the link) and
// gets a post in the staff-only #bug-tracker forum, named "#<issue> · <title>" and tagged from the
// form's answers, so staff can browse and filter every bug in one place (the author, 25 Oct). When the
// issue is closed the ticket is told so; closed as done: ✅ in the ticket's name, Fixed on the tracker
// post, and a point for the reporter (PEST_COUNT earns PEST_ROLE, Bug Zapper). #suggestions posts
// tagged Done earn IDEA_ROLE (Ideas Machine) the same way.
// KV STATE keeps the links, so a run never has to scan every ticket: "t:<ticket>" -> issue number,
// "i:<issue>" -> {ticket, post, reporter, done}, "z:<member>" -> fixed reports.
// Secrets: DISCORD_TOKEN (the bot), GITHUB_TOKEN (fine-grained, issues on REPO).
import { linkedIssue, toldClosed, issueFor, starterOf, formFields, reporterOf, trackerTags, trackerName,
         trackerBody, swapTag, taggedBy, toldCounted, FIXED_MARK } from './logic.js'

const MAX_NEW = 3, MAX_CLOSED = 3, MAX_SUGGESTIONS = 5   // the free plan allows 50 requests a run

async function discord(env, path, method = 'GET', body) {
  const r = await fetch(`https://discord.com/api/v10${path}`, {
    method, body: body && JSON.stringify(body),
    headers: { Authorization: `Bot ${env.DISCORD_TOKEN}`, 'Content-Type': 'application/json', 'User-Agent': 'DiscordBot (https://github.com/BigCheeseThe3st/rdr2-coop, 1.0)' },
  })
  if (!r.ok) throw new Error(`Discord ${method} ${path}: ${r.status} ${(await r.text()).slice(0, 200)}`)
  return r.status === 204 ? null : r.json()
}

async function github(env, path, method = 'GET', body) {
  const r = await fetch(`https://api.github.com/repos/${env.REPO}${path}`, {
    method, body: body && JSON.stringify(body),
    headers: { Authorization: `Bearer ${env.GITHUB_TOKEN}`, Accept: 'application/vnd.github+json', 'User-Agent': 'rdr2coop-bug-sync' },
  })
  if (!r.ok) throw new Error(`GitHub ${method} ${path}: ${r.status} ${(await r.text()).slice(0, 200)}`)
  return r.json()
}

const getLink = async (env, issue) => JSON.parse(await env.STATE.get(`i:${issue}`) || 'null')
const putLink = (env, issue, link) => env.STATE.put(`i:${issue}`, JSON.stringify(link))

// A new ticket: its GitHub issue, the bot's reply with the link, and the staff tracker post.
async function file(env, t, tagIds) {
  const msgs = await discord(env, `/channels/${t.id}/messages?limit=50`)
  const old = linkedIssue(msgs, env.BOT_ID, env.REPO)   // filed before the links were kept
  if (old) return env.STATE.put(`t:${t.id}`, String(old))
  const starter = msgs.length < 50 ? starterOf(msgs) : starterOf(await discord(env, `/channels/${t.id}/messages?after=0&limit=5`))
  if (!starter) return   // nothing written yet
  const made = await github(env, '/issues', 'POST', issueFor(t, starter, env.GUILD_ID))
  await env.STATE.put(`t:${t.id}`, String(made.number))
  const reporter = reporterOf(starter, t, env.BOT_ID)
  await putLink(env, made.number, { ticket: t.id, reporter })
  await discord(env, `/channels/${t.id}/messages`, 'POST', { flags: 4,
    content: `Copied to GitHub as #${made.number}: <${made.html_url}>. You'll get a reply here when it's fixed.` })
  const post = await discord(env, `/channels/${env.TRACKER_FORUM}/threads`, 'POST', { name: trackerName(made.number, t.name),
    applied_tags: trackerTags(formFields(starter.content), await tagIds()),
    message: { content: trackerBody(t, starter, reporter, made.number, env.REPO, env.GUILD_ID), allowed_mentions: { parse: [] } } })
  await putLink(env, made.number, { ticket: t.id, reporter, post: post.id })
  console.log(`filed ${t.id} as #${made.number} (tracker ${post.id})`)
}

// A closed issue of ours: the ticket is told; done as fixed, the ticket and tracker post are marked
// and the reporter gets a point.
async function closed(env, gh, link, tagIds) {
  const done = gh.state_reason === 'completed'
  const t = await discord(env, `/channels/${link.ticket}`)
  if (!(t.name || '').startsWith(FIXED_MARK)) {
    // A reply first: it reopens an archived ticket, which can't be renamed while archived.
    await discord(env, `/channels/${t.id}/messages`, 'POST', { flags: 4, content: done
      ? `Fixed: GitHub #${gh.number} was closed as done. The fix comes with the next release (see #announcements).`
      : `GitHub #${gh.number} was closed (${(gh.state_reason || 'closed').replace('_', ' ')}): <${gh.html_url}>` })
    if (done) await discord(env, `/channels/${t.id}`, 'PATCH', { name: (FIXED_MARK + t.name).slice(0, 100) })
  }
  await putLink(env, gh.number, { ...link, done: true })   // before the extras: never told twice
  console.log(`told ${t.id} that #${gh.number} was closed (${gh.state_reason})`)
  if (!done) return
  if (link.post) {
    const post = await discord(env, `/channels/${link.post}`)
    const ids = await tagIds()
    if (post.thread_metadata?.archived) await discord(env, `/channels/${post.id}`, 'PATCH', { archived: false })
    await discord(env, `/channels/${post.id}`, 'PATCH', { applied_tags: swapTag(post.applied_tags, ids['On GitHub'], ids.Fixed) })
  }
  if (link.reporter) {
    const n = Number(await env.STATE.get(`z:${link.reporter}`) || 0) + 1
    await env.STATE.put(`z:${link.reporter}`, String(n))
    await award(env, link.reporter, t.id, n, env.PEST_ROLE, 'Bug Zapper', 'reports fixed')
  }
}

// The role at PEST_COUNT (given once), with a reply where it was earned. Returns the count.
async function award(env, userId, channelId, n, roleId, roleName, what) {
  if (!userId || !roleId || n < Number(env.PEST_COUNT || 5)) return n
  const member = await discord(env, `/guilds/${env.GUILD_ID}/members/${userId}`).catch(() => null)
  if (!member || member.roles.includes(roleId)) return n
  await discord(env, `/guilds/${env.GUILD_ID}/members/${userId}/roles/${roleId}`, 'PUT')
  await discord(env, `/channels/${channelId}/messages`, 'POST', { flags: 4, allowed_mentions: { parse: [] },
    content: `That's ${n} of your ${what}: you're now **${roleName}**. Thank you.` })
  console.log(`${roleName}: ${userId} (${n})`)
  return n
}

// Every post of a public forum: the active ones and up to four pages of the archive.
async function forumThreads(env, active, forumId) {
  const threads = active.filter(x => x.parent_id === forumId)
  let before
  for (let page = 0; page < 4; page++) {
    const r = await discord(env, `/channels/${forumId}/threads/archived/public?limit=25${before ? '&before=' + before : ''}`)
    threads.push(...r.threads)
    if (!r.has_more || !r.threads.length) break
    before = r.threads[r.threads.length - 1].thread_metadata?.archive_timestamp
  }
  return threads
}

// #suggestions (a public forum): a post tagged Done (by a moderator) is counted once, with a reply saying so.
async function countSuggestions(env, active) {
  if (!env.SUGGEST_FORUM || !env.TAG_DONE) return
  const all = await forumThreads(env, active, env.SUGGEST_FORUM)
  for (const t of all.filter(t => !(t.flags & 2) && (t.applied_tags || []).includes(env.TAG_DONE)).slice(0, MAX_SUGGESTIONS)) {
    try {
      const msgs = await discord(env, `/channels/${t.id}/messages?limit=50`)
      if (toldCounted(msgs, env.BOT_ID)) continue
      const n = await award(env, t.owner_id, t.id, taggedBy(all, t.owner_id, env.TAG_DONE), env.IDEA_ROLE, 'Ideas Machine', 'suggestions built')
      await discord(env, `/channels/${t.id}/messages`, 'POST', { flags: 4, allowed_mentions: { parse: [] },
        content: `Built. That's ${n} of your suggestions in the mod${n < Number(env.PEST_COUNT || 5) ? `; ${Number(env.PEST_COUNT || 5)} earns the Ideas Machine role` : ''}.` })
      console.log(`suggestion ${t.id} counted (${n})`)
    } catch (e) { console.log(`suggestion ${t.id}: ${e.message}`) }
  }
}

export async function sync(env, now = Date.now()) {
  let ids
  const tagIds = async () => (ids ??= Object.fromEntries(((await discord(env, `/channels/${env.TRACKER_FORUM}`)).available_tags || []).map(x => [x.name, x.id])))
  const active = (await discord(env, `/guilds/${env.GUILD_ID}/threads/active`)).threads
  // New tickets are active ones the store doesn't know yet, newest first.
  const fresh = []
  for (const t of active.filter(x => x.parent_id === env.BUG_CHANNEL).sort((a, b) => b.id.length - a.id.length || (b.id > a.id ? 1 : -1))) {
    if (fresh.length >= MAX_NEW) break
    if (!(await env.STATE.get(`t:${t.id}`))) fresh.push(t)
  }
  for (const t of fresh) { try { await file(env, t, tagIds) } catch (e) { console.log(`ticket ${t.id}: ${e.message}`) } }
  // Closures: issues closed in the last three days that came from a ticket and weren't told yet.
  const since = new Date(now - 3 * 86400_000).toISOString()
  let told = 0
  for (const gh of await github(env, `/issues?state=closed&since=${since}&sort=updated&direction=desc&per_page=30`)) {
    if (told >= MAX_CLOSED) break
    if (gh.pull_request) continue
    const link = await getLink(env, gh.number)
    if (!link?.ticket || link.done) continue
    told++
    try { await closed(env, gh, link, tagIds) } catch (e) { console.log(`issue #${gh.number}: ${e.message}`) }
  }
  await countSuggestions(env, active)
}

export default {
  async scheduled(event, env, ctx) { ctx.waitUntil(sync(env)) },
  async fetch() { return new Response('RDR2 Co-op bug-report sync (runs on its own every five minutes).\n') },
}
