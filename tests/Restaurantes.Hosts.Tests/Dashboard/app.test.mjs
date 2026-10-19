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
