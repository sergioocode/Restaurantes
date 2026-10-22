import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import { test } from 'node:test';

const appUrl = new URL('../../../src/Clients/Restaurantes.Clients.Dashboard.Web/wwwroot/dashboard/js/app.js', import.meta.url);
const appSource = await readFile(appUrl, 'utf8');

function loadRenderer() {
  const elements = new Map();
  const getElementById = id => {
    if (!elements.has(id)) elements.set(id, createElement());
    return elements.get(id);
  };
  const context = vm.createContext({
    Date,
    Headers,
    Intl,
    URL,
    URLSearchParams,
    console,
    document: {
      body: createElement(),
      getElementById,
    },
    fetch: async () => ({
      ok: true,
      json: async () => ({
        activeProvider: 'Microsoft',
        microsoftConfigured: true,
        googleConfigured: false,
      }),
    }),
    history: { replaceState() {} },
    location: { origin: 'https://dashboard.test', pathname: '/dashboard/', hash: '' },
    navigator: {},
    sessionStorage: {
      getItem: () => null,
      removeItem() {},
      setItem() {},
    },
    setTimeout: () => 1,
    clearTimeout() {},
    window: {
      location: { assign() {} },
      setTimeout: () => 1,
      addEventListener() {},
    },
  });
  const instrumentedSource = appSource.replace(
    /\}\)\(\);\s*$/u,
    'globalThis.__dashboardTest = { renderOrder };})();'
  );
  vm.runInContext(instrumentedSource, context, { filename: fileURLToPath(appUrl) });
  return context.__dashboardTest.renderOrder;
}

function createElement() {
  return {
    classList: { add() {}, remove() {}, toggle() {} },
    addEventListener() {},
  };
}

test('dashboard order renderer displays each payment method and all service labels', () => {
  const renderOrder = loadRenderer();
  const baseOrder = {
    orderId: '12345678-rest',
    source: 'Pos',
    orderStatus: 'Completed',
    paymentStatus: 'Paid',
    total: 10,
    lines: [
      {
        kitchenStatus: 'Ready',
        quantity: 1,
        productName: 'Agua',
        categoryName: 'Bebidas',
        preparationStationCode: 'DRINKS',
      },
    ],
    tableLabel: 'Mesa 1',
    customerName: 'Ana',
    paymentMethod: 'Cash',
  };

  const mixed = renderOrder({
    ...baseOrder,
    serviceMode: 'DineIn',
    paymentMethods: ['Cash', 'Card'],
  });
  assert.match(mixed, /Mesa 1/u);
  assert.match(mixed, /Cash/u);
  assert.match(mixed, /Card/u);
  assert.match(mixed, /Esperando despacho del chef/u);
  assert.match(mixed, /1 × Agua/u);
  assert.match(mixed, /12345678/u);

  const bar = renderOrder({ ...baseOrder, serviceMode: 'Bar', tableLabel: '' });
  assert.match(bar, /Barra/u);
  assert.match(bar, /Cash/u);

  const takeaway = renderOrder({
    ...baseOrder,
    serviceMode: 'Takeaway',
    customerName: '',
    paymentMethods: [],
  });
  assert.match(takeaway, /Para llevar · Sin nombre/u);
  assert.match(takeaway, /Cash/u);
});

