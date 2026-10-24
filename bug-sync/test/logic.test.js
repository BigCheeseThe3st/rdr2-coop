import test from 'node:test'
import assert from 'node:assert/strict'
import { linkedIssue, toldClosed, issueFor, withTag, quiet } from '../src/logic.js'

const BOT = '1', REPO = 'BigCheeseThe3st/rdr2-coop'
test('finds the issue the bot linked, ignoring links from others', () => {
  const msgs = [{ author: { id: '9' }, content: 'see https://github.com/BigCheeseThe3st/rdr2-coop/issues/99' },
                { author: { id: BOT }, content: 'Copied to GitHub as #12: <https://github.com/BigCheeseThe3st/rdr2-coop/issues/12>.' }]
  assert.equal(linkedIssue(msgs, BOT, REPO), 12)
  assert.equal(linkedIssue(msgs.slice(0, 1), BOT, REPO), 0)
})
test('says a closed issue once', () => {
  assert.equal(toldClosed([{ author: { id: BOT }, content: 'Fixed: GitHub #12 was closed as done.' }], BOT, 12), true)
  assert.equal(toldClosed([{ author: { id: BOT }, content: 'Copied to GitHub as #12' }], BOT, 12), false)
})
test('the issue carries the reporter, the text, tags and file names, with no live @mentions', () => {
  const i = issueFor({ id: '55', name: 'Horse floats' }, { author: { global_name: 'Plagre' }, content: 'ping @octocat', attachments: [{ filename: 'RDR2Coop.log' }] }, ['Crash', 'Windows'], '7')
  assert.equal(i.title, '[Discord] Horse floats')
  assert.match(i.body, /by Plagre: https:\/\/discord\.com\/channels\/7\/55/)
  assert.match(i.body, /Tags: Crash, Windows/)
  assert.match(i.body, /RDR2Coop\.log/)
  assert.ok(!i.body.includes('@octocat') && i.body.includes(quiet('@octocat')))
  assert.deepEqual(i.labels, ['bug'])
})
test('a tag is added once, and never past five', () => {
  assert.deepEqual(withTag(['a'], 'b'), ['a', 'b'])
  assert.deepEqual(withTag(['a', 'b'], 'b'), ['a', 'b'])
  assert.deepEqual(withTag(['a', 'b', 'c', 'd', 'e'], 'f'), ['a', 'b', 'c', 'd', 'e'])
})
