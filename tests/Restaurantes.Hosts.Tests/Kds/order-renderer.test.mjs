import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { OrderRenderer } from '../../../src/Clients/Restaurantes.Clients.Kds.Web/wwwroot/kds/js/order-renderer.js';

const originalWindow = globalThis.window;
globalThis.window = { innerWidth: 1366 };
after(() => {
  if (originalWindow === undefined) delete globalThis.window;
  else globalThis.window = originalWindow;
});

class Element {
  children = [];
  dataset = {};
  textContent = '';
  style = { setProperty() {} };
  listeners = new Map();
  attributes = new Map();
  classList = { toggle() {} };
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  addEventListener(name, listener) { this.listeners.set(name, listener); }
  setAttribute(name, value) { this.attributes.set(name, value); }
}

function createOrder() {
  return {
    id: 'order-12345678', status: 'Ready', source: 'Pos', tableLabel: 'Mesa 1',
    readyAtUtc: '2026-10-17T00:00:00Z', guestCount: 2,
    stations: [
      { code: 'CHEF', name: 'Chef', status: 'Ready', priority: 0, requiresPrimaryDispatch: false },
      { code: 'GRILL', name: 'Parrilla', status: 'Ready', priority: 1, requiresPrimaryDispatch: true },
      { code: 'DRINKS', name: 'Bebidas', status: 'Dispatched', priority: 2, requiresPrimaryDispatch: false },
    ],
    lines: [
      { id: 'line-1', quantity: 1, productName: 'Plato', categoryName: 'Carta', kitchenStatus: 'Ready', preparationStationCode: 'GRILL' },
      { id: 'line-2', quantity: 1, productName: 'Agua', categoryName: 'Bebidas', kitchenStatus: 'Dispatched', preparationStationCode: 'DRINKS' },
    ],
  };
}

function createRenderer(stationCode, pending = false) {
  const renderer = Object.create(OrderRenderer.prototype);
  renderer.element = new Element();
  renderer.collapsedOrders = new Set();
  renderer.options = {
    getStationCode: () => stationCode,
    getPrimaryStationCode: () => 'CHEF',
    isOrderPending: () => pending,
    onAdvance: () => {},
  };
  return { renderer };
}

function firstRenderedOrder(renderer) {
  return renderer.element.children
    .flatMap(column => column.children)
    .find(child => child.className === 'order');
}

test('station order keeps header, badges, lines and collapse state', () => {
  const previousDocument = globalThis.document;
  globalThis.document = { createElement: () => new Element() };
  try {
    const order = createOrder();
    const { renderer } = createRenderer('GRILL');
    renderer.render([order]);
    const article = firstRenderedOrder(renderer);
    const [title, body] = article.children;
    const [titleContent, headerMeta, toggle] = title.children;
    assert.equal(article.dataset.status, 'Ready');
    assert.deepEqual(titleContent.children.map(child => child.textContent), ['order-12']);
    assert.doesNotMatch(titleContent.textContent, /Mesa 1/u);
    assert.equal(headerMeta.children[0].textContent, 'TPV');
    assert.equal(headerMeta.children[2].children[1].textContent, 2);
    assert.equal(body.children[1].children.map(child => child.textContent).join(','), 'Chef: Ready,Bebidas: Dispatched');
    assert.equal(body.children[2].children.length, 2);
    assert.equal(body.children[2].children[1].children[0].children[0].textContent, '1 × Agua');
    toggle.listeners.get('click')();
    assert.equal(body.hidden, true);
    assert.equal(toggle.attributes.get('aria-expanded'), 'false');
    assert.equal(renderer.collapsedOrders.has(order.id), true);
    renderer.render([order]);
    assert.equal(firstRenderedOrder(renderer).children[1].hidden, true);
    firstRenderedOrder(renderer).children[0].children[2].listeners.get('click')();
    assert.equal(renderer.collapsedOrders.has(order.id), false);
  } finally {
    globalThis.document = previousDocument;
  }
});

test('station card color follows its station status and chef highlights ready passes', () => {
  const previousDocument = globalThis.document;
  globalThis.document = { createElement: () => new Element() };
  try {
    const order = createOrder();
    order.status = 'InPreparation';
    order.stations = order.stations.map(station =>
      station.code === 'GRILL' ? { ...station, status: 'Ready' } : station
    );
    const { renderer } = createRenderer('GRILL');
    renderer.render([order]);
    assert.equal(firstRenderedOrder(renderer).dataset.status, 'Ready');

    const { renderer: chefRenderer } = createRenderer('CHEF');
    chefRenderer.render([order]);
    assert.equal(firstRenderedOrder(chefRenderer).dataset.status, 'Ready');
  } finally {
    globalThis.document = previousDocument;
  }
});

test('primary station separates controlled passes and disables dispatch while pending', () => {
  const previousDocument = globalThis.document;
  globalThis.document = { createElement: () => new Element() };
  try {
    const order = createOrder();
    order.guestCount = 0;
    order.readyAtUtc = null;
    order.stations = [
      ...order.stations,
      { code: 'PASTA', name: 'Pastas', status: 'Pending', priority: 3, requiresPrimaryDispatch: true },
    ];
    order.lines = [
      ...order.lines,
      { id: 'line-3', quantity: 1, productName: 'Pasta', categoryName: 'Carta', kitchenStatus: 'Pending', preparationStationCode: 'PASTA' },
    ];
    const { renderer } = createRenderer('CHEF', true);
    renderer.render([order]);
    const [title, body] = firstRenderedOrder(renderer).children;
    assert.equal(title.children[1].children.length, 2);
    assert.equal(body.children[0].children.length, 1);
    assert.equal(body.children[0].children[0].children[0].children[0].textContent, '1 × Agua · Bebidas');
    assert.equal(body.children[1].children[0].children[0].children[0].textContent, 'Prioridad 1');
    const dispatchButton = body.children[1].children[0].children[1].children[0].children[1].children[2];
    assert.equal(dispatchButton.textContent, 'Servir');
    assert.equal(dispatchButton.disabled, true);
  } finally {
    globalThis.document = previousDocument;
  }
});

