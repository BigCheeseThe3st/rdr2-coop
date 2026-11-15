import test from 'node:test'
import assert from 'node:assert/strict'
import { keyFor, fieldsFrom } from '../src/logic.js'

test('keys are safe and dated', () => {
  const k = keyFor({ version: '0.9.0-beta.6', name: 'Big Cheese/..', install: 'abc-123', kind: 'mod' }, Date.parse('2026-10-25T17:30:00Z'))
  assert.equal(k, 'logs/2026-10-25/0.9.0-beta.6/Big_Cheese_..-abc-123-20261025T173000.mod.log')
})
test('fields are checked', () => {
  const h = m => ({ get: k => m[k.toLowerCase()] ?? null })
  assert.equal(fieldsFrom(h({ 'x-coop-version': '0.9.0-beta.6', 'x-coop-install': 'deadbeef', 'x-coop-kind': 'host' })).kind, 'host')
  assert.equal(fieldsFrom(h({ 'x-coop-version': '../x', 'x-coop-install': 'deadbeef' })), null)
  assert.equal(fieldsFrom(h({ 'x-coop-version': '0.9.0', 'x-coop-install': 'no spaces here!' })), null)
  assert.equal(fieldsFrom(h({ 'x-coop-version': '0.9.0', 'x-coop-install': 'deadbeef', 'x-coop-kind': 'evil' })), null)
  assert.equal(fieldsFrom(h({ 'x-coop-version': '0.9.0-beta.8.4', 'x-coop-install': 'deadbeef', 'x-coop-kind': 'mission' })).kind, 'mission')
})
test('an upload without an install id gets the derived one (SendLogs=Yes typed by hand, before 0.9.0-beta.8)', () => {
  const h = m => ({ get: k => m[k.toLowerCase()] ?? null })
  const missing = fieldsFrom(h({ 'x-coop-version': '0.9.0-beta.7', 'x-coop-install': '' }), 'a1b2c3d4e5f6')
  assert.equal(missing.install, 'a1b2c3d4e5f6')
  assert.equal(missing.derived, true)
  assert.equal(fieldsFrom(h({ 'x-coop-version': '0.9.0-beta.7', 'x-coop-install': 'not hex!' }), 'a1b2c3d4e5f6').install, 'a1b2c3d4e5f6')
  assert.equal(fieldsFrom(h({ 'x-coop-version': '0.9.0-beta.7', 'x-coop-install': 'deadbeef' }), 'a1b2c3d4e5f6').install, 'deadbeef')
  assert.equal(fieldsFrom(h({ 'x-coop-version': '0.9.0-beta.7' })), null)   // no fallback: still refused
})
