import assert from 'node:assert/strict';
import { test } from 'node:test';

const appUrl = new URL('../../../src/Clients/Restaurantes.Clients.Kds.Web/wwwroot/kds/js/app.js', import.meta.url);

class Element {
  constructor(tagName = 'div') {
    this.tagName = tagName.toUpperCase();
    this.children = [];
    this.listeners = new Map();
    this.attributes = new Map();
    this.dataset = {};
    this.value = '';
    this.textContent = '';
    this.disabled = false;
    this.hidden = false;
    this.open = false;
    this.classes = new Set();
    this.classList = {
      add: name => this.classes.add(name),
      remove: name => this.classes.delete(name),
      contains: name => this.classes.has(name),
      toggle: (name, active) => {
        if (active === undefined) active = !this.classes.has(name);
        if (active) this.classes.add(name);
        else this.classes.delete(name);
        return active;
      },
    };
  }

  set className(value) { this.classes = new Set(value.split(/\s+/u).filter(Boolean)); }
  get className() { return [...this.classes].join(' '); }
  get options() { return this.children; }
  get selectedIndex() { return this.options.findIndex(option => option.value === this.value); }
  set innerHTML(value) {
    this.replaceChildren();
    if (value.includes('class="empty"')) {
      const empty = new Element('p');
      empty.className = 'empty';
      this.append(empty);
    }
  }
  append(...children) {
    for (const child of children) {
      child.parent = this;
      this.children.push(child);
    }
  }
  replaceChildren(...children) { this.children = []; this.append(...children); }
  addEventListener(name, listener) { this.listeners.set(name, listener); }
  setAttribute(name, value) { this.attributes.set(name, value); }
  querySelector(selector) {
    if (selector === 'span:last-child') return this.children.at(-1);
    return this.querySelectorAll(selector)[0] || null;
  }
  querySelectorAll(selector) {
    const matches = element => selector === 'button'
      ? element.tagName === 'BUTTON'
      : selector === '.empty' ? element.classes.has('empty') : false;
    const found = [];
    const visit = element => {
      for (const child of element.children) {
        if (matches(child)) found.push(child);
        visit(child);
      }
    };
    visit(this);
    return found;
  }
  closest(selector) {
    for (let element = this; element; element = element.parent) {
      if (selector === 'article.order' && element.tagName === 'ARTICLE' && element.classes.has('order')) return element;
    }
    return null;
  }
  showModal() { this.open = true; }
  close() { this.open = false; }
}

async function until(predicate) {
  for (let attempt = 0; attempt < 50; attempt++) {
    if (predicate()) return;
    await new Promise(resolve => setImmediate(resolve));
  }
  assert.fail('KDS did not reach the expected state');
}

