// What the log store decides, kept apart from the network so it can be tested.

/** A safe object key for one upload: logs/<day>/<version>/<name>-<install>-<time>.<kind>.log */
export function keyFor(fields, now) {
  const day = new Date(now).toISOString().slice(0, 10)
  const clean = (s, max) => String(s ?? '').replace(/[^A-Za-z0-9._-]/g, '_').slice(0, max) || 'x'
  const t = new Date(now).toISOString().replace(/[-:]/g, '').replace(/\..*/, '')
  return `logs/${day}/${clean(fields.version, 32)}/${clean(fields.name, 32)}-${clean(fields.install, 36)}-${t}.${clean(fields.kind, 16)}.log`
}

/** The upload's fields from the request headers, or null when something's off. A missing or malformed install id
 * (a player who typed SendLogs=Yes into the ini got none from mods before 0.9.0-beta.8) becomes `fallbackInstall`. */
export function fieldsFrom(headers, fallbackInstall) {
  const f = { name: headers.get('x-coop-name'), version: headers.get('x-coop-version'), install: headers.get('x-coop-install'),
              kind: headers.get('x-coop-kind') || 'mod', build: headers.get('x-coop-build') || '', why: headers.get('x-coop-why') || '' }
  if (!f.version || !/^[0-9][0-9A-Za-z.-]{0,31}$/.test(f.version)) return null
  if (!f.install || !/^[0-9a-f-]{8,36}$/i.test(f.install)) {
    if (!fallbackInstall) return null
    f.install = fallbackInstall
    f.derived = true
  }
  if (!['mod', 'host', 'core'].includes(f.kind)) return null
  return f
}
