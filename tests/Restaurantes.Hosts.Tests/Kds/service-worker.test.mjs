import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import { test } from 'node:test';

const workerUrl = new URL('../../../src/Clients/Restaurantes.Clients.Kds.Web/wwwroot/kds/service-worker.js', import.meta.url);
const source = await readFile(workerUrl, 'utf8');

function createWorker({ offline = false, cached, cacheFails = false } = {}) {
  const handlers = new Map();
  const writes = [];
  const errors = [];
  const context = vm.createContext({
    URL,
    console: { error: (...args) => errors.push(args) },
    self: {
      registration: { scope: 'https://test.local/kds/' },
      addEventListener: (name, handler) => handlers.set(name, handler),
    },
    fetch: async () => {
      if (offline) throw new Error('offline');
      return new Response('network');
    },
    caches: {
      match: async () => cached,
      open: async () => ({
        put: async (key, response) => {
          if (cacheFails) throw new Error('cache full');
          writes.push({ key, text: await response.text() });
        },
      }),
    },
  });
  vm.runInContext(source, context, { filename: fileURLToPath(workerUrl) });
  return {
    writes, errors,
    async request(path, mode = 'cors', method = 'GET') {
      const pending = [];
      let response;
      handlers.get('fetch')({
        request: { url: new URL(path, 'https://test.local').href, method, mode },
        respondWith: promise => { response = promise; },
        waitUntil: promise => pending.push(promise),
      });
      const result = await response;
      await Promise.all(pending);
      return result;
    },
  };
}

for (const [path, mode, key] of [
  ['/kds/', 'navigate', './index.html'],
  ['/kds/js/app.js', 'cors', '/kds/js/app.js'],
  ['/kds/css/kds.css', 'cors', '/kds/css/kds.css'],
]) {
  test(`stores a readable copy of ${path} while returning the network response`, async () => {
    const worker = createWorker();
    assert.equal(await (await worker.request(path, mode)).text(), 'network');
    assert.equal(worker.writes.length, 1);
    assert.equal(worker.writes[0].text, 'network');
    assert.equal(typeof worker.writes[0].key === 'string' ? worker.writes[0].key : new URL(worker.writes[0].key.url).pathname, key);
    assert.equal(worker.errors.length, 0);
  });

  test(`cache failure does not discard the successful response for ${path}`, async () => {
    const worker = createWorker({ cacheFails: true });
    assert.equal(await (await worker.request(path, mode)).text(), 'network');
    assert.equal(worker.errors.length, 1);
    assert.equal(worker.errors[0][1].message, 'cache full');
  });
}

test('offline navigation uses the saved page', async () => {
  const worker = createWorker({ offline: true, cached: new Response('saved page') });
  assert.equal(await (await worker.request('/kds/', 'navigate')).text(), 'saved page');
  assert.equal(worker.writes.length, 0);
});

test('ignores requests outside its scope and mutations', async () => {
  const worker = createWorker();
  assert.equal(await worker.request('/api/orders'), undefined);
  assert.equal(await worker.request('https://external.local/kds/app.js'), undefined);
  assert.equal(await worker.request('/kds/', 'cors', 'POST'), undefined);
  assert.equal(worker.writes.length, 0);
});
