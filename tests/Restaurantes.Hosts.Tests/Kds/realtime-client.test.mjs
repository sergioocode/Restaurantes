import assert from 'node:assert/strict';
import { test } from 'node:test';
import { KdsRealtimeClient } from '../../../src/Clients/Restaurantes.Clients.Kds.Web/wwwroot/kds/js/realtime-client.js';

const separator = String.fromCodePoint(30);

class FakeWebSocket {
  static instances = [];

  constructor(url) {
    this.url = url;
    this.listeners = new Map();
    this.sent = [];
    this.closeCalls = 0;
    FakeWebSocket.instances.push(this);
  }

  addEventListener(eventName, handler) {
    const handlers = this.listeners.get(eventName) ?? [];
    handlers.push(handler);
    this.listeners.set(eventName, handlers);
  }

  send(message) {
    this.sent.push(message);
  }

  close() {
    this.closeCalls++;
  }

  async dispatch(eventName, event = {}) {
    for (const handler of this.listeners.get(eventName) ?? []) {
      await handler(event);
    }
  }
}

function installBrowserFakes() {
  const originalWindow = globalThis.window;
  const originalWebSocket = globalThis.WebSocket;
  const timeouts = new Map();
  let nextTimeout = 1;
  globalThis.window = {
    clearTimeout(id) {
      timeouts.delete(id);
    },
    setTimeout(callback, delay) {
      const id = nextTimeout++;
      timeouts.set(id, { callback, delay });
      return id;
    },
  };
  globalThis.WebSocket = FakeWebSocket;
  FakeWebSocket.instances = [];

  return {
    sockets: FakeWebSocket.instances,
    timeouts,
    restore() {
      globalThis.window = originalWindow;
      globalThis.WebSocket = originalWebSocket;
    },
    runTimeout(id) {
      const timeout = timeouts.get(id);
      assert.ok(timeout, `Expected timeout ${id} to exist.`);
      timeouts.delete(id);
      timeout.callback();
      return timeout.delay;
    },
  };
}

function handlers() {
  const statuses = [];
  const updates = [];
  let connected = 0;
  return {
    value: {
      onStatus: (...args) => statuses.push(args),
      onConnected: async () => connected++,
      onOrderUpdated: async order => updates.push(order),
    },
    statuses,
    updates,
    connected: () => connected,
  };
}

const connection = {
  accessToken: 'token+/= value',
  restaurantId: 'restaurant-1',
  stationCode: 'DRINKS',
  stationName: 'Bebidas',
};

test('connect negotiates SignalR, joins the selected station, and forwards order updates', async () => {
  const browser = installBrowserFakes();
  const callbacks = handlers();
  try {
    const client = new KdsRealtimeClient('http://kds.local:5010/', callbacks.value);
    client.connect(connection);
    const [socket] = browser.sockets;

    assert.equal(
      socket.url,
      'ws://kds.local:5010/hubs/kds?access_token=token%2B%2F%3D%20value',
    );
    assert.deepEqual(callbacks.statuses, [[false, 'Conectando…']]);

    await socket.dispatch('open');
    assert.equal(socket.sent[0], JSON.stringify({ protocol: 'json', version: 1 }) + separator);
    await socket.dispatch('message', { data: '{}' + separator });
    assert.equal(
      socket.sent[1],
      JSON.stringify({
        type: 1,
        invocationId: 'join',
        target: 'JoinStation',
        arguments: ['restaurant-1', 'DRINKS'],
      }) + separator,
    );

    const order = { id: 'order-1', version: 3 };
    await socket.dispatch('message', {
      data:
        JSON.stringify({ type: 3, invocationId: 'join' }) +
        separator +
        JSON.stringify({ type: 1, target: 'OrderUpdated', arguments: [order] }) +
        separator,
    });

    assert.deepEqual(callbacks.statuses.at(-1), [true, 'Conectado a Bebidas']);
    assert.equal(callbacks.connected(), 1);
    assert.deepEqual(callbacks.updates, [order]);
  } finally {
    browser.restore();
  }
});

test('secure KDS endpoints use the secure WebSocket protocol', () => {
  const browser = installBrowserFakes();
  try {
    const client = new KdsRealtimeClient('https://kds.example.test/path', handlers().value);
    client.connect(connection);

    assert.equal(
      browser.sockets[0].url,
      'wss://kds.example.test/hubs/kds?access_token=token%2B%2F%3D%20value',
    );
  } finally {
    browser.restore();
  }
});

test('replaced connections ignore old socket events and clear a pending reconnect timer', async () => {
  const browser = installBrowserFakes();
  const callbacks = handlers();
  try {
    const client = new KdsRealtimeClient('http://kds.local', callbacks.value);
    client.connect(connection);
    const oldSocket = browser.sockets[0];
    await oldSocket.dispatch('close');
    const reconnectId = Number([...browser.timeouts.keys()][0]);

    const replacement = { ...connection, stationCode: 'CHEF', stationName: 'Chef' };
    client.connect(replacement);

    assert.equal(oldSocket.closeCalls, 1);
    assert.equal(browser.timeouts.size, 0);
    await oldSocket.dispatch('error');
    await oldSocket.dispatch('message', { data: '{}' + separator });
    assert.deepEqual(callbacks.statuses, [
      [false, 'Conectando…'],
      [false, 'Desconectado; reintentando…'],
      [false, 'Conectando…'],
    ]);
    assert.equal(browser.sockets.length, 2);
    assert.equal(browser.timeouts.has(reconnectId), false);
  } finally {
    browser.restore();
  }
});

test('socket errors report connection failure and a current close retries after two seconds', async () => {
  const browser = installBrowserFakes();
  const callbacks = handlers();
  try {
    const client = new KdsRealtimeClient('http://kds.local', callbacks.value);
    client.connect(connection);
    const socket = browser.sockets[0];
    await socket.dispatch('error');
    await socket.dispatch('close');

    assert.deepEqual(callbacks.statuses.slice(1), [
      [false, 'Error de conexión'],
      [false, 'Desconectado; reintentando…'],
    ]);
    const [reconnectId] = browser.timeouts.keys();
    assert.equal(browser.runTimeout(reconnectId), 2000);
    assert.equal(browser.sockets.length, 2);
    assert.deepEqual(callbacks.statuses.at(-1), [false, 'Conectando…']);
  } finally {
    browser.restore();
  }
});

test('disconnect clears reconnect state and makes subsequent old socket events inert', async () => {
  const browser = installBrowserFakes();
  const callbacks = handlers();
  try {
    const client = new KdsRealtimeClient('http://kds.local', callbacks.value);
    client.connect(connection);
    const socket = browser.sockets[0];
    await socket.dispatch('close');
    const [reconnectId] = browser.timeouts.keys();

    client.disconnect();
    await socket.dispatch('close');
    await socket.dispatch('error');
    await socket.dispatch('message', { data: '{}' + separator });

    assert.equal(socket.closeCalls, 1);
    assert.equal(browser.timeouts.size, 0);
    assert.equal(callbacks.statuses.length, 2);
    assert.equal(callbacks.connected(), 0);
    assert.equal(callbacks.updates.length, 0);
    assert.equal(browser.timeouts.has(reconnectId), false);
  } finally {
    browser.restore();
  }
});