test('KDS restores a session, advances an order, shows its ticket and changes stations', async () => {
  const original = new Map();
  const replaceGlobal = (name, value) => {
    original.set(name, Object.getOwnPropertyDescriptor(globalThis, name));
    Object.defineProperty(globalThis, name, { configurable: true, writable: true, value });
  };
  const elements = new Map();
  const element = id => {
    if (!elements.has(id)) elements.set(id, new Element());
    return elements.get(id);
  };
  const connectionStatus = element('connectionStatus');
  connectionStatus.append(new Element('span'), new Element('span'));
  const assigned = [];
  const saved = new Map([['restaurantes.kds.station.restaurant-1', 'GRILL']]);
  const calls = [];
  const sockets = [];
  const line = {
    id: 'line-1', quantity: 1, productName: 'Tortilla', categoryName: 'Tapas',
    kitchenStatus: 'Pending', preparationStationCode: 'GRILL', format: 'TAPA',
    notes: '',
  };
  let order = {
    id: 'order-12345678', version: 1, status: 'InPreparation', source: 'POS',
    tableLabel: 'Mesa 1 - Sala', customerName: '', serviceMode: 'DineIn',
    submittedAtUtc: '2026-10-19T08:00:00Z',
    stations: [
      { code: 'MAIN', name: 'Principal', isPrimary: true, isActive: true, priority: 0, status: 'Pending' },
      { code: 'GRILL', name: 'Parrilla', isPrimary: false, isActive: true, priority: 1, status: 'Pending', printTicket: true },
    ],
    lines: [line],
  };
  const stations = order.stations;
  const json = value => Response.json(value);
  replaceGlobal('document', {
    hidden: false,
    querySelector: selector => element(selector.slice(1)),
    createElement: tag => new Element(tag),
    addEventListener() {},
  });
  replaceGlobal('window', {
    location: {
      origin: 'https://kds.test', hash: '', pathname: '/kds/',
      assign: url => assigned.push(url),
    },
    setInterval: () => 1,
    setTimeout: () => 1,
    clearTimeout() {},
    addEventListener() {},
  });
  replaceGlobal('navigator', {});
  replaceGlobal('sessionStorage', {
    getItem: () => JSON.stringify({
      accessToken: 'test-token', expiresAtUtc: '2099-01-01T00:00:00Z',
      restaurants: [{ restaurantId: 'restaurant-1', role: 'Kds', permissions: ['kds.use'] }],
    }),
    setItem() {}, removeItem() {},
  });
  replaceGlobal('localStorage', {
    getItem: key => saved.get(key) || null,
    setItem: (key, value) => saved.set(key, value),
  });
  replaceGlobal('history', { replaceState() {} });
  replaceGlobal('WebSocket', class {
    constructor(url) { this.url = url; this.listeners = new Map(); sockets.push(this); }
    addEventListener(name, listener) { this.listeners.set(name, listener); }
    close() {}
    send() {}
  });
  replaceGlobal('fetch', async (url, options = {}) => {
    calls.push({ url: String(url), method: options.method || 'GET' });
    const path = new URL(url).pathname;
    if (path.endsWith('/provider')) return json({ activeProvider: 'Microsoft', microsoftConfigured: true });
    if (path.endsWith('/accessible/kds')) return json([
      { id: 'restaurant-1', name: 'Centro', code: 'CENTRO' },
      { id: 'restaurant-2', name: 'Norte', code: 'NORTE' },
    ]);
    if (path.endsWith('/stations')) return json(path.includes('restaurant-2') ? [] : stations);
    if (path.endsWith('/kds-history')) return json([{ orderId: order.id, status: 'Ready',
      tableLabel: order.tableLabel, createdAtUtc: order.submittedAtUtc,
      lines: [{ quantity: 1, productName: 'Tortilla', format: 'TAPA', status: 'Active' }] }]);
    if (path.endsWith('/kds')) return json([order]);
    if (path.includes('/lines/line-1/')) {
      const ready = path.endsWith('/ready');
      order = { ...order, version: order.version + 1,
        lines: [{ ...line, kitchenStatus: ready ? 'Ready' : 'InPreparation',
          preparationStartedAtUtc: '2026-10-19T08:01:00Z',
          readyAtUtc: ready ? '2026-10-19T08:02:00Z' : null }] };
      return json(order);
    }
    throw new Error('Unexpected KDS request: ' + url);
  });

  try {
    await import(appUrl.href);
    await until(() => element('stationCode').options.length === 2);
    assert.equal(element('restaurantId').value, 'restaurant-1');
    assert.equal(element('stationCode').value, 'GRILL');
    assert.equal(sockets.length, 1);
    assert.equal(element('monitorPanel').classList.contains('hidden'), false);

    await element('refresh').listeners.get('click')();
    const orderCard = element('orders').children[0];
    assert.equal(orderCard.classes.has('order'), true);
    let action = orderCard.querySelectorAll('button').find(button => button.textContent === 'Iniciar');
    assert.ok(action);
    await action.listeners.get('click')();
    assert.equal(order.version, 2);
    assert.equal(element('event').textContent, 'Comando aceptado; actualizando el pedido…');

    action = element('orders').children[0].querySelectorAll('button').find(button => button.textContent === 'Listo');
    assert.ok(action);
    await action.listeners.get('click')();
    assert.equal(order.version, 3);
    assert.equal(element('ticketDialog').open, true);
    assert.match(element('ticketContent').children[0].textContent, /Parrilla/u);
    element('closeTicket').listeners.get('click')();
    assert.equal(element('ticketDialog').open, false);

    await element('historyButton').listeners.get('click')();
    await until(() => element('historyList').children.length === 1);
    assert.match(element('historyList').children[0].children[0].textContent, /Mesa 1/u);
    assert.equal(element('historyPanel').classList.contains('hidden'), false);
    element('historyButton').listeners.get('click')();
    assert.equal(element('historyPanel').classList.contains('hidden'), true);

    element('restaurantId').value = 'restaurant-2';
    element('restaurantId').listeners.get('change')();
    await until(() => element('connectionStatus').children[1].textContent === 'Sin estaciones KDS activas');
    assert.match(element('orders').children[0].textContent, /no tiene estaciones/u);
    assert.ok(calls.some(call => call.url.includes('/kds-history')));

    element('logout').listeners.get('click')();
    assert.equal(element('loginPanel').classList.contains('hidden'), false);
    element('loginMicrosoft').listeners.get('click')();
    assert.match(assigned[0], /auth\/start\/Microsoft/u);
  } finally {
    for (const [name, descriptor] of original) {
      if (descriptor) Object.defineProperty(globalThis, name, descriptor);
      else delete globalThis[name];
    }
  }
});
