// RDR2 Co-op bug-report sync. Every five minutes: each new post in the Discord
// #bug-reports forum is copied to a GitHub issue (the bot replies with the link
// and tags the post "On GitHub"); when that issue is closed, the post is told so
// (closed as done: tagged "Fixed"). Nothing is stored here: what has been done is
// read back from the bot's own replies in each post.
// Secrets: DISCORD_TOKEN (the bot), GITHUB_TOKEN (fine-grained, issues on REPO).
import { linkedIssue, toldClosed, issueFor, withTag } from './logic.js'

const MAX_THREADS = 7   // the free plan allows 50 requests a run; a post can take six

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

async function syncThread(env, t, tagNames) {
  const msgs = await discord(env, `/channels/${t.id}/messages?limit=50`)
  const issue = linkedIssue(msgs, env.BOT_ID, env.REPO)
  if (!issue) {
    if (t.thread_metadata?.archived) return   // only new, active posts get copied
    const starter = msgs.find(m => m.id === t.id) || await discord(env, `/channels/${t.id}/messages/${t.id}`)
    const made = await github(env, '/issues', 'POST', issueFor(t, starter, (t.applied_tags || []).map(id => tagNames[id]).filter(Boolean), env.GUILD_ID))
    await discord(env, `/channels/${t.id}/messages`, 'POST', { flags: 4,
      content: `Copied to GitHub as #${made.number}: <${made.html_url}>. You'll get a reply here when it's fixed.` })
    await discord(env, `/channels/${t.id}`, 'PATCH', { applied_tags: withTag(t.applied_tags || [], env.TAG_ON_GITHUB) })
    console.log(`filed ${t.id} as #${made.number}`)
    return
  }
  if ((t.applied_tags || []).includes(env.TAG_FIXED) || toldClosed(msgs, env.BOT_ID, issue)) return
  const gh = await github(env, `/issues/${issue}`)
  if (gh.state !== 'closed') return
  const done = gh.state_reason === 'completed'
  // A reply first: it reopens an archived post, which can't be tagged while archived.
  await discord(env, `/channels/${t.id}/messages`, 'POST', { flags: 4, content: done
    ? `Fixed: GitHub #${issue} was closed as done. The fix comes with the next release (see #announcements).`
    : `GitHub #${issue} was closed (${(gh.state_reason || 'closed').replace('_', ' ')}): <${gh.html_url}>` })
  if (done) await discord(env, `/channels/${t.id}`, 'PATCH', { applied_tags: withTag(t.applied_tags || [], env.TAG_FIXED) })
  console.log(`told ${t.id} that #${issue} was closed (${gh.state_reason})`)
}

export async function sync(env) {
  const forum = await discord(env, `/channels/${env.FORUM_ID}`)
  const tagNames = Object.fromEntries((forum.available_tags || []).map(x => [x.id, x.name]))
  const active = (await discord(env, `/guilds/${env.GUILD_ID}/threads/active`)).threads.filter(t => t.parent_id === env.FORUM_ID)
  const archived = (await discord(env, `/channels/${env.FORUM_ID}/threads/archived/public?limit=25`)).threads
  const todo = [...active, ...archived].filter(t => !(t.flags & 2)).slice(0, MAX_THREADS)   // the pinned guide is not a report
  for (const t of todo) {
    try { await syncThread(env, t, tagNames) } catch (e) { console.log(`post ${t.id}: ${e.message}`) }
  }
}

export default {
  async scheduled(event, env, ctx) { ctx.waitUntil(sync(env)) },
  async fetch() { return new Response('RDR2 Co-op bug-report sync (runs on its own every five minutes).\n') },
}