async function loadDashboard(session, deadLetters = [], testOptions = {}) {
  const elements = new Map();
  const requests = [];
  const sockets = [];
  const scheduled = [];
  const getElementById = id => {
    if (!elements.has(id)) elements.set(id, { ...createElement(), hidden: false, value: '', innerHTML: '', textContent: '' });
    return elements.get(id);
  };
  const order = {
    orderId: 'order-12345678', restaurantId: 'restaurant-1', serviceMode: 'DineIn',
    tableLabel: 'Mesa 1', source: 'Pos', orderStatus: 'Completed', paymentStatus: 'Paid',
    paymentMethods: ['Cash', 'Card'], total: 20,
    lines: [{ quantity: 1, productName: 'Tortilla', categoryName: 'Tapas',
      preparationStationCode: 'GRILL', kitchenStatus: 'Ready' }],
  };
  const daily = {
    totalSales: 20, completedSales: 1, averageTicket: 20,
    topProducts: [{ name: '<Tortilla>', quantity: 1, sales: 20 }],
    restaurantRanking: [{ restaurantId: 'restaurant-1', tickets: 1, sales: 20 }],
    channels: [{ source: 'Pos', tickets: 1, sales: 20 }],
    paymentMethods: [{ method: 'Mixed', tickets: 1, sales: 20 }],
    operationalOrders: [order],
  };
  const json = value => ({ ok: true, json: async () => value });
  const context = vm.createContext({
    Date, Headers, Intl, URL, URLSearchParams, console,
    document: { body: createElement(), getElementById },
    fetch: async (url, options = {}) => {
      requests.push({ url: String(url), headers: options.headers });
      const path = new URL(url).pathname;
      if (path.endsWith('/provider')) return json({ activeProvider: 'Microsoft', microsoftConfigured: true });
      if (path.endsWith('/restaurants')) {
        if (testOptions.failRestaurants) throw new Error('restaurant list unavailable');
        return json([
          { id: 'restaurant-1', name: 'Centro' },
          { id: 'restaurant-2', name: 'Norte' },
        ]);
      }
      if (path.endsWith('/daily')) {
        if (testOptions.failDaily) throw new Error('daily unavailable');
        return json(daily);
      }
      if (path.endsWith('/health')) {
        if (testOptions.failHealth) throw new Error('health unavailable');
        return json([{ service: 'Orders', healthy: true, status: 'OK' }]);
      }
      if (path.endsWith('/dead-letters')) return json(deadLetters);
      if (path.endsWith('/negotiate')) return json({ connectionToken: 'connection' });
      throw new Error('Unexpected dashboard request: ' + url);
    },
    history: { replaceState() {} },
    location: { origin: 'https://dashboard.test', pathname: '/dashboard/', hash: '' },
    navigator: {},
    sessionStorage: {
      getItem: () => JSON.stringify(session),
      removeItem() {}, setItem() {},
    },
    setTimeout: (callback, delay) => {
      scheduled.push({ callback, delay });
      return scheduled.length;
    },
    clearTimeout() {},
    WebSocket: class {
      constructor(url) { this.url = url; this.sent = []; sockets.push(this); }
      send(message) { this.sent.push(message); }
      close() {}
    },
    window: {
      location: { hash: '', pathname: '/dashboard/', assign() {} },
      setTimeout: (callback, delay) => {
        scheduled.push({ callback, delay });
        return scheduled.length;
      },
      addEventListener() {},
    },
  });
  const instrumentedSource = appSource.replace(
    /\}\)\(\);\s*$/u,
    'globalThis.__dashboardTest = { load, logout, openDashboard, loadHealth, loadDeadLetters, schedule };})();'
  );
  vm.runInContext(instrumentedSource, context, { filename: fileURLToPath(appUrl) });
  for (let attempt = 0; attempt < 50 && !getElementById('operations').innerHTML; attempt++) {
    await new Promise(resolve => setImmediate(resolve));
  }
  assert.ok(getElementById('operations').innerHTML, 'dashboard did not load');
  return { context, getElementById, requests, sockets, scheduled, testOptions };
}

test('dashboard loads authorized local data, escapes product names and refreshes filters', async () => {
  const session = {
    accessToken: 'token', expiresAtUtc: '2099-01-01T00:00:00Z',
    user: { displayName: 'Gerente' },
    restaurants: [{ restaurantId: 'restaurant-1', role: 'Gerente', permissions: ['dashboard.read'] }],
  };
  const { context, getElementById, requests } = await loadDashboard(session);

  assert.equal(getElementById('currentUser').textContent, 'Gerente');
  assert.match(getElementById('products').innerHTML, /&lt;Tortilla&gt;/u);
  assert.match(getElementById('operations').innerHTML, /Centro/u);
  assert.match(getElementById('paymentMethods').innerHTML, /Mixto/u);
  assert.equal(getElementById('deadLetterAlert').hidden, true);
  assert.equal(getElementById('dashboardContent').hidden, false);
  assert.ok(requests.some(request => request.headers?.get('Authorization') === 'Bearer token'));

  getElementById('orderLimit').value = '25';
  await context.__dashboardTest.load();
  assert.ok(requests.some(request => request.url.includes('orderLimit=25')));
  context.__dashboardTest.logout();
  assert.equal(getElementById('loginView').hidden, false);
});

