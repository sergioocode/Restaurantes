import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';

const kdsAuthUrl = new URL(
  '../../../src/Clients/Restaurantes.Clients.Kds.Web/wwwroot/kds/js/auth-client.js',
  import.meta.url
);
const dashboardAuthUrl = new URL(
  '../../../src/Clients/Restaurantes.Clients.Dashboard.Web/wwwroot/dashboard/js/auth-client.js',
  import.meta.url
);

await import(kdsAuthUrl.href);
const { createAuthClient } = globalThis.RestaurantesAuth;

function withBrowser({ hash = '', stored = null, fetchImpl } = {}) {
  const original = new Map();
  const replaceGlobal = (name, value) => {
    original.set(name, Object.getOwnPropertyDescriptor(globalThis, name));
    Object.defineProperty(globalThis, name, { configurable: true, writable: true, value });
  };
  const state = { assigned: [], replaced: [], storage: new Map(), fetches: [] };
  if (stored) state.storage.set('test.login', JSON.stringify(stored));
  replaceGlobal('window', {
    location: { hash, pathname: '/app/', assign: url => state.assigned.push(url) },
  });
  replaceGlobal('history', { replaceState: (...args) => state.replaced.push(args) });
  replaceGlobal('sessionStorage', {
    getItem: key => state.storage.get(key) ?? null,
    setItem: (key, value) => state.storage.set(key, value),
    removeItem: key => state.storage.delete(key),
  });
  replaceGlobal('fetch', async (url, options) => {
    state.fetches.push({ url: String(url), options });
    return fetchImpl(url, options);
  });
  state.restore = () => {
    for (const [name, descriptor] of original) {
      if (descriptor) Object.defineProperty(globalThis, name, descriptor);
      else delete globalThis[name];
    }
  };
  return state;
}

const newClient = () => createAuthClient({
  appPath: '/app/',
  sessionKey: 'test.login',
  baseUrl: 'https://app.test',
});

test('KDS and Dashboard ship the same shared auth client', async () => {
  assert.equal(
    await readFile(kdsAuthUrl, 'utf8'),
    await readFile(dashboardAuthUrl, 'utf8')
  );
});

test('login and logout navigate through the identity endpoints and clear the stored session', () => {
  const browser = withBrowser({ stored: { accessToken: 'token' } });
  try {
    const auth = newClient();
    auth.startLogin();
    auth.startLogin(true);
    auth.logout();

    assert.deepEqual(browser.assigned, [
      '/api/identity/auth/start?returnPath=%2Fapp%2F',
      '/api/identity/auth/start?returnPath=%2Fapp%2F&reauthenticate=true',
      '/api/identity/auth/logout?returnPath=%2Fapp%2F',
    ]);
    assert.equal(browser.storage.has('test.login'), false);
  } finally {
    browser.restore();
  }
});

test('login button starts a normal or reauthenticated login according to its mode', () => {
  const browser = withBrowser();
  try {
    const auth = newClient();
    const button = { dataset: {}, textContent: '', addEventListener(_, listener) { this.click = listener; } };
    auth.bindLoginButton(button);

    auth.prepareLoginButton(button);
    assert.equal(button.textContent, 'Iniciar sesión');
    button.click();
    auth.prepareLoginButton(button, true);
    assert.equal(button.textContent, 'Cambiar usuario');
    button.click();
    auth.prepareLoginButton(button);
    assert.equal('reauthenticate' in button.dataset, false);

    assert.deepEqual(browser.assigned, [
      '/api/identity/auth/start?returnPath=%2Fapp%2F',
      '/api/identity/auth/start?returnPath=%2Fapp%2F&reauthenticate=true',
    ]);
  } finally {
    browser.restore();
  }
});

test('a login code is exchanged, removed from the URL and stored', async () => {
  const session = { accessToken: 'access-token', expiresAtUtc: '2099-01-01T00:00:00Z' };
  const browser = withBrowser({
    hash: '#login_code=one-time-code',
    fetchImpl: async () => Response.json(session),
  });
  try {
    assert.deepEqual(await newClient().resolveSession(), { session });

    assert.deepEqual(browser.replaced, [[null, '', '/app/']]);
    assert.equal(browser.fetches[0].url, 'https://app.test/api/identity/auth/exchange');
    assert.equal(browser.fetches[0].options.method, 'POST');
    assert.equal(browser.fetches[0].options.body, JSON.stringify({ code: 'one-time-code' }));
    assert.equal(browser.storage.get('test.login'), JSON.stringify(session));
  } finally {
    browser.restore();
  }
});

test('a rejected exchange asks the user to choose another account', async () => {
  const browser = withBrowser({
    hash: '#login_code=expired',
    fetchImpl: async () => new Response('denied', { status: 400 }),
  });
  try {
    await assert.rejects(newClient().resolveSession(), error =>
      error.message === 'La cuenta no está autorizada.' && error.reauthenticate === true);
  } finally {
    browser.restore();
  }
});

test('a provider rejection is reported without calling the API', async () => {
  const browser = withBrowser({ hash: '#login_error=access_denied' });
  try {
    const result = await newClient().resolveSession();

    assert.match(result.message, /proveedor rechazó el acceso/u);
    assert.equal(result.session, undefined);
    assert.deepEqual(browser.replaced, [[null, '', '/app/']]);
  } finally {
    browser.restore();
  }
});

test('stored sessions are restored until they expire or become unreadable', async () => {
  const valid = { accessToken: 'token', expiresAtUtc: '2099-01-01T00:00:00Z' };
  let browser = withBrowser({ stored: valid });
  try {
    assert.deepEqual(await newClient().resolveSession(), { session: valid });
  } finally {
    browser.restore();
  }

  browser = withBrowser({ stored: { accessToken: 'old', expiresAtUtc: '2000-01-01T00:00:00Z' } });
  try {
    assert.deepEqual(await newClient().resolveSession(), {});
    assert.equal(browser.storage.has('test.login'), false);
  } finally {
    browser.restore();
  }

  browser = withBrowser();
  browser.storage.set('test.login', '{not json');
  try {
    assert.deepEqual(await newClient().resolveSession(), {});
    assert.equal(browser.storage.has('test.login'), false);
  } finally {
    browser.restore();
  }

  browser = withBrowser();
  try {
    assert.deepEqual(await newClient().resolveSession(), {});
  } finally {
    browser.restore();
  }
});
