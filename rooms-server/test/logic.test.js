import test from 'node:test'
import assert from 'node:assert/strict'
import { empty, checkForm, cleanName, requestStart, requestStop, poll, report, statusText, statusButtons, voicePlan, checkVoice, OFFLINE_MS, RESEND_MS, JOB_TTL_MS, TRIES, TRY_WINDOW_MS } from '../src/logic.js'

const on = () => { const s = empty(); s.agentSeen = 1000; return s }
test('names are cleaned and forms checked', () => {
  assert.equal(cleanName('My ~r~posse<>'), 'My rposse')
  assert.equal(checkForm('A', 'secret').ok, false)
  assert.equal(checkForm('Posse', 'abc').ok, false)
  assert.equal(checkForm('Posse', 'abcd').ok, true)
  for (const bad of ['N1GG3R posse', 'n i g g a s', 'Big_N!gguh']) assert.equal(checkForm(bad, 'abcd').ok, false, bad)
  for (const fine of ['Niggle', 'Snow Nights', 'Egg nog gang']) assert.equal(checkForm(fine, 'abcd').ok, true, fine)
})
test('a start goes to the Pi once, with the password, and is resent if lost', () => {
  const s = on()
  assert.equal(requestStart(s, { owner: 'u1', owner_name: 'Arthur', name: 'Posse', password: 'pass1', token: 't1' }, 2000).ok, true)
  const jobs = poll(s, 3000)
  assert.equal(jobs.length, 1); assert.equal(jobs[0].password, 'pass1'); assert.equal(jobs[0].token, undefined)
  assert.equal(poll(s, 4000).length, 0)
  assert.equal(poll(s, 3000 + RESEND_MS + 1).length, 1)
})
test('the result reaches the player and the password is forgotten', () => {
  const s = on()
  requestStart(s, { owner: 'u1', name: 'Posse', password: 'pass1', token: 't1' }, 2000)
  const [job] = poll(s, 3000)
  const replies = report(s, { results: [{ id: job.id, ok: true, port: 27461 }], rooms: [{ port: 27461, owner: 'u1', owner_name: 'Arthur', name: 'Posse', players: 0, started: 3 }], capacity: 4 }, 4000)
  assert.equal(replies.length, 1); assert.match(replies[0].content, /is up/); assert.equal(replies[0].token, 't1')
  assert.equal(JSON.stringify(s).includes('pass1'), false)
  assert.match(statusText(s, 5000), /Posse\*\*: 0\/4 players \(started by Arthur\)/)
})
test('one each, a full house, offline and too quick are refused', () => {
  const s = on(); s.capacity = 1
  requestStart(s, { owner: 'u1', name: 'One', password: 'pass1', token: 't' }, 2000)
  assert.match(requestStart(s, { owner: 'u1', name: 'Two', password: 'pass2', token: 't' }, 2001).why, /already/)
  assert.match(requestStart(s, { owner: 'u2', name: 'Two', password: 'pass2', token: 't' }, 2002).why, /in use/)
  const off = empty(); assert.match(requestStart(off, { owner: 'u3', name: 'X1', password: 'pass3', token: 't' }, 10 + OFFLINE_MS).why, /offline/)
  const q = on(); q.lastStart.u4 = 1500
  assert.match(requestStart(q, { owner: 'u4', name: 'X2', password: 'pass4', token: 't' }, 2000).why, /wait/)
})
test('stop cancels a request or stops the room', () => {
  const s = on()
  requestStart(s, { owner: 'u1', name: 'Posse', password: 'pass1', token: 't' }, 2000)
  assert.match(requestStop(s, 'u1', 2100).why, /cancelled/); assert.equal(s.jobs.length, 0)
  report(s, { rooms: [{ port: 27462, owner: 'u2', name: 'Gang', players: 2 }] }, 3000)
  assert.match(requestStop(s, 'u2', 3100).why, /Stopping/)
  assert.deepEqual(poll(s, 3200).map(j => [j.action, j.port]), [['stop', 27462]])
})
test('old jobs and their passwords expire', () => {
  const s = on()
  requestStart(s, { owner: 'u1', name: 'Posse', password: 'pass1', token: 't' }, 2000)
  poll(s, 2000 + JOB_TTL_MS + 1)
  assert.equal(s.jobs.length, 0)
})

test('a server gets a voice chat locked to its password, gone when the server ends', () => {
  const s = on()
  requestStart(s, { owner: 'u1', name: 'Posse', password: 'pass1', salt: 'x', hash: 'H', token: 't1' }, 2000)
  const [job] = poll(s, 3000)
  assert.equal(job.hash, undefined)                                   // the Pi never sees the hash
  const room = { port: 27461, owner: 'u1', name: 'Posse', players: 0, started: 3 }
  const [r] = report(s, { results: [{ id: job.id, ok: true, port: 27461 }], rooms: [room], capacity: 4 }, 4000)
  assert.equal(r.port, '27461')
  assert.deepEqual(voicePlan(s), { create: ['27461'], remove: [] })
  assert.equal(checkVoice(s, 'u2', '27461', 'H', 4100).why.includes('being set up'), true)
  s.locks['27461'].voice = 'V1'
  assert.deepEqual(voicePlan(s), { create: [], remove: [] })
  assert.deepEqual(checkVoice(s, 'u2', '27461', 'H', 4200), { ok: true, channel: 'V1' })
  assert.equal(statusButtons(s)[1].components[0].custom_id, 'rooms:voice:27461')
  assert.match(statusText(s, 4300), /voice chat/)
  report(s, { results: [], rooms: [], capacity: 4 }, 5000)
  assert.deepEqual(voicePlan(s), { create: [], remove: ['27461'] })
})
test('wrong voice passwords are limited per person', () => {
  const s = on(); s.rooms = [{ port: 27461, name: 'P' }]; s.locks = { 27461: { hash: 'H', voice: 'V' } }
  for (let i = 0; i < TRIES; i++) assert.equal(checkVoice(s, 'u9', '27461', 'nope', 1000 + i).ok, false)
  assert.match(checkVoice(s, 'u9', '27461', 'H', 2000).why, /Too many/)       // even the right one, for now
  assert.equal(checkVoice(s, 'u8', '27461', 'H', 2000).ok, true)             // others aren't affected
  assert.equal(checkVoice(s, 'u9', '27461', 'H', 1000 + TRY_WINDOW_MS + TRIES).ok, true)
  assert.equal(checkVoice(s, 'u9', '27499', 'H', 2000).ok, false)            // no such server
})
test('servers started before voice chats existed get no button', () => {
  const s = on(); s.rooms = [{ port: 27462, name: 'Old' }]
  assert.equal(statusButtons(s).length, 1)
  assert.deepEqual(voicePlan(s), { create: [], remove: [] })
})
