import assert from 'node:assert/strict';
import { test } from 'node:test';
import { OrderRenderer } from '../../../src/Clients/Restaurantes.Clients.Kds.Web/wwwroot/kds/js/order-renderer.js';

class Element {
  children = [];
  dataset = {};
  textContent = '';
  listeners = new Map();
  append(...children) { this.children.push(...children); }
  addEventListener(name, listener) { this.listeners.set(name, listener); }
}

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
    for (const [notes, expectedNotes, extras] of cases) {
      const item = renderer.renderProductLine({ id: 'order-1' }, {
        notes, quantity: 1, productName: 'Hamburguesa', categoryName: 'Carnes', kitchenStatus: 'Dispatched'
      }, false, false, false);
      const description = item.children[0];
      assert.equal(description.children[0].textContent,
        '1 × Hamburguesa · Carnes' + (expectedNotes ? ' — ' + expectedNotes : ''));
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
