import assert from 'node:assert/strict';
import { test } from 'node:test';
import { KdsApiClient } from '../../../src/Clients/Restaurantes.Clients.Kds.Web/wwwroot/kds/js/api-client.js';

test('KDS API client sends the bearer token and describes rejected responses', async () => {
  const originalFetch = globalThis.fetch;
  const api = new KdsApiClient('https://kds.test');
  const requests = [];
  const url = 'https://kds.test/api/restaurant-operations/restaurants/accessible/kds';

  try {
    globalThis.fetch = async (requestUrl, options) => {
      requests.push({ url: String(requestUrl), authorization: options.headers.get('Authorization') });
      return Response.json([{ id: 'restaurant-1' }]);
    };

    assert.deepEqual(await api.getKdsRestaurants(), [{ id: 'restaurant-1' }]);
    api.setAccessToken('access-token');
    await api.getKdsRestaurants();
    assert.deepEqual(requests, [
      { url, authorization: null },
      { url, authorization: 'Bearer access-token' },
    ]);

    globalThis.fetch = async () => new Response('unavailable', { status: 503 });
    await assert.rejects(api.getKdsRestaurants(), {
      message: 'No se pudieron cargar los locales KDS: HTTP 503',
    });
  } finally {
    globalThis.fetch = originalFetch;
  }
});