test('dashboard rejects unauthorized access and reports a failed data refresh', async () => {
  const session = {
    accessToken: 'manager-token', expiresAtUtc: '2099-01-01T00:00:00Z',
    user: { displayName: 'Gerente' },
    restaurants: [{ restaurantId: 'restaurant-1', role: 'Gerente', permissions: ['dashboard.read'] }],
  };
  const { context, getElementById, requests, testOptions } = await loadDashboard(session);
  const requestsBeforeDeniedAccess = requests.length;

  await assert.rejects(context.__dashboardTest.openDashboard({
    accessToken: 'unprivileged-token',
    user: { displayName: 'Cajero' },
    restaurants: [{ restaurantId: 'restaurant-1', role: 'Cajero', permissions: ['dashboard.read'] }],
  }), /Este perfil no tiene acceso al Dashboard/u);
  assert.equal(requests.length, requestsBeforeDeniedAccess);

  testOptions.failDaily = true;
  await context.__dashboardTest.load();
  assert.equal(getElementById('state').textContent, 'Error: daily unavailable');
});

test('dashboard alerts an administrator about pending dead letters', async () => {
  const session = {
    accessToken: 'admin-token', expiresAtUtc: '2099-01-01T00:00:00Z',
    user: { displayName: 'Admin' }, restaurants: [],
    allRestaurantsRoles: ['Admin'], allRestaurantsPermissions: ['dashboard.read'],
  };
  const { getElementById } = await loadDashboard(session, [
    { queue: 'orders.dead', available: true, messageCount: 3 },
    { queue: 'sales.dead', available: false, messageCount: 0 },
  ]);

  for (let attempt = 0; attempt < 50 && !getElementById('deadLetterAlert').textContent; attempt++) {
    await new Promise(resolve => setImmediate(resolve));
  }
  assert.match(getElementById('deadLetterAlert').textContent, /DLQ 3/u);
  assert.match(getElementById('deadLetterAlert').title, /orders.dead: 3 pendientes/u);
  assert.match(getElementById('deadLetterAlert').title, /sales.dead: no disponible/u);
});

test('dashboard falls back to the restaurant access list when discovery is unavailable', async () => {
  const session = {
    accessToken: 'manager-token', expiresAtUtc: '2099-01-01T00:00:00Z',
    user: { displayName: 'Gerente' },
    restaurants: [{ restaurantId: 'restaurant-1', role: 'Gerente', permissions: ['dashboard.read'] }],
  };
  const { getElementById, requests } = await loadDashboard(session, [], { failRestaurants: true });

  assert.equal(getElementById('currentUser').textContent, 'Gerente');
  assert.match(getElementById('operations').innerHTML, /Mesa 1/u);
  assert.ok(requests.some(request => request.url.endsWith('/api/restaurant-operations/restaurants')));
});

test('dashboard reports unavailable health and an empty dead-letter queue', async () => {
  const session = {
    accessToken: 'admin-token', expiresAtUtc: '2099-01-01T00:00:00Z',
    user: { displayName: 'Admin' }, restaurants: [],
    allRestaurantsRoles: ['Admin'], allRestaurantsPermissions: ['dashboard.read'],
  };
  const { getElementById } = await loadDashboard(session, [], { failHealth: true });

  assert.match(getElementById('health').innerHTML, /Error al consultar/u);
  assert.match(getElementById('deadLetterAlert').textContent, /DLQ 0/u);
  assert.match(getElementById('deadLetterAlert').title, /No hay mensajes muertos/u);
});

test('dashboard joins realtime updates and schedules a refresh after a notification', async () => {
  const session = {
    accessToken: 'manager-token', expiresAtUtc: '2099-01-01T00:00:00Z',
    user: { displayName: 'Gerente' },
    restaurants: [{ restaurantId: 'restaurant-1', role: 'Gerente', permissions: ['dashboard.read'] }],
  };
  const { context, getElementById, sockets, scheduled } = await loadDashboard(session);
  for (let attempt = 0; attempt < 50 && sockets.length === 0; attempt++) {
    await new Promise(resolve => setImmediate(resolve));
  }
  assert.equal(sockets.length, 1);

  const socket = sockets[0];
  const separator = String.fromCodePoint(30);
  socket.onopen();
  socket.onmessage({ data: '{}' + separator });
  assert.equal(getElementById('state').textContent, 'Conectado');
  assert.match(socket.sent[1], /JoinRestaurants/u);

  socket.onmessage({
    data: JSON.stringify({ type: 1, target: 'DashboardUpdated', arguments: [] }) + separator,
  });
  const refresh = scheduled.findLast(item => item.delay === 200);
  assert.ok(refresh);
  await refresh.callback();
  assert.equal(getElementById('operations').innerHTML.includes('Mesa 1'), true);
  socket.onclose();
  assert.ok(scheduled.some(item => item.delay === 2000));
  context.__dashboardTest.logout();
});
