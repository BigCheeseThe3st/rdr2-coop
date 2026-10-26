import test from 'node:test'
import assert from 'node:assert/strict'
import { emptyLedger, pence, isTest, record, due, announcement, reminder, monthOf } from '../src/kofi.js'

const tip = (id, amount, ts, more = {}) => ({ kofi_transaction_id: id, amount, currency: 'GBP', timestamp: ts, type: 'Donation', from_name: 'Someone', email: 'a@b.co', ...more })

test('amounts are read exactly', () => {
  assert.equal(pence('3.00'), 300); assert.equal(pence('3'), 300); assert.equal(pence('3.5'), 350); assert.equal(pence('12.34'), 1234)
  for (const bad of ['abc', '-1', '1.234', '', null]) assert.equal(pence(bad), null, String(bad))
})
test('tips add up per month and currency; retries and tests are not counted', () => {
  const l = emptyLedger(), now = Date.parse('2026-10-24T20:00:00Z')
  assert.equal(record(l, tip('t1', '3.00', '2026-10-24T19:00:00Z'), now), 'recorded')
  assert.equal(record(l, tip('t1', '3.00', '2026-10-24T19:00:00Z'), now), 'duplicate')
  assert.equal(record(l, tip('t2', '5.50', '2026-10-25T10:00:00Z'), now), 'recorded')
  assert.equal(record(l, tip('t3', '2.00', '2026-10-26T10:00:00Z', { currency: 'USD' }), now), 'recorded')
  assert.equal(record(l, tip('00000000-1111-2222-3333-444444444444', '3.00', '2026-10-24T19:00:00Z', { email: 'jo.example@example.com', from_name: 'Jo Example' }), now), 'test')
  assert.equal(isTest({ from_name: 'Jo Example' }), true)
  assert.deepEqual(l.months['2026-10'], { totals: { GBP: 850, USD: 200 }, count: 3, reported: false })
  assert.equal(JSON.stringify(l).includes('a@b.co'), false)              // no emails kept
  assert.equal(JSON.stringify(l).includes('Someone'), false)             // no names kept
})
test('a month is posted once, after it ends, only if money came in', () => {
  const l = emptyLedger()
  record(l, tip('t1', '4.00', '2026-10-24T19:00:00Z'), 0)
  assert.deepEqual(due(l, Date.parse('2026-10-31T23:59:00Z')), [])
  assert.deepEqual(due(l, Date.parse('2026-11-01T00:01:00Z')), ['2026-10'])
  l.months['2026-10'].reported = true
  assert.deepEqual(due(l, Date.parse('2026-11-01T00:02:00Z')), [])
  assert.equal(monthOf(Date.parse('2026-12-31T23:00:00Z')), '2026-12')
})
test('the posts say the amount and where it goes', () => {
  const m = { totals: { GBP: 1250 }, count: 4, reported: false }
  assert.match(announcement('2026-10', m), /October 2026: £12\.50 from 4 tips\.\*\* All of it.*Wildlife Conservation Society/)
  assert.match(reminder('2026-10', m), /£12\.50 \(4 tips\).*wcs\.org/)
  assert.match(announcement('2026-11', { totals: { GBP: 300 }, count: 1 }), /from 1 tip\./)
})
