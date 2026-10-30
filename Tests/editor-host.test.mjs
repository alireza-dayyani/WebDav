import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import test from 'node:test'
import assert from 'node:assert/strict'

const source = readFileSync(new URL('../Front/public/editor-host.js', import.meta.url), 'utf8')
function bridge(letterMode = false) {
  const messages = [], calls = [], handlers = {}, store = new Map()
  const id = '11111111-1111-1111-1111-111111111111'
  store.set(id, { id, title: 'First document', version: 1, document: null })
  const parent = { postMessage: message => messages.push(message) }
  const window = { parent, addEventListener: (name, handler) => handlers[name] = handler }
  let fail = null, next = 2, email = 'writer@example.test'
  const fetch = async (url, options = {}) => {
    calls.push({ url, options })
    if (fail && options.method === 'PUT') return { ok: false, status: fail, json: async () => ({ detail: 'Concurrent edit' }) }
    let result
    if (url === '/api/csrf') result = { token: 'csrf-token' }
    else if (url === '/api/account/me') result = { email }
    else if (url === '/api/documents/') {
      const body = JSON.parse(options.body)
      result = { id: `new-${next++}`, title: body.title, version: 1 }
      store.set(result.id, { ...result })
    } else {
      const [, docId, route] = url.match(/\/api\/(?:documents|letters)\/([^/]+)\/(snapshot|content|editor)/) || []
      const document = store.get(docId)
      if (route === 'snapshot' || (route === 'editor' && !options.method)) result = { metadata: { id: document.id, title: document.title, version: document.version }, document: document.document, bodyBookmark: letterMode ? 'Body' : undefined }
      else {
        const body = JSON.parse(options.body)
        assert.equal(body.version, document.version, 'uses acknowledged version')
        document.document = body.document; document.version++
        result = { id: document.id, title: document.title, version: document.version }
      }
    }
    return { ok: true, status: 200, json: async () => result }
  }
  vm.runInNewContext(source, { window, fetch, URLSearchParams, location: { origin: 'http://localhost', search: letterMode ? `?letter=${id}&version=1` : `?id=${id}` } })
  return { host: window.inkstoneHost, calls, messages, store, id, parent, handlers, fail: code => fail = code, account: value => email = value }
}

test('loads a consistent snapshot; saves the complete model and advances acknowledged versions', async () => {
  const b = bridge()
  assert.equal(JSON.parse(await b.host.load()).title, 'First document')
  assert.equal(b.host.preferenceScope(), 'writer@example.test', 'editor preferences are scoped to the authenticated account')
  b.host.bind('generation-1')
  assert.equal(b.messages.find(m => m.type === 'inkstone:ready').document.version, 1, 'download controls receive the snapshot revision')
  const content = { body: [{ text: 'Hello سلام' }], media: { image: 'data' }, styles: { heading: {} } }
  await b.host.save(JSON.stringify(content), 'generation-1', 'First document')
  await b.host.save(JSON.stringify(content), 'generation-1', 'First document')
  assert.deepEqual(b.store.get(b.id).document, content)
  assert.equal(b.store.get(b.id).version, 3)
  assert.equal(b.calls.filter(c => c.options.method === 'POST').length, 0)
  assert.equal(b.calls.find(c => c.options.method === 'PUT').options.headers['X-CSRF-TOKEN'], 'csrf-token')
})

test('letter editor loads its own versioned model and never saves New/Open over the letter', async () => {
  const b = bridge(true)
  assert.equal(JSON.parse(await b.host.load()).bookmark, 'Body')
  b.host.bind('letter-generation')
  await b.host.save('{"body":[]}', 'letter-generation', 'Letter')
  assert.equal(b.store.get(b.id).version, 2)
  assert.equal(b.calls.find(c => c.options.method === 'PUT').url, `/api/letters/${b.id}/editor`)
  await assert.rejects(b.host.save('{"body":[]}', 'another-generation', 'New'), /Create another letter/)
  assert.equal(b.calls.filter(c => c.options.method === 'POST').length, 0)
  assert.equal(b.store.size, 1)
})
test('New/Open receives a separate document; a failed save retries the same entry', async () => {
  const b = bridge(); await b.host.load(); b.host.bind('1')
  b.fail(503)
  await assert.rejects(b.host.save('{"body":[]}', '2', 'Imported'), /Concurrent edit/)
  b.fail(null)
  await b.host.save('{"body":[]}', '2', 'Imported')
  assert.equal(b.calls.filter(c => c.options.method === 'POST').length, 1)
  assert.equal(b.store.get(b.id).version, 1, 'previous document is untouched')
})
test('a conflict pauses autosave and never overwrites the newer server content', async () => {
  const b = bridge(); await b.host.load(); b.host.bind('1'); b.fail(409)
  await assert.rejects(b.host.save('{"body":[]}', '1', 'First'), /Concurrent edit/)
  assert.equal(b.host.canAutosave(), false)
  b.fail(null)
  await assert.rejects(b.host.save('{"body":[]}', '1', 'First'), /changed in another tab/)
  assert.equal(b.store.get(b.id).version, 1)
})
test('save requests only accept the same-origin parent and are consumed once', () => {
  const b = bridge()
  b.handlers.message({ origin: 'https://attacker.example', source: b.parent, data: { type: 'inkstone:save-request' } })
  assert.equal(b.host.takeSaveRequest(), false)
  b.handlers.message({ origin: 'http://localhost', source: b.parent, data: { type: 'inkstone:save-request' } })
  assert.equal(b.host.takeSaveRequest(), true)
  assert.equal(b.host.takeSaveRequest(), false)
})
test('an acknowledgement from a replaced editor document does not change the current document', async () => {
  const b = bridge(); await b.host.load(); b.host.bind('1')
  b.host.state(true, false, '2')
  await b.host.save('{"body":[]}', '1', 'Old document')
  assert.equal(b.messages.filter(m => m.type === 'inkstone:saved').length, 0)
  assert.equal(b.store.get(b.id).version, 2, 'the old document still completes its own save')
})

test('a conflict in a replaced document does not disable saves or report errors on the new document', async () => {
  const b = bridge(); await b.host.load(); b.host.bind('1'); b.host.state(true, false, '2'); b.fail(409)
  await assert.rejects(b.host.save('{"body":[]}', '1', 'Old'))
  assert.equal(b.host.canAutosave(), true)
  assert.equal(b.messages.filter(m => m.type === 'inkstone:error').length, 0)
  b.fail(null); await b.host.save('{"body":[]}', '2', 'New')
  assert.equal(b.store.size, 2)
  b.host.state(true, false, '1'); assert.equal(b.host.canAutosave(), false)
})

test('switching accounts blocks saving or copying an open document into the new account', async () => {
  const b = bridge(); await b.host.load(); b.host.bind('1'); b.account('other@example.test')
  await assert.rejects(b.host.save('{"body":[]}', '1', 'Existing'), /account/i)
  await assert.rejects(b.host.save('{"body":[]}', '2', 'New'), /account/i)
  assert.equal(b.calls.filter(c => ['POST','PUT'].includes(c.options.method)).length, 0)
  b.account('writer@example.test'); await b.host.save('{"body":[]}', '1', 'Existing')
  assert.equal(b.store.get(b.id).version, 2)
})
