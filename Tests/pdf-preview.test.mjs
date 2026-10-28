import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import vm from 'node:vm'

function client({ blocked = false, status = 200, type = 'application/pdf', closeDuringRender = false } = {}) {
  const events = [], intervals = new Map()
  const tab = { closed: false, opener: {}, location: { replace: url => events.push(['navigate', url]) }, close() { this.closed = true; events.push(['close']) } }
  const context = {
    FormData, Blob,
    window: { open: (...args) => { events.push(['open', ...args]); return blocked ? null : tab } },
    document: { createElement() { throw new Error('A preview must not trigger a download') } },
    URL: { createObjectURL: blob => { assert.equal(blob.type, 'application/pdf'); events.push(['create']); return 'blob:preview' }, revokeObjectURL: url => events.push(['revoke', url]) },
    setInterval: callback => { intervals.set(1, callback); return 1 },
    clearInterval: id => intervals.delete(id),
    fetch: async (path, options) => {
      events.push(['fetch', path, options])
      if (path === '/api/csrf') return { ok: true, json: async () => ({ token: 'fixture-token' }) }
      return { ok: status === 200, status, json: async () => ({ detail: 'Invalid letter', issues: [{ code: 'required' }] }),
        blob: async () => { if (closeDuringRender) tab.closed = true; return new Blob(['%PDF-1.7'], { type }) } }
    },
  }
  const source = readFileSync(new URL('../Front/src/api.js', import.meta.url), 'utf8').replace(/^export /gm, '')
  vm.runInNewContext(source + '\nthis.previewPdf = previewPdf;', context)
  return { preview: () => context.previewPdf('/letters/test/preview', { method: 'POST', body: { version: 3 } }), events, tab, intervals }
}

test('reserves a tab before awaiting CSRF/rendering, displays PDF without download, retains it until tab closes', async () => {
  const c = client()
  const pending = c.preview()
  assert.deepEqual(c.events[0], ['open', 'about:blank', '_blank'])
  await pending
  assert.equal(c.tab.opener, null)
  const request = c.events.find(e => e[1] === '/api/letters/test/preview')[2]
  assert.equal(request.credentials, 'same-origin')
  assert.equal(request.headers['X-CSRF-TOKEN'], 'fixture-token')
  assert.equal(JSON.parse(request.body).version, 3)
  assert.ok(c.events.some(e => e[0] === 'navigate' && e[1] === 'blob:preview'))
  c.intervals.get(1)()
  assert.ok(!c.events.some(e => e[0] === 'revoke'))
  c.tab.closed = true; c.intervals.get(1)()
  assert.ok(c.events.some(e => e[0] === 'revoke'))
  assert.equal(c.intervals.size, 0)
})

test('blocked popups give actionable error without rendering a PDF', async () => {
  const c = client({ blocked: true })
  await assert.rejects(c.preview(), e => e.code === 'preview_blocked')
  assert.equal(c.events.length, 1)
})

test('API validation errors close the reserved tab and retain the issue details', async () => {
  const c = client({ status: 422 })
  await assert.rejects(c.preview(), e => e.status === 422 && e.issues[0].code === 'required')
  assert.equal(c.tab.closed, true)
  assert.ok(!c.events.some(e => e[0] === 'create'))
})

test('a tab closed during rendering is not reopened', async () => {
  const c = client({ closeDuringRender: true }); await c.preview()
  assert.ok(!c.events.some(e => e[0] === 'create' || e[0] === 'navigate'))
})

test('an unexpected HTML response cannot become an executable preview tab', async () => {
  const c = client({ type: 'text/html' })
  await assert.rejects(c.preview(), e => e.status === 422)
  assert.equal(c.tab.closed, true)
  assert.ok(!c.events.some(e => e[0] === 'navigate'))
})
