// What the bug-report sync decides, kept apart from the network so it can be tested.

export const FIXED_MARK = '✅ '   // a fixed report's ticket name starts with this (private threads have no tags)

/** The GitHub issue number the bot already linked in this thread, or 0. */
export function linkedIssue(messages, botId, repo) {
  const re = new RegExp(`github\\.com/${repo.split('/')[0]}/[\\w.-]+/issues/(\\d+)`)   // any repo of ours: the tracker moved once, the numbers didn't
  for (const m of messages) {
    if (m.author?.id !== botId) continue
    const hit = re.exec(m.content || '')
    if (hit) return Number(hit[1])
  }
  return 0
}

/** Whether the bot already said the issue was closed (so it says it once). */
export function toldClosed(messages, botId, issue) {
  return messages.some(m => m.author?.id === botId && (m.content || '').includes(`#${issue} was closed`))
}

/** User text made safe for a public issue: no @mentions pinging GitHub users. */
export function quiet(text) {
  return (text || '').replace(/@/g, '@​')
}

/** The first message of a ticket: the bot's report from the form, or the reporter's own first message. */
export function starterOf(messages) {
  return [...messages].sort((a, b) => (a.id.length - b.id.length) || (a.id < b.id ? -1 : 1))[0]
}

/** The answers in a form report (rooms-server/src/bugform.js bugReport writes it: keep the two in step), or null. */
export function formFields(text) {
  const m = /^\*\*Bug report by (.*?)\*\* \(<@(\d+)>\)\n\*\*Version:\*\* (.*?) · \*\*Setup:\*\* (?:(Windows|Linux), (hosting|joining)|not given) · \*\*Kind:\*\* (.+)\n/.exec(text || '')
  return m && { name: m[1], id: m[2], version: m[3], platform: m[4] || '', role: m[5] || '', kind: m[6], body: (text.split('\n\n')[1] || '').trim() }
}

/** Who reported it: the form's reporter, the person a moved report was for, or whoever opened the thread. */
export function reporterOf(starter, thread, botId) {
  const text = starter?.content || ''
  const id = formFields(text)?.id || /^<@(\d+)> your bug report moved here/.exec(text)?.[1] || thread.owner_id
  return id && id !== botId ? id : ''
}

/** The issue for a new report. */
export function issueFor(thread, starter, guildId) {
  const f = formFields(starter?.content)
  const who = f ? f.name : (starter?.author?.global_name || starter?.author?.username || 'someone')
  const files = (starter?.attachments || []).map(a => a.filename)
  const text = f ? `Version ${f.version}${f.platform ? `, ${f.platform}, ${f.role}` : ''} (${f.kind})\n\n${f.body}` : (starter?.content || '(no text)')
  const lines = [`**Reported on Discord** by ${quiet(who)} (private ticket): https://discord.com/channels/${guildId}/${thread.id}`, '', quiet(text), '']
  if (files.length) lines.push(`Attachments (in the Discord ticket): ${files.map(quiet).join(', ')}`)
  lines.push('', '<sub>Copied automatically from the RDR2 Co-op Discord.</sub>')
  return { title: `[Discord] ${thread.name}`.slice(0, 250), body: lines.join('\n'), labels: ['bug'] }
}

/** The staff tracker post's tags (ids, five at most): platform, role, kind, Beta, On GitHub. */
export function trackerTags(fields, ids) {
  const names = fields ? [fields.platform, { hosting: 'Hosting', joining: 'Joining' }[fields.role], fields.kind, /-|beta/i.test(fields.version) && 'Beta'] : []
  return [...new Set([...names, 'On GitHub'].filter(n => n && ids[n]).map(n => ids[n]))].slice(-5)
}

/** A tracker post is named after its issue, so the two find each other. */
export const trackerName = (issue, title) => `#${issue} · ${title}`.slice(0, 100)
export const trackerIssue = name => Number(/^#(\d+) · /.exec(name || '')?.[1] || 0)

/** The tracker post's text: who, setup, the report, and where to answer. */
export function trackerBody(thread, starter, reporter, issue, repo, guildId) {
  const f = formFields(starter?.content)
  const head = `**Ticket:** <#${thread.id}> (private: reply to the reporter there)\n**Reporter:** ${reporter ? `<@${reporter}>` : 'unknown'} · **GitHub:** <https://github.com/${repo}/issues/${issue}>` +
    (f ? `\n**Version:** ${f.version} · **Setup:** ${f.platform ? `${f.platform}, ${f.role}` : 'not given'} · **Kind:** ${f.kind}` : '')
  const body = f ? f.body : (starter?.content || '(no text)')
  return `${head}\n\n${body}`.slice(0, 2000)
}

/** One tag swapped for another (Fixed replaces On GitHub), five at most. */
export function swapTag(tags, from, to) {
  const out = (tags || []).filter(t => t !== from && t !== to)
  return [...out.slice(0, 4), to]
}

/** How many of this member's posts carry the given tag (pinned guides aren't posts): the #suggestions forum. */
export function taggedBy(threads, ownerId, tag) {
  return threads.filter(t => t.owner_id === ownerId && !(t.flags & 2) && (t.applied_tags || []).includes(tag)).length
}

/** Whether the bot already counted this suggestion (so it says so once). */
export function toldCounted(messages, botId) {
  return messages.some(m => m.author?.id === botId && (m.content || '').includes('of your suggestions'))
}
