import test from 'node:test'
import assert from 'node:assert/strict'
import { notListable, isPublicIpv4 } from '../src/index.js'

test('a shared (CGNAT) address is named as such, with the way out', () => {
  assert.match(notListable('2804:14c::1', '100.72.3.4'), /CGNAT.*#find-a-partner/)
})

test('IPv6 with no IPv4 from the router says so', () => {
  assert.match(notListable('2804:14c::1', undefined), /IPv6 only.*no UPnP/)
})

test('a private IPv4 is named without the full address', () => {
  const m = notListable('10.1.2.3', undefined)
  assert.match(m, /\(10\.x\)/)
  assert.doesNotMatch(m, /10\.1\.2\.3/)
})

test('public and non-public IPv4', () => {
  assert.equal(isPublicIpv4('81.2.69.160'), true)
  for (const a of ['100.64.0.1', '10.0.0.1', '192.168.1.1', '172.16.0.1', '127.0.0.1', '169.254.1.1', 'x']) assert.equal(isPublicIpv4(a), false, a)
})
