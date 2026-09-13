import { formatKitchenDuration, kitchenClock } from './kitchen-clock.js';

export function orderDestinationLabel(order) {
  return order.tableLabel?.trim()
    || order.customerName?.trim()
    || 'Destino sin identificar';
}

export class OrderRenderer {
  constructor(element, options) {
    this.element = element;
    this.options = options;
    window.setInterval(() => this.refreshKitchenClocks(), 1000);
    document.addEventListener('visibilitychange', () => this.refreshKitchenClocks());
  }

  render(orders) {
    this.element.replaceChildren();
    if (!orders.length) {
      this.showEmpty('No hay pedidos enviados a cocina.');
      return;
    }

    for (const order of orders) {
      const article = document.createElement('article');
      article.className = 'order';
      article.dataset.status = order.status;

      const title = document.createElement('h2');
      const destination = document.createElement('span');
      destination.textContent = orderDestinationLabel(order);
      const badge = document.createElement('span');
      badge.className = 'badge';
      badge.textContent = order.status + ' · v' + order.version;
      title.append(destination, badge);

      const id = document.createElement('small');
      id.textContent = order.id;
      const age = document.createElement('small');
      age.textContent = order.status === 'Ready' && order.readyAtUtc
        ? ' · ' + elapsedLabel(order.readyAtUtc)
        : '';
      id.append(age);

      const channel = document.createElement('div');
      channel.className = 'stations';
      const sourceBadge = document.createElement('span');
      sourceBadge.className = 'station';
      sourceBadge.textContent = 'Origen: ' + order.source;
      channel.append(sourceBadge, this.renderKitchenClock(order, this.options.getStationCode()));
      if (Number.isInteger(order.guestCount) && order.guestCount > 0) {
        const guests = document.createElement('span');
        guests.className = 'station';
        guests.textContent = '👥 ' + order.guestCount;
        guests.title = order.guestCount + ' comensales';
        guests.setAttribute('aria-label', guests.title);
        channel.append(guests);
      }

      const stations = document.createElement('div');
      stations.className = 'stations';
      for (const station of order.stations) {
        const stationBadge = document.createElement('span');
        stationBadge.className = 'station';
        stationBadge.textContent = station.name + ': ' + station.status;
        stations.append(stationBadge);
      }

      const lines = document.createElement('ul');
      for (const line of order.lines) {
        const item = document.createElement('li');
        item.textContent = line.quantity + ' × ' + line.productName + ' [' + line.categoryName + ']'
          + (line.notes ? ' — ' + line.notes : '');
        lines.append(item);
      }

      article.append(title, id, channel, stations, lines);
      if (this.options.getStationCode() === this.options.getPrimaryStationCode()) {
        article.append(this.renderChefPasses(order));
      }

      const action = this.nextAction(order);
      const canRecover = this.options.canRecover(order);
      if (action || canRecover) {
        const actions = document.createElement('div');
        actions.className = 'actions';
        if (action) {
          const actionButton = document.createElement('button');
          actionButton.textContent = action.label;
          actionButton.addEventListener('click', () => this.options.onAdvance(order, action, actionButton));
          actions.append(actionButton);
        }
        if (canRecover) {
          const deliverButton = document.createElement('button');
          deliverButton.textContent = 'Entregado · resolver incidencia';
          deliverButton.addEventListener('click', () => this.options.onRecover(order, deliverButton));
          actions.append(deliverButton);
        }
        article.append(actions);
      }
      this.element.append(article);
    }
  }

  showEmpty(message) {
    this.element.innerHTML = '<p class="empty"></p>';
    this.element.querySelector('.empty').textContent = message;
  }

