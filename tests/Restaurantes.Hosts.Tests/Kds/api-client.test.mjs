import assert from 'node:assert/strict';
import { test } from 'node:test';
import { KdsApiClient } from '../../../src/Clients/Restaurantes.Clients.Kds.Web/wwwroot/kds/js/api-client.js';

test('provider and login exchange return successful JSON and describe rejected responses', async () => {
  const originalFetch = globalThis.fetch;
  const api = new KdsApiClient('https://kds.test');

  try {
    globalThis.fetch = async (url, options) => {
      if (String(url).endsWith('/provider')) {
        assert.equal(options, undefined);
        return Response.json({ activeProvider: 'Google' });
      }
      assert.equal(String(url), 'https://kds.test/api/identity/auth/exchange');
      assert.equal(options.method, 'POST');
      assert.equal(options.headers['Content-Type'], 'application/json');
      assert.equal(options.body, JSON.stringify({ code: 'one-time-code' }));
      return Response.json({ accessToken: 'access-token' });
    };

    assert.deepEqual(await api.provider(), { activeProvider: 'Google' });
    assert.deepEqual(await api.exchange('one-time-code'), { accessToken: 'access-token' });

    globalThis.fetch = async () => new Response('unavailable', { status: 503 });
    await assert.rejects(api.provider(), { message: 'No se pudo consultar el proveedor.' });
    await assert.rejects(api.exchange('expired-code'), {
      message: 'La cuenta no está autorizada.',
    });
  } finally {
    globalThis.fetch = originalFetch;
  }
});
