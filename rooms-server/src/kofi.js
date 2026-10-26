// Ko-fi tips, counted for the Wildlife Conservation Society (the author: every tip goes to WCS, after the
// card payment fee). Ko-fi tells the worker about each payment; each month's total is posted in
// #announcements on the 1st and the author is reminded to make the donation. Only the amount, currency
// and Ko-fi's id are kept: no names, emails or messages.

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']
const SYMBOL = { GBP: '£', USD: '$', EUR: '€' }

export const emptyLedger = () => ({ months: {}, seen: [], lastTest: 0 })
export const monthOf = ms => new Date(ms).toISOString().slice(0, 7)   // 'YYYY-MM' (UTC)

/** "3.00" -> 300; anything else -> null. */
export function pence(amount) {
  const m = /^(\d{1,7})(?:\.(\d{1,2}))?$/.exec(String(amount ?? '').trim())
  return m ? Number(m[1]) * 100 + Number((m[2] ?? '0').padEnd(2, '0')) : null
}

/** Ko-fi's "Send test" payment, which isn't real money. */
export const isTest = d => /^0{8}-1111-2222-3333-4{12}$/.test(String(d.kofi_transaction_id ?? d.message_id ?? '')) ||
  /@example\.com$/i.test(String(d.email ?? '')) || d.from_name === 'Jo Example'

/** One Ko-fi payment (the parsed "data" field). Returns what happened, for the log. */
export function record(ledger, d, now) {
  if (isTest(d)) { ledger.lastTest = now; return 'test' }
  const id = String(d.kofi_transaction_id || d.message_id || '')
  const p = pence(d.amount), cur = String(d.currency || 'GBP').toUpperCase().slice(0, 3)
  if (!id || p === null || p <= 0) return 'ignored'
  if (ledger.seen.includes(id)) return 'duplicate'                    // Ko-fi retries until it gets a 200
  ledger.seen = [...ledger.seen, id].slice(-500)
  const when = Date.parse(d.timestamp) || now
  const m = ledger.months[monthOf(when)] ??= { totals: {}, count: 0, reported: false }
  m.totals[cur] = (m.totals[cur] ?? 0) + p
  m.count++
  return 'recorded'
}

/** Finished months with money in them that haven't been posted yet, oldest first. */
export const due = (ledger, now) => Object.keys(ledger.months).filter(k => k < monthOf(now) && !ledger.months[k].reported && ledger.months[k].count > 0).sort()

const money = totals => Object.entries(totals).map(([c, p]) => `${SYMBOL[c] ?? c + ' '}${(p / 100).toFixed(2)}`).join(' + ')
const monthName = key => `${MONTHS[Number(key.slice(5, 7)) - 1]} ${key.slice(0, 4)}`

/** The public post in #announcements. */
export function announcement(key, m) {
  return `**Ko-fi tips in ${monthName(key)}: ${money(m.totals)} from ${m.count} tip${m.count === 1 ? '' : 's'}.** ` +
    'All of it, after the card payment fees, goes to the Wildlife Conservation Society. Thank you! The donation confirmation will be posted for supporters.'
}

/** The private reminder to the author. */
export function reminder(key, m) {
  return `Ko-fi tips for ${monthName(key)}: ${money(m.totals)} (${m.count} tip${m.count === 1 ? '' : 's'}), posted in #announcements. ` +
    'Time to donate it (less the PayPal/Stripe fees) to WCS: <https://secure.wcs.org/donate/individual-giving>, then post the confirmation in #supporters with your personal details blacked out.'
}