  nextAction(order) {
    const stationCode = this.options.getStationCode();
    if (stationCode === this.options.getPrimaryStationCode()) return null;
    const ticket = order.stations.find(station => station.code === stationCode);
    if (!ticket) return null;
    if (ticket.status === 'Pending') {
      return {
        path: 'stations/' + encodeURIComponent(stationCode) + '/start-preparation',
        label: 'Iniciar ' + ticket.name
      };
    }
    if (ticket.status === 'InPreparation') {
      return {
        path: 'stations/' + encodeURIComponent(stationCode) + '/ready',
        label: 'Marcar ' + ticket.name + ' listo'
      };
    }
    return null;
  }

  renderChefPasses(order) {
    const container = document.createElement('div');
    container.className = 'passes';
    const controlled = order.stations
      .filter(station => station.requiresPrimaryDispatch)
      .sort((a, b) => a.priority - b.priority || a.name.localeCompare(b.name));
    if (!controlled.length) {
      const note = document.createElement('small');
      note.textContent = 'Este pedido no contiene pases que requieran despacho del KDS principal.';
      container.append(note);
      return container;
    }

    for (const station of controlled) {
      const pass = document.createElement('section');
      pass.className = 'pass';
      const header = document.createElement('header');
      const name = document.createElement('strong');
      name.textContent = station.name + ' · prioridad ' + station.priority;
      const status = document.createElement('span');
      status.className = 'station';
      status.textContent = station.status;
      header.append(name, status, this.renderKitchenClock(order, station.code));

      const lines = document.createElement('ul');
      for (const line of order.lines.filter(line => line.preparationStationCode === station.code)) {
        const item = document.createElement('li');
        item.textContent = line.quantity + ' × ' + line.productName;
        lines.append(item);
      }
      pass.append(header, lines);

      if (station.status === 'Ready') {
        const blocked = controlled.some(other =>
          other.priority < station.priority
          && other.status !== 'Dispatched'
          && other.status !== 'Cancelled'
        );
        if (blocked) {
          const note = document.createElement('small');
          note.textContent = 'Esperando el despacho de un pase anterior.';
          pass.append(note);
        } else {
          const actions = document.createElement('div');
          actions.className = 'actions';
          const button = document.createElement('button');
          button.textContent = 'Despachar ' + station.name;
          button.addEventListener('click', () => this.options.onAdvance(order, {
            path: 'stations/' + encodeURIComponent(station.code) + '/dispatch',
            label: button.textContent
          }, button));
          actions.append(button);
          pass.append(actions);
        }
      }
      container.append(pass);
    }
    return container;
  }

  renderKitchenClock(order, stationCode) {
    const badge = document.createElement('span');
    badge.className = 'station kitchen-clock';
    const clock = kitchenClock(
      order,
      stationCode === this.options.getPrimaryStationCode() ? 'CHEF' : stationCode
    );
    if (!clock) {
      badge.textContent = '⏱ —';
      badge.title = 'Tiempo de cocina no disponible';
      badge.setAttribute('aria-label', badge.title);
      return badge;
    }
    badge.classList.toggle('finished', clock.end !== null);
    badge.title = clock.end === null
      ? 'Tiempo desde el envío a cocina'
      : 'Tiempo de cocina hasta marcar listo';
    if (clock.end === null) badge.dataset.kitchenStart = String(clock.start);
    updateKitchenClock(badge, clock.start, clock.end);
    return badge;
  }

  refreshKitchenClocks() {
    if (document.hidden) return;
    for (const badge of this.element.querySelectorAll('[data-kitchen-start]')) {
      updateKitchenClock(badge, Number(badge.dataset.kitchenStart), null);
    }
  }
}

function updateKitchenClock(badge, start, end) {
  const duration = formatKitchenDuration(start, end);
  badge.textContent = '⏱ ' + duration;
  badge.setAttribute('aria-label', badge.title + ': ' + duration);
}

function elapsedLabel(value) {
  const seconds = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 1000));
  if (seconds < 60) return 'Listo hace menos de un minuto';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return 'Listo hace ' + minutes + ' min';
  const hours = Math.floor(minutes / 60);
  return 'Listo hace ' + hours + ' h ' + (minutes % 60) + ' min';
}
