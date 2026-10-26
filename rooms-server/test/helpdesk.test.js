import test from 'node:test'
import assert from 'node:assert/strict'
import { emptyHelp, newMessages, allowed, counted, redact, clean, looksLikeQuestion, transcript, parseDecision, unsafeLink, ask, imageType, SYSTEM, MODEL,
         USER_PER_HOUR, DAY_CALLS, FRESH_MS } from '../src/helpdesk.js'
import { KB } from '../src/helpdesk-kb.js'

const BOT = '900'
const msg = (id, uid, content, ts = 0, more = {}) => ({ id: String(id), type: 0, content, timestamp: new Date(ts).toISOString(), author: { id: uid, username: 'u' + uid }, ...more })

test('only players\' new messages, oldest first; on first look only fresh ones', () => {
  const now = 100 * 60_000
  const page = [msg(5, 'p1', 'still stuck', now - 1000), msg(4, BOT, 'try this', now - 2000), msg(3, 'p2', 'me too', now - 3000, { author: { id: 'b2', bot: true } }),
                msg(2, 'p1', 'help', now - FRESH_MS - 1), { ...msg(6, 'p1', '', now), type: 7 }]
  assert.deepEqual(newMessages(page, '1', BOT, now).map(m => m.id), ['2', '5'])
  assert.deepEqual(newMessages(page, '4', BOT, now).map(m => m.id), ['5'])
  assert.deepEqual(newMessages(page, undefined, BOT, now).map(m => m.id), ['5'])
  assert.deepEqual(newMessages([msg('1563700000000000010', 'p', 'a'), msg('1563700000000000009', 'p', 'b')], '1563700000000000009', BOT, 0).map(m => m.id), ['1563700000000000010'])
})
test('calls are limited per player per hour and per day', () => {
  const h = emptyHelp(), now = Date.parse('2026-10-25T12:00:00Z')
  for (let i = 0; i < USER_PER_HOUR; i++) { assert.equal(allowed(h, 'p1', now + i), true); counted(h, 'p1', now + i) }
  assert.equal(allowed(h, 'p1', now + 100), false)
  assert.equal(allowed(h, 'p2', now + 100), true)
  assert.equal(allowed(h, 'p1', now + 3600_000 + 100), true)
  h.calls = DAY_CALLS
  assert.equal(allowed(h, 'p3', now + 200), false)
  assert.equal(allowed(h, 'p3', Date.parse('2026-10-26T00:00:01Z')), true)   // a new day
})
test('passwords never leave, posts never ping', () => {
  assert.equal(redact('Name=Arthur\nPassword=hunter2\n  password = x y\nPort=27460'), 'Name=Arthur\nPassword=<hidden>\n  password =<hidden>\nPort=27460')
  assert.equal(redact('python3 relay.py --password s3cret --port 1'), 'python3 relay.py --password <hidden> --port 1')
  assert.equal(clean('hi @everyone and @here <@123> <@&456> <#789>'), 'hi @​everyone and @​here   <#789>')
  assert.equal(clean('x'.repeat(5000)).length, 1900)
})
test('chat messages that might be asking something', () => {
  for (const q of ['how do i join my mate?', "f10 doesn't work", 'Is there a beta', 'UPnP problem', 'cant connect']) assert.equal(looksLikeQuestion(q), true, q)
  for (const n of ['gg lads', 'that was class', 'see you tonight']) assert.equal(looksLikeQuestion(n), false, n)
})
test('the transcript says who spoke and carries the files', () => {
  const t = transcript([msg(1, 'p1', 'it says no upnp', 0, { attachments: [{ id: 'a1', filename: 'RDR2Coop.log' }, { id: 'a2', filename: 'shot.png' }] }),
                        msg(2, BOT, 'Turn UPnP on'), msg(3, 's1', 'on it')], BOT, id => id === 's1', { a1: 'no UPnP router' })
  assert.match(t, /^player up1: it says no upnp\n\[file RDR2Coop.log\]\nno UPnP router\n\[end of RDR2Coop.log\] \[attached: shot.png\]/)
  assert.match(t, /RDR2 Co-op helper \(you\): Turn UPnP on/)
  assert.match(t, /staff: on it$/)
})
test('anything odd from the model becomes a hand-off', () => {
  const text = s => ({ stop_reason: 'end_turn', content: [{ type: 'thinking', thinking: '' }, { type: 'text', text: s }] })
  assert.deepEqual(parseDecision(text('{"action":"answer","reply":"Do X","topic":"C3"}')), { action: 'answer', reply: 'Do X', topic: 'C3' })
  assert.equal(parseDecision(text('not json')).action, 'handoff')
  assert.equal(parseDecision(text('{"action":"shout","reply":"x","topic":""}')).action, 'handoff')
  assert.equal(parseDecision({ stop_reason: 'refusal', content: [] }).action, 'handoff')
  assert.equal(parseDecision({ stop_reason: 'max_tokens', content: [] }).action, 'handoff')
})
test('the request: Haiku, low effort, the script cached, pictures before the text', async () => {
  let sent
  const client = { messages: { create: async p => { sent = p; return { stop_reason: 'end_turn', content: [{ type: 'text', text: '{"action":"none","reply":"","topic":"thanks"}' }], usage: { input_tokens: 9 } } } } }
  const d = await ask(client, { where: '#help ticket "x"', versions: { stable: '0.7.2', beta: '0.8.0-beta.2' }, text: 'player a: thanks', images: [{ media_type: 'image/png', data: 'AAAA' }] })
  assert.equal(d.action, 'none'); assert.deepEqual(d.usage, { input_tokens: 9 })
  assert.equal(sent.model, MODEL); assert.equal(MODEL, 'claude-haiku-5-5')
  assert.deepEqual(sent.output_config.effort, 'low'); assert.equal(sent.output_config.format.type, 'json_schema')
  assert.equal(sent.system[0].text, SYSTEM); assert.deepEqual(sent.system[0].cache_control, { type: 'ephemeral' })
  assert.equal(sent.messages[0].content[0].type, 'image')
  assert.match(sent.messages[0].content[1].text, /stable 0\.7\.2, beta 0\.8\.0-beta\.2\.\nWhere: #help ticket "x"\n\nplayer a: thanks$/)
  assert.ok(SYSTEM.endsWith(KB))
})
test('pictures are sent with their real type', () => {
  assert.equal(imageType(new Uint8Array([0x89, 0x50, 0x4e, 0x47, 13, 10])), 'image/png')
  assert.equal(imageType(new Uint8Array([0xff, 0xd8, 0xff, 0xe0])), 'image/jpeg')
  assert.equal(imageType(new Uint8Array([0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50])), 'image/webp')
  assert.equal(imageType(new Uint8Array([0x3c, 0x68, 0x74])), null)
})

test("links the script uses pass; any other link stops the answer being posted", () => {
  for (const ok of ['<https://github.com/BigCheeseThe3st/rdr2-coop/releases>', 'https://www.nexusmods.com/reddeadredemption2/mods/11238',
                    '<https://www.dev-c.com/rdr2/scripthookrdr2/>', 'https://ko-fi.com/rdr2_coop', 'https://discord.gg/NAYzjAaQDs',
                    'https://github.com/Halen84/ScriptHookRDR2DotNet-V2/releases/tag/2.2.0', 'press **F10**, file RDR2Coop.log, RDR2-Coop-0.7.3.zip'])
    assert.equal(unsafeLink(ok), null, ok)
  for (const bad of ['get the fix at https://evil.example/fix.exe', '[the fix](https://github.com/someone-else/fix)', 'www.evil.example',
                     'https://ko-fi.com/rdr2_coopx', 'https://discord.gg/other', 'http://nexusmods.com.evil.example/reddeadredemption2/'])
    assert.ok(unsafeLink(bad), bad)
  const d = parseDecision({ stop_reason: 'end_turn', content: [{ type: 'text', text: JSON.stringify({ action: 'answer', reply: 'Download <https://evil.example/fix.exe>', topic: 'C1' }) }] })
  assert.equal(d.action, 'handoff')
  assert.equal(d.reply, '')
  assert.doesNotMatch(d.topic, /evil/)   // the topic goes to the helpers' channel: the link isn't repeated there
})
