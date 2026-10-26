import test from 'node:test'
import assert from 'node:assert/strict'
import { linkedIssue, toldClosed, issueFor, starterOf, formFields, reporterOf, trackerTags, trackerName, trackerIssue,
         trackerBody, swapTag, taggedBy, toldCounted } from '../src/logic.js'
import { bugReport } from '../../rooms-server/src/bugform.js'

const BOT = '1', REPO = 'BigCheeseThe3st/rdr2-coop'
const msg = (id, author, content, extra = {}) => ({ id, author: { id: author }, content, ...extra })
const IDS = { Windows: 'w', Linux: 'l', Hosting: 'h', Joining: 'j', Missions: 'm', Crash: 'c', Other: 'o', Beta: 'b', 'On GitHub': 'g', Fixed: 'f' }
// The form's own writer, so a change on either side breaks this test.
const FORM = bugReport('Ann', '7', { version: '0.9.0-beta.6', setup: 'linux-guest', kind: 'Missions', what: 'It floats.\nTwice.' })

test('finds the issue the bot linked, ignoring links from others, in any repo of ours', () => {
  const msgs = [msg('3', '9', 'see https://github.com/BigCheeseThe3st/rdr2-coop/issues/99'),
                msg('2', BOT, 'Copied to GitHub as #12: <https://github.com/BigCheeseThe3st/rdr2-coop-source/issues/12>')]
  assert.equal(linkedIssue(msgs, BOT, REPO), 12)
  assert.equal(linkedIssue(msgs.slice(0, 1), BOT, REPO), 0)
})

test('says a closed issue once', () => {
  assert.equal(toldClosed([msg('1', BOT, 'Fixed: GitHub #12 was closed as done.')], BOT, 12), true)
  assert.equal(toldClosed([msg('1', BOT, 'Fixed: GitHub #13 was closed as done.')], BOT, 12), false)
})

test('the oldest message is the starter', () => {
  const msgs = [msg('300', BOT, 'Copied'), msg('200', '7', 'later'), msg('1000', '7', 'newest'), msg('99', BOT, 'form report')]
  assert.equal(starterOf(msgs).content, 'form report')
  assert.equal(starterOf([]), undefined)
})

test("the form's report is read back: reporter, version, setup, kind, text", () => {
  const f = formFields(FORM)
  assert.deepEqual({ ...f, body: undefined }, { name: 'Ann', id: '7', version: '0.9.0-beta.6', platform: 'Linux', role: 'joining', kind: 'Missions', body: undefined })
  assert.equal(f.body, 'It floats.\nTwice.')
  assert.equal(formFields('just a message'), null)
  assert.equal(formFields(bugReport('B', '8', { version: '1', setup: 'x', kind: 'Crash', what: 'w' })).platform, '')
})

test('the reporter: from the form, from a moved report, or the thread owner, never the bot', () => {
  assert.equal(reporterOf({ content: FORM }, { owner_id: BOT }, BOT), '7')
  assert.equal(reporterOf({ content: '<@42> your bug report moved here from the old public forum' }, { owner_id: BOT }, BOT), '42')
  assert.equal(reporterOf({ content: 'my game crashes' }, { owner_id: '55' }, BOT), '55')
  assert.equal(reporterOf({ content: 'hello' }, { owner_id: BOT }, BOT), '')
})

test('the issue carries the reporter, the setup and the text, not the form footer, with no live @mentions', () => {
  const i = issueFor({ id: '55', name: 'Horse floats' }, { author: { id: BOT }, content: FORM }, 'G')
  assert.equal(i.title, '[Discord] Horse floats')
  assert.match(i.body, /by Ann \(private ticket\): https:\/\/discord.com\/channels\/G\/55/)
  assert.match(i.body, /Version 0.9.0-beta.6, Linux, joining \(Missions\)\n\nIt floats/)
  assert.doesNotMatch(i.body, /Drop your logs/)
  const own = issueFor({ id: '56', name: 'X' }, { author: { username: 'bo' }, content: 'hi @everyone', attachments: [{ filename: 'RDR2Coop.log' }] }, 'G')
  assert.match(own.body, /by bo .*@​everyone/s)
  assert.match(own.body, /RDR2Coop.log/)
})

test('tracker tags come from the answers, five at most, On GitHub always', () => {
  assert.deepEqual(trackerTags(formFields(FORM), IDS), ['l', 'j', 'm', 'b', 'g'])
  assert.deepEqual(trackerTags(null, IDS), ['g'])
  assert.deepEqual(trackerTags(formFields(bugReport('B', '8', { version: '0.7.3', setup: 'windows-host', kind: 'Crash', what: 'w' })), IDS), ['w', 'h', 'c', 'g'])
})

test('a tracker post is named after its issue and found by it', () => {
  assert.equal(trackerName(7, 'Horse floats'), '#7 · Horse floats')
  assert.equal(trackerIssue('#7 · Horse floats'), 7)
  assert.equal(trackerIssue('Horse floats #7'), 0)
  assert.ok(trackerName(1, 'x'.repeat(200)).length <= 100)
})

test("the tracker post links the ticket, the reporter (no ping) and the issue", () => {
  const b = trackerBody({ id: '55' }, { content: FORM }, '7', 9, REPO, 'G')
  assert.match(b, /<#55>/)
  assert.match(b, /<@7>/)
  assert.match(b, /issues\/9>/)
  assert.match(b, /\*\*Setup:\*\* Linux, joining · \*\*Kind:\*\* Missions\n\nIt floats/)
  assert.ok(trackerBody({ id: '1' }, { content: 'x'.repeat(3000) }, '', 1, REPO, 'G').length <= 2000)
})

test('Fixed replaces On GitHub, five tags at most', () => {
  assert.deepEqual(swapTag(['l', 'j', 'm', 'b', 'g'], 'g', 'f'), ['l', 'j', 'm', 'b', 'f'])
  assert.deepEqual(swapTag(['a', 'b', 'c', 'd', 'e'], 'g', 'f'), ['a', 'b', 'c', 'd', 'f'])
  assert.deepEqual(swapTag(undefined, 'g', 'f'), ['f'])
})

test('tagged suggestions are counted per poster, pinned guide and other tags left out', () => {
  const ts = [{ owner_id: 'a', applied_tags: ['D'] }, { owner_id: 'a', applied_tags: ['X'] }, { owner_id: 'a', applied_tags: ['D'], flags: 2 }, { owner_id: 'b', applied_tags: ['D'] }]
  assert.equal(taggedBy(ts, 'a', 'D'), 1)
})

test('a counted suggestion is counted once', () => {
  assert.equal(toldCounted([msg('1', BOT, "Built. That's 2 of your suggestions in the mod")], BOT), true)
  assert.equal(toldCounted([msg('1', '9', 'of your suggestions')], BOT), false)
})
