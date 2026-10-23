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
  const saved = new Map([
    ['restaurantes.kds.station.restaurant-1', 'GRILL'],
    ['restaurantes.kds.zone.restaurant-1.GRILL', 'zone-2'],
  ]);
  const calls = [];
  const sockets = [];
  let commandStatus = null;
  let historyMode = 'order';
  let stationsFailure = false;
  let ordersFailure = false;
  let confirmDelivery = false;
  const line = {
    id: 'line-1', quantity: 1, productName: 'Tortilla', categoryName: 'Tapas',
    kitchenStatus: 'Pending', preparationStationCode: 'GRILL', format: 'TAPA',
    notes: '',
  };
  let order = {
    id: 'order-12345678', version: 1, status: 'InPreparation', source: 'POS',
    tableLabel: 'Mesa 1 - Sala', zoneId: 'zone-2', customerName: '', serviceMode: 'DineIn',
    submittedAtUtc: '2026-10-19T08:00:00Z',
    stations: [
      { code: 'MAIN', name: 'Principal', isPrimary: true, isActive: true, priority: 0, status: 'Pending' },
      { code: 'GRILL', name: 'Parrilla', isPrimary: false, isActive: true, priority: 1, status: 'Pending', printTicket: true, managesZones: true },
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
    setTimeout: (callback, delay) => {
      if (delay === 150) callback();
      return 1;
    },
    clearTimeout() {},
    addEventListener() {},
  });
  replaceGlobal('navigator', {});
  replaceGlobal('sessionStorage', {
    getItem: () => JSON.stringify({
      accessToken: 'test-token', expiresAtUtc: '2099-01-01T00:00:00Z',
      user: { displayName: 'Usuario KDS', username: 'kds@example.com' },
      restaurants: [{ restaurantId: 'restaurant-1', role: 'Kds', permissions: ['kds.use', 'orders.recover'] }],
    }),
    setItem() {}, removeItem() {},
  });
  replaceGlobal('localStorage', {
    getItem: key => saved.get(key) || null,
    setItem: (key, value) => saved.set(key, value),
  });
  replaceGlobal('history', { replaceState() {} });
  replaceGlobal('confirm', () => confirmDelivery);
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
    ]);
    if (path.endsWith('/zones')) return json([
      { id: 'zone-2', name: 'Terraza', sortOrder: 2, deletedAtUtc: null },
      { id: 'zone-1', name: 'Barra', sortOrder: 1, deletedAtUtc: null },
      { id: 'zone-deleted', name: 'Cerrada', sortOrder: 0, deletedAtUtc: '2026-10-19T08:00:00Z' },
    ]);
    if (path.endsWith('/stations')) {
      if (stationsFailure) throw new Error('stations offline');
      return json(stations);
    }
    if (path.endsWith('/kds-history')) {
      if (historyMode === 'failure') throw new Error('history offline');
      if (historyMode === 'empty') return json([]);
      return json([{ orderId: order.id, status: 'Ready',
        tableLabel: order.tableLabel, createdAtUtc: order.submittedAtUtc,
        lines: [{ quantity: 1, productName: 'Tortilla', format: 'TAPA', status: 'Active' }] }]);
    }
    if (path.endsWith('/kds')) {
      if (ordersFailure) throw new Error('orders offline');
      return json([order]);
    }
    if (path.endsWith('/deliver')) {
      order = { ...order, version: order.version + 1, status: 'Delivered' };
      return json(order);
    }
    if (path.includes('/lines/line-1/')) {
      if (commandStatus !== null) return new Response('command rejected', { status: commandStatus });
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
    assert.equal(element('currentUserName').textContent, 'Usuario KDS');
    assert.equal(element('stationCode').value, 'GRILL');
    assert.equal(element('zoneId').disabled, false);
    assert.deepEqual(element('zoneId').options.map(option => option.value), ['zone-1', 'zone-2']);
    assert.equal(element('zoneId').value, 'zone-2');
    assert.equal(sockets.length, 1);
    assert.equal(element('monitorPanel').classList.contains('hidden'), false);

    const socket = sockets[0];
    const separator = String.fromCodePoint(30);
    await socket.listeners.get('open')();
    await socket.listeners.get('message')({ data: '{}' + separator });
    const orderRequestCount = calls.filter(call =>
      call.url.includes('/kds?restaurantId=restaurant-1')
    ).length;
    await socket.listeners.get('message')({
      data: JSON.stringify({
        type: 1,
        target: 'OrderUpdated',
        arguments: [{ orderId: order.id, status: order.status, version: order.version }],
      }) + separator,
    });
    await until(() => calls.filter(call =>
      call.url.includes('/kds?restaurantId=restaurant-1')
    ).length > orderRequestCount);
    assert.equal(element('event').textContent, '');

    element('stationCode').value = 'CHEF';
    await element('stationCode').listeners.get('change')();
    assert.equal(element('zoneId').disabled, true);
    await element('refresh').listeners.get('click')();
    assert.ok(calls.some(call => call.url.endsWith('/kds?restaurantId=restaurant-1&stationCode=CHEF')));
    element('stationCode').value = 'GRILL';
    await element('stationCode').listeners.get('change')();
    await element('refresh').listeners.get('click')();

    element('zoneId').value = 'zone-1';
    await element('zoneId').listeners.get('change')();
    assert.equal(saved.get('restaurantes.kds.zone.restaurant-1.GRILL'), 'zone-1');
    assert.ok(calls.some(call => call.url.includes('zoneId=zone-1')));

    element('zoneId').value = '';
    await element('zoneId').listeners.get('change')();
    assert.match(element('orders').children[0].textContent, /No hay zonas activas/u);
    element('zoneId').value = 'zone-2';
    await element('zoneId').listeners.get('change')();

    await element('refresh').listeners.get('click')();
    let orderCard = element('orders').children[0];
    assert.equal(orderCard.classes.has('order'), true);
    let action = orderCard.querySelectorAll('button').find(button => button.textContent === 'Iniciar');
    assert.ok(action);
    commandStatus = 500;
    ordersFailure = true;
    await action.listeners.get('click')();
    assert.equal(
      element('event').textContent,
      'El comando fue rechazado: HTTP 500 No se pudo actualizar el monitor: orders offline',
    );
    ordersFailure = false;
    commandStatus = null;
    orderCard = element('orders').children[0];
    action = orderCard.querySelectorAll('button').find(button => button.textContent === 'Iniciar');
    assert.ok(action);
    await action.listeners.get('click')();
    assert.equal(order.version, 2);
    assert.equal(element('event').textContent, '');

    action = element('orders').children[0].querySelectorAll('button').find(button => button.textContent === 'Listo');
    assert.ok(action);
    order = { ...order, version: order.version + 1 };
    commandStatus = 409;
    await action.listeners.get('click')();
    assert.equal(element('event').textContent, '');
    commandStatus = null;
    action = element('orders').children[0].querySelectorAll('button').find(button => button.textContent === 'Listo');
    assert.ok(action);
    await action.listeners.get('click')();
    assert.equal(order.version, 4);
    assert.equal(element('ticketDialog').open, true);
    assert.match(element('ticketContent').children[0].textContent, /Parrilla/u);
    assert.equal(element('ticketContent').children[1].textContent, 'Destino: Mesa 1 · Zona: Terraza');
    element('closeTicket').listeners.get('click')();
    assert.equal(element('ticketDialog').open, false);

    await element('historyButton').listeners.get('click')();
    await until(() => element('historyList').children.length === 1);
    assert.match(element('historyList').children[0].children[0].textContent, /Mesa 1/u);
    assert.equal(element('historyPanel').classList.contains('hidden'), false);
    element('historyButton').listeners.get('click')();
    assert.equal(element('historyPanel').classList.contains('hidden'), true);
    historyMode = 'empty';
    element('historyButton').listeners.get('click')();
    await until(() => element('historyList').children[0]?.classes.has('empty'));
    assert.match(element('historyList').children[0].textContent, /No hay pedidos procesados/u);
    element('historyButton').listeners.get('click')();
    historyMode = 'failure';
    element('historyButton').listeners.get('click')();
    await until(() => element('event').textContent === 'history offline');
    assert.equal(element('historyButton').disabled, false);

    stationsFailure = true;
    await element('refresh').listeners.get('click')();
    assert.equal(element('event').textContent, 'stations offline');
    assert.equal(element('refresh').disabled, false);

    stationsFailure = false;
    ordersFailure = true;
    await element('refresh').listeners.get('click')();
    assert.equal(element('event').textContent, 'orders offline');
    assert.equal(element('refresh').disabled, false);
    ordersFailure = false;
    await element('refresh').listeners.get('click')();
    order = {
      ...order,
      status: 'Ready',
      serviceMode: 'Takeaway',
      customerName: 'Elena',
      lines: [{ ...line, kitchenStatus: 'Ready' }],
    };
    await element('refresh').listeners.get('click')();
    let delivery = element('orders').children[0].querySelectorAll('button')
      .find(button => button.textContent === 'Entregado · resolver incidencia');
    assert.ok(delivery);
    const deliveryCallsBeforeCancel = calls.filter(call => call.url.endsWith('/deliver')).length;
    await delivery.listeners.get('click')();
    assert.equal(calls.filter(call => call.url.endsWith('/deliver')).length, deliveryCallsBeforeCancel);
    confirmDelivery = true;
    await delivery.listeners.get('click')();
    assert.equal(calls.filter(call => call.url.endsWith('/deliver')).length, deliveryCallsBeforeCancel + 1);
    assert.equal(order.status, 'Delivered');

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

test('KDS login error keeps the login screen available and registers its service worker', async () => {
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
  const authLoading = element('authLoading');
  const loginPanel = element('loginPanel');
  const monitorPanel = element('monitorPanel');
  const eventText = element('event');
  const historyCalls = [];
  const serviceWorkerCalls = [];
  const windowListeners = new Map();
  let reloadCount = 0;
  replaceGlobal('document', {
    querySelector: selector => element(selector.slice(1)),
    createElement: tag => new Element(tag),
    addEventListener() {},
  });
  replaceGlobal('window', {
    setInterval: () => 1,
    setTimeout: () => 1,
    clearTimeout() {},
    addEventListener: (name, listener) => windowListeners.set(name, listener),
    location: {
      origin: 'https://kds.test',
      hash: '#login_error=access_denied',
      pathname: '/kds/',
      assign() {},
      reload: () => reloadCount++,
    },
  });
  replaceGlobal('navigator', {
    serviceWorker: {
      addEventListener: (name, listener) => serviceWorkerCalls.push({ name, listener }),
      register: (...args) => serviceWorkerCalls.push({ name: 'register', args }),
    },
  });
  replaceGlobal('sessionStorage', { getItem: () => null, setItem() {}, removeItem() {} });
  replaceGlobal('localStorage', { getItem: () => null, setItem() {} });
  replaceGlobal('history', { replaceState: (...args) => historyCalls.push(args) });
  replaceGlobal('fetch', async url => {
    assert.match(String(url), /\/api\/identity\/auth\/provider$/u);
    return Response.json({
      activeProvider: 'Microsoft',
      microsoftConfigured: true,
      googleConfigured: false,
    });
  });

  try {
    await import(appUrl.href + '?login-error-service-worker');
    await until(() => eventText.textContent.includes('proveedor rechazó el acceso'));

    assert.equal(authLoading.classList.contains('hidden'), true);
    assert.equal(loginPanel.classList.contains('hidden'), false);
    assert.equal(monitorPanel.classList.contains('hidden'), false);
    assert.equal(element('loginMicrosoft').disabled, false);
    assert.equal(element('loginGoogle').disabled, true);
    assert.equal(historyCalls.length, 1);
    assert.deepEqual(historyCalls[0], [null, '', '/kds/']);

    const controllerChange = serviceWorkerCalls.find(call => call.name === 'controllerchange');
    assert.ok(controllerChange);
    controllerChange.listener();
    assert.equal(reloadCount, 1);
    windowListeners.get('load')();
    assert.deepEqual(serviceWorkerCalls.find(call => call.name === 'register').args, [
      './service-worker.js',
      { updateViaCache: 'none' },
    ]);
  } finally {
    for (const [name, descriptor] of original) {
      if (descriptor) Object.defineProperty(globalThis, name, descriptor);
      else delete globalThis[name];
    }
  }
});
