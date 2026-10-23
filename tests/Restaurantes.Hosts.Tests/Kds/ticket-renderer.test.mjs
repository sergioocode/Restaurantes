import assert from 'node:assert/strict';
import { test } from 'node:test';
import { renderKitchenTicket } from '../../../src/Clients/Restaurantes.Clients.Kds.Web/wwwroot/kds/js/ticket-renderer.js';

class Element {
  children = [];
  textContent = '';
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
}

function withDocument(action) {
  const previousDocument = globalThis.document;
  globalThis.document = { createElement: () => new Element() };
  try {
    return action();
  } finally {
    globalThis.document = previousDocument;
  }
}

test('kitchen ticket renders destination, product details, notes and station timing', () => {
  withDocument(() => {
    const ticket = new Element();
    ticket.append(new Element());
    renderKitchenTicket(
      ticket,
      {
        tableLabel: 'Mesa 4A - Terraza Norte',
        submittedAtUtc: '2026-10-19T10:00:00Z',
        stations: [{ code: 'GRILL', name: 'Parrilla' }],
      },
      {
        quantity: 2,
        productName: 'Hamburguesa',
        format: 'MEDIA_RACION',
        notes: '  Sin cebolla  ',
        preparationStationCode: 'GRILL',
        preparationStartedAtUtc: '2026-10-19T10:00:00Z',
        readyAtUtc: '2026-10-19T10:02:05Z',
        dispatchedAtUtc: '2026-10-19T10:03:00Z',
      },
      { name: 'Chef', isPrimary: true, code: 'CHEF' },
      true,
      'Terraza Norte',
    );

    assert.equal(ticket.children[0].id, 'ticketTitle');
    assert.equal(ticket.children[0].textContent, 'Chef');
    assert.equal(ticket.children[1].textContent, 'Destino: Mesa 4A · Zona: Terraza Norte');
    assert.equal(ticket.children[3].textContent, '2 × Hamburguesa · media racion');
    assert.equal(ticket.children[4].textContent, 'Sin cebolla');
    assert.match(ticket.children[5].textContent, /^Llega: \d{2}:\d{2}:\d{2}$/);
    assert.match(ticket.children[6].textContent, /^Pase: \d{2}:\d{2}:\d{2}$/);
    assert.equal(ticket.children[7].textContent, 'Tiempo en Parrilla: 2:05');
  });
});

test('kitchen ticket preserves hyphenated customer names and uses destination fallback', () => {
  withDocument(() => {
    const ticket = new Element();
    const order = {
      tableLabel: '',
      customerName: 'Ana-María',
      submittedAtUtc: null,
      stations: [],
    };
    const line = { quantity: 1, productName: 'Café', readyAtUtc: null };
    const station = { name: 'Bebidas', isPrimary: false };

    renderKitchenTicket(ticket, order, line, station, false);
    assert.equal(ticket.children[1].textContent, 'Destino: Ana-María');
    assert.equal(ticket.children[4].textContent, 'Llega: --:--');
    assert.equal(ticket.children[5].textContent, 'Pase: --:--');

    renderKitchenTicket(ticket, { ...order, customerName: '' }, line, station, false);
    assert.equal(ticket.children[1].textContent, 'Destino: Pedido');
    assert.equal(ticket.children.length, 6);
  });
});
