// The #bug-reports form (the author, 25 Oct: bug reports private per bug, "easy to go through like the
// old format"). The pinned post's button opens it; the answers become the reporter's private ticket,
// posted as one report message whose first two lines bug-sync reads back for the staff tracker's tags
// (bug-sync/src/logic.js formFields: keep the two in step).

export const SETUPS = { 'windows-host': 'Windows, hosting', 'windows-guest': 'Windows, joining', 'linux-host': 'Linux, hosting', 'linux-guest': 'Linux, joining' }
export const KINDS = ['Crash', 'Connection', 'Out of sync', 'Missions', 'Looks & animation', 'enRage', 'Installer', 'Other']

export const BUG_FORM = { type: 9, data: { custom_id: 'bugs:form', title: 'Report a bug', components: [
  { type: 18, label: 'What goes wrong, in a few words', component: { type: 4, custom_id: 'title', style: 1, min_length: 5, max_length: 80, required: true, placeholder: "Partner's horse floats after a cutscene" } },
  { type: 18, label: 'What you did and what happened', description: 'Step by step, and what you expected instead', component: { type: 4, custom_id: 'what', style: 2, min_length: 20, max_length: 1500, required: true } },
  { type: 18, label: 'Mod version', description: 'Press F10 in game: it shows at the top', component: { type: 4, custom_id: 'version', style: 1, min_length: 3, max_length: 30, required: true, placeholder: '0.9.0-beta.6' } },
  { type: 18, label: 'Your setup', component: { type: 3, custom_id: 'setup', required: true, options: Object.entries(SETUPS).map(([value, label]) => ({ label, value })) } },
  { type: 18, label: 'What kind of bug', component: { type: 3, custom_id: 'kind', required: true, options: KINDS.map(k => ({ label: k, value: k })) } },
] } }

/** A submitted form's answers by custom_id (text: its value, select: its first choice), for both modal layouts. */
export function formValues(components) {
  const out = {}
  for (const c of (components ?? []).flatMap(r => r.component ? [r.component] : (r.components ?? []))) out[c.custom_id] = c.values ? c.values[0] : c.value
  return out
}

const plain = s => String(s ?? '').replace(/[\r\n]+/g, ' ').replace(/[*_`~|]/g, '').trim()   // one line, no markdown that would break the header

/** The report posted in the ticket. Lines 1-2 are the header bug-sync parses. */
export function bugReport(name, id, v) {
  return `**Bug report by ${plain(name) || 'someone'}** (<@${id}>)\n` +
    `**Version:** ${plain(v.version)} · **Setup:** ${SETUPS[v.setup] ?? 'not given'} · **Kind:** ${KINDS.includes(v.kind) ? v.kind : 'Other'}\n\n` +
    `${String(v.what ?? '').trim()}\n\n` +
    'Drop your logs in here: `scripts\\RDR2Coop.log` from your game folder (and `RDR2Coop.Host.log` + `RDR2Coop.Core.log` if you have them). ' +
    'You can add the friend you played with to this ticket. The bot copies the report to GitHub within a few minutes and replies with the link; only your name and text go there.'
}
