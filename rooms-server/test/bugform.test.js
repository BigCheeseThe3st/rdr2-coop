import test from 'node:test'
import assert from 'node:assert/strict'
import { BUG_FORM, formValues, bugReport, SETUPS, KINDS } from '../src/bugform.js'

test('the form has five parts, each a labelled input or pick-list', () => {
  const c = BUG_FORM.data.components
  assert.equal(c.length, 5)
  assert.ok(c.every(x => x.type === 18 && x.label.length <= 45 && x.component.custom_id))
  assert.ok(c.every(x => x.component.type !== 4 || !('label' in x.component)))   // a text input's own label is deprecated inside a Label
})

test('answers are read from the Label layout and the old action-row layout', () => {
  const labelled = [{ type: 18, component: { type: 4, custom_id: 'title', value: 'Floats' } }, { type: 18, component: { type: 3, custom_id: 'setup', values: ['linux-guest'] } }]
  assert.deepEqual(formValues(labelled), { title: 'Floats', setup: 'linux-guest' })
  assert.deepEqual(formValues([{ type: 1, components: [{ type: 4, custom_id: 'what', value: 'x' }] }]), { what: 'x' })
})

test('the report header is one clean line each, whatever is typed', () => {
  const r = bugReport('**Ann**\n', '7', { version: '0.9.0-beta.6\n**', setup: 'windows-host', kind: 'Missions', what: 'It floats.' })
  const [l1, l2] = r.split('\n')
  assert.equal(l1, '**Bug report by Ann** (<@7>)')
  assert.equal(l2, `**Version:** 0.9.0-beta.6 · **Setup:** ${SETUPS['windows-host']} · **Kind:** Missions`)
  assert.match(r, /\n\nIt floats\.\n\nDrop your logs in here:/)
  assert.match(bugReport('B', '8', { version: '1', setup: 'nope', kind: 'Hacked', what: 'w' }), /Setup:\*\* not given · \*\*Kind:\*\* Other/)
  assert.ok(KINDS.includes('Other'))
})
