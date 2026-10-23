import assert from 'node:assert/strict';
import { test } from 'node:test';
import { OrderRenderer } from '../../../src/Clients/Restaurantes.Clients.Kds.Web/wwwroot/kds/js/order-renderer.js';

class Element {
  children = [];
  dataset = {};
  textContent = '';
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
    id: 'order-12345678', status: 'Ready', source: 'POS', tableLabel: 'Mesa 1',
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

function createRenderer(stationCode, pending = false, recover = false) {
  const renderer = Object.create(OrderRenderer.prototype);
  const recovered = [];
  renderer.element = new Element();
  renderer.collapsedOrders = new Set();
  renderer.options = {
    getStationCode: () => stationCode,
    getPrimaryStationCode: () => 'CHEF',
    canRecover: () => recover,
    isOrderPending: () => pending,
    onRecover: (order, button) => recovered.push({ order, button }),
    onAdvance: () => {},
  };
  return { renderer, recovered };
}

test('station order keeps header, badges, lines, recovery action and collapse state', () => {
  const previousDocument = globalThis.document;
  globalThis.document = { createElement: () => new Element() };
  try {
    const order = createOrder();
    const { renderer, recovered } = createRenderer('GRILL', false, true);
    renderer.render([order]);
    const article = renderer.element.children[0];
    const [title, body] = article.children;
    const [titleContent, headerMeta, toggle] = title.children;
    assert.equal(article.dataset.status, 'Ready');
    assert.deepEqual(titleContent.children.map(child => child.textContent), ['Mesa 1', 'order-12']);
    assert.equal(headerMeta.children[0].textContent, 'Origen: POS');
    assert.equal(headerMeta.children[2].children[1].textContent, 2);
    assert.equal(body.children[1].children.map(child => child.textContent).join(','), 'Chef: Ready,Bebidas: Dispatched');
    assert.equal(body.children[2].children.length, 2);
    assert.equal(body.children[2].children[1].children[0].children[0].textContent, '1 × Agua');
    const deliverButton = body.children[3].children[0];
    assert.equal(deliverButton.disabled, false);
    deliverButton.listeners.get('click')();
    assert.deepEqual(recovered, [{ order, button: deliverButton }]);

    toggle.listeners.get('click')();
    assert.equal(body.hidden, true);
    assert.equal(toggle.attributes.get('aria-expanded'), 'false');
    assert.equal(renderer.collapsedOrders.has(order.id), true);
    renderer.render([order]);
    assert.equal(renderer.element.children[0].children[1].hidden, true);
    renderer.element.children[0].children[0].children[2].listeners.get('click')();
    assert.equal(renderer.collapsedOrders.has(order.id), false);
  } finally {
    globalThis.document = previousDocument;
  }
});

test('primary station separates controlled passes and disables pending recovery', () => {
  const previousDocument = globalThis.document;
  globalThis.document = { createElement: () => new Element() };
  try {
    const order = createOrder();
    order.guestCount = 0;
    order.readyAtUtc = null;
    const { renderer } = createRenderer('CHEF', true, true);
    renderer.render([order]);
    const [title, body] = renderer.element.children[0].children;
    assert.equal(title.children[1].children.length, 2);
    assert.equal(body.children[0].children.length, 1);
    assert.equal(body.children[0].children[0].children[0].children[0].textContent, '1 × Agua · Bebidas');
    assert.equal(body.children[1].children[0].children[0].children[0].textContent, 'Parrilla · prioridad 1');
    assert.equal(body.children[2].children[0].disabled, true);
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
    assert.deepEqual(readyChef.children.map(child => child.textContent), ['🕓 —', '🕓 —', 'Despachar']);
    readyChef.children[2].listeners.get('click')();

    const waitingChef = controls('Ready', true, true);
    assert.equal(waitingChef.children[2].textContent, 'Esperando un pase anterior.');
    assert.equal(controls('Pending', true).children.length, 3);

    const pendingStation = controls('Pending', false);
    assert.equal(pendingStation.children[2].textContent, 'Iniciar');
    pendingStation.children[2].listeners.get('click')();
    const preparingStation = controls('InPreparation', false);
    assert.equal(preparingStation.children[2].textContent, 'Listo');
    preparingStation.children[2].listeners.get('click')();
    assert.equal(controls('Ready', false).children.length, 2);
    assert.equal(controls('Pending', false, false, false).children.length, 2);
    assert.deepEqual(advances, [
      'lines/line-1/dispatch',
      'lines/line-1/start-preparation',
      'lines/line-1/ready',
    ]);
  } finally {
    globalThis.document = previousDocument;
  }
});
