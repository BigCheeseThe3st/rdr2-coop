// What the bug-report sync decides, kept apart from the network so it can be tested.

/** The GitHub issue number the bot already linked in this thread, or 0. */
export function linkedIssue(messages, botId, repo) {
  const re = new RegExp(`github\\.com/${repo.replace('/', '\\/')}/issues/(\\d+)`)
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

/** The issue for a new report. */
export function issueFor(thread, starter, tagNames, guildId) {
  const who = starter?.author?.global_name || starter?.author?.username || 'someone'
  const files = (starter?.attachments || []).map(a => a.filename)
  const lines = [
    `**Reported on Discord** by ${quiet(who)}: https://discord.com/channels/${guildId}/${thread.id}`,
    '',
    quiet(starter?.content || '(no text)'),
    '',
  ]
  if (tagNames.length) lines.push(`Tags: ${tagNames.join(', ')}`)
  if (files.length) lines.push(`Attachments (in the Discord post): ${files.map(quiet).join(', ')}`)
  lines.push('', '<sub>Copied automatically from the RDR2 Co-op Discord.</sub>')
  return { title: `[Discord] ${thread.name}`.slice(0, 250), body: lines.join('\n'), labels: ['bug'] }
}

/** The tag list with one more tag (a post can carry five at most). */
export function withTag(tags, tag) {
  if (tags.includes(tag) || tags.length >= 5) return tags
  return [...tags, tag]
}