test('primary station omits the passes area when no manual dispatch is required', () => {
  const previousDocument = globalThis.document;
  globalThis.document = { createElement: () => new Element() };
  try {
    const order = createOrder();
    order.stations = order.stations.map(station => ({
      ...station,
      requiresPrimaryDispatch: false,
    }));
    const { renderer } = createRenderer('CHEF');
    renderer.render([order]);

    const body = firstRenderedOrder(renderer).children[1];
    assert.equal(body.children.some(child => child.className === 'passes'), false);
    assert.equal(body.children.some(child =>
      child.textContent.includes('Este pedido no contiene pases')
    ), false);
  } finally {
    globalThis.document = previousDocument;
  }
});

test('render shows the empty state when no orders are available', () => {
  const { renderer } = createRenderer('GRILL');
  renderer.showEmpty = message => { renderer.emptyMessage = message; };
  renderer.element.append(new Element());
  renderer.render([]);
  assert.equal(renderer.element.children.length, 0);
  assert.equal(renderer.emptyMessage, 'No hay pedidos enviados a cocina.');
});

test('product notes and extras are rendered separately as text', () => {
  const previousDocument = globalThis.document;
  globalThis.document = { createElement: () => new Element() };
  try {
    const renderer = Object.create(OrderRenderer.prototype);
    const cases = [
      ['Sin sal', 'Sin sal', []],
      ['Sin sal · Extras: Queso, Bacon', 'Sin sal', ['Queso', 'Bacon']],
      ['  Sin sal · ·  Extras: Queso', 'Sin sal', ['Queso']],
      ['Extras: Queso', '', ['Queso']],
      ['Sin sal Extras:', 'Sin sal', []],
      ['<script>alert(1)</script> · Extras: <b>Queso</b>', '<script>alert(1)</script>', ['<b>Queso</b>']],
      ['', '', []],
    ];
    const order = { id: 'order-1', stations: [{ code: 'GRILL', name: 'Parrilla' }] };
    for (const [notes, expectedNotes, extras] of cases) {
      const item = renderer.renderProductLine(order, {
        notes, quantity: 1, productName: 'Hamburguesa', categoryName: 'Carnes', kitchenStatus: 'Dispatched',
        preparationStationCode: 'GRILL'
      }, true, false, false);
      const description = item.children[0];
      assert.equal(description.children[0].textContent,
        '1 × Hamburguesa · Parrilla' + (expectedNotes ? ' — ' + expectedNotes : ''));
      assert.deepEqual(description.children[1]?.children.map(extra => extra.textContent) ?? [], extras);
    }
  } finally {
    globalThis.document = previousDocument;
  }
});

test('chef dispatch waits for earlier passes and station actions follow line status', () => {
  const previousDocument = globalThis.document;
  globalThis.document = { createElement: () => new Element() };
  try {
    const advances = [];
    const renderer = Object.create(OrderRenderer.prototype);
    renderer.options = {
      isOrderPending: () => false,
      onAdvance: (_order, action) => advances.push(action.path),
    };
    const order = { id: 'order-1' };
    const line = { id: 'line-1', quantity: 1, productName: 'Plato', categoryName: 'Carta' };
    const controls = (status, isPrimary, blocked = false, stationActions = true) =>
      renderer.renderProductLine(order, { ...line, kitchenStatus: status }, isPrimary, blocked, stationActions).children[1];

    const readyChef = controls('Ready', true);
    assert.deepEqual(readyChef.children.map(child => child.textContent), ['🕓 —', '🕓 —', 'Servir']);
    assert.equal(readyChef.children[2].attributes.get('aria-label'), 'Despachar');
    readyChef.children[2].listeners.get('click')();

    const waitingChef = controls('Ready', true, true);
    assert.equal(waitingChef.children[2].textContent, '⌛');
    assert.equal(waitingChef.children[2].title, 'Esperando un pase anterior');
    assert.equal(waitingChef.children[2].attributes.get('aria-label'), 'Esperando un pase anterior');
    assert.equal(controls('Pending', true).children.length, 2);

    const pendingStation = controls('Pending', false);
    assert.equal(pendingStation.children[1].textContent, 'Iniciar');
    pendingStation.children[1].listeners.get('click')();
    const preparingStation = controls('InPreparation', false);
    assert.equal(preparingStation.children[2].textContent, 'Listo');
    preparingStation.children[2].listeners.get('click')();
    assert.equal(controls('Ready', false).children.length, 2);
    assert.equal(controls('Pending', false, false, false).children.length, 1);
    assert.deepEqual(advances, [
      'lines/line-1/dispatch',
      'lines/line-1/start-preparation',
      'lines/line-1/ready',
    ]);
  } finally {
    globalThis.document = previousDocument;
  }
});
