import { formatKitchenDuration, kitchenClock } from './kitchen-clock.js';

export function productFormatLabel(format) {
  if (!format) return '';
  const labels = {
    PLATO: 'Plato',
    RACION: 'Ración',
    MEDIA_RACION: 'Media ración',
    TAPA: 'Tapa',
    UNIDAD: 'Unidad',
    TERCIO: 'Tercio',
    VASO: 'Vaso',
    COPA: 'Copa',
    COMBO: 'Combo'
  };
  return ' [' + (labels[format] || format.replaceAll('_', ' ')) + ']';
}

export function orderDestinationLabel(order) {
  return order.tableLabel?.trim()
    || order.customerName?.trim()
    || 'Destino sin identificar';
}

export class OrderRenderer {
  constructor(element, options) {
    this.element = element;
    this.options = options;
    this.collapsedOrders = new Set();
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
      const titleContent = document.createElement('span');
      titleContent.className = 'order-title-content';
      const destination = document.createElement('span');
      destination.textContent = orderDestinationLabel(order);
      const shortId = document.createElement('small');
      shortId.className = 'order-short-id';
      shortId.textContent = order.id.slice(0, 8);
      shortId.title = order.id;
      titleContent.append(destination, shortId);

      const headerMeta = document.createElement('span');
      headerMeta.className = 'order-header-meta';
      const sourceBadge = document.createElement('span');
      sourceBadge.className = 'station';
      sourceBadge.textContent = 'Origen: ' + order.source;
      headerMeta.append(sourceBadge, this.renderKitchenClock(order, this.options.getStationCode()));

      const collapsed = this.collapsedOrders.has(order.id);
      const toggleButton = document.createElement('button');
      toggleButton.className = 'order-collapse secondary';
      toggleButton.type = 'button';
      toggleButton.textContent = collapsed ? '+' : '−';
      toggleButton.setAttribute('aria-expanded', String(!collapsed));
      toggleButton.setAttribute('aria-label', collapsed ? 'Expandir tarjeta' : 'Contraer tarjeta');
      toggleButton.title = collapsed ? 'Mostrar pedido' : 'Recoger tarjeta';
      title.append(titleContent, headerMeta, toggleButton);

      const age = document.createElement('small');
      age.className = 'order-age';
      age.textContent = order.status === 'Ready' && order.readyAtUtc
        ? elapsedLabel(order.readyAtUtc)
        : '';

      if (Number.isInteger(order.guestCount) && order.guestCount > 0) {
        const guests = document.createElement('span');
        guests.className = 'station guest-count';
        const guestIcon = document.createElement('span');
        guestIcon.className = 'guest-icon';
        guestIcon.textContent = '👥';
        const guestNumber = document.createElement('span');
        guestNumber.textContent = order.guestCount;
        guests.append(guestIcon, guestNumber);
        guests.title = order.guestCount + ' comensales';
        guests.setAttribute('aria-label', guests.title);
        headerMeta.append(guests);
      }

      const stations = document.createElement('div');
      stations.className = 'stations';
      const selectedStationCode = this.options.getStationCode();
      const isPrimary = selectedStationCode === this.options.getPrimaryStationCode();
      if (!isPrimary) {
        for (const station of order.stations) {
          if (station.code === selectedStationCode) continue;
          const stationBadge = document.createElement('span');
          stationBadge.className = 'station';
          stationBadge.textContent = station.name + ': ' + station.status;
          stations.append(stationBadge);
        }
      }

      const controlledCodes = new Set(order.stations
        .filter(station => station.requiresPrimaryDispatch)
        .map(station => station.code));
      const lines = document.createElement('ul');
      lines.className = 'product-lines';
      for (const line of order.lines) {
        if (isPrimary && controlledCodes.has(line.preparationStationCode)) continue;
        lines.append(this.renderProductLine(order, line, false, false, !isPrimary));
      }

      const body = document.createElement('div');
      body.className = 'order-body';
      body.hidden = collapsed;
      toggleButton.addEventListener('click', () => {
        if (body.hidden) this.collapsedOrders.delete(order.id);
        else this.collapsedOrders.add(order.id);
        body.hidden = !body.hidden;
        toggleButton.textContent = body.hidden ? '+' : '−';
        toggleButton.setAttribute('aria-expanded', String(!body.hidden));
        toggleButton.setAttribute('aria-label', body.hidden ? 'Expandir tarjeta' : 'Contraer tarjeta');
        toggleButton.title = body.hidden ? 'Mostrar pedido' : 'Recoger tarjeta';
      });

      article.append(title);
      if (age.textContent) body.append(age);
      if (!isPrimary) body.append(stations);
      body.append(lines);
      if (isPrimary) {
        body.append(this.renderChefPasses(order));
      }

      const canRecover = this.options.canRecover(order);
      if (canRecover) {
        const actions = document.createElement('div');
        actions.className = 'actions';
        const deliverButton = document.createElement('button');
        deliverButton.textContent = 'Entregado · resolver incidencia';
        deliverButton.disabled = isPending;
        deliverButton.addEventListener('click', () => this.options.onRecover(order, deliverButton));
        actions.append(deliverButton);
        body.append(actions);
      }
      article.append(body);
      this.element.append(article);
    }
  }

  showEmpty(message) {
    this.element.innerHTML = '<p class="empty"></p>';
    this.element.querySelector('.empty').textContent = message;
  }

  renderProductLine(order, line, isPrimary, blocked = false, stationActions = !isPrimary) {
    const item = document.createElement('li');
    item.className = 'product-line';
    item.append(
      this.renderProductDescription(line),
      this.renderProductControls(order, line, isPrimary, blocked, stationActions)
    );
    return item;
  }

  renderProductDescription(line) {
    const description = document.createElement('div');
    description.className = 'product-description';
    const name = document.createElement('span');
    name.className = 'product-name';
    const notes = line.notes || '';
    const extrasMarker = notes.indexOf('Extras:');
    let productNotes = notes;
    if (extrasMarker >= 0) {
      let end = extrasMarker;
      while (end > 0 && (notes[end - 1] === '·' || notes[end - 1].trim() === '')) end--;
      productNotes = notes.slice(0, end).trim();
    }
    name.textContent = line.quantity + ' × ' + line.productName
      + productFormatLabel(line.format) + ' · ' + line.categoryName
      + (productNotes ? ' — ' + productNotes : '');
    description.append(name);
    if (extrasMarker >= 0) {
      const extras = notes.slice(extrasMarker + 'Extras:'.length).trim();
      if (extras) {
        const extraLines = document.createElement('ul');
        extraLines.className = 'product-extras';
        for (const extra of extras.split(/,\s*/)) {
          const extraLine = document.createElement('li');
          extraLine.textContent = extra;
          extraLines.append(extraLine);
        }
        description.append(extraLines);
      }
    }
    return description;
  }

  renderProductControls(order, line, isPrimary, blocked, stationActions) {
    const state = document.createElement('small');
    state.className = 'line-state ' + (line.kitchenStatus || 'Pending');
    state.textContent = kitchenStatusLabel(line.kitchenStatus);
    const controls = document.createElement('div');
    controls.className = 'line-controls';
    if (!(isPrimary && line.kitchenStatus === 'Ready')) controls.append(state);

    if (isPrimary) {
      this.appendPrimaryControls(controls, order, line, blocked);
    } else {
      this.appendStationControls(controls, order, line, stationActions);
    }
    return controls;
  }

  appendPrimaryControls(controls, order, line, blocked) {
    controls.append(
      this.renderLineClock('Preparación', line.preparationStartedAtUtc, line.readyAtUtc),
      this.renderLineClock('Principal', line.readyAtUtc, line.dispatchedAtUtc)
    );
    if (line.kitchenStatus !== 'Ready') return;
    if (blocked) {
      const note = document.createElement('small');
      note.className = 'line-waiting';
      note.textContent = 'Esperando un pase anterior.';
      controls.append(note);
    } else {
      controls.append(this.createLineAction(order, line, 'dispatch', 'Despachar'));
    }
  }

  appendStationControls(controls, order, line, stationActions) {
    controls.append(this.renderLineClock(
      'Preparación', line.preparationStartedAtUtc, line.readyAtUtc
    ));
    if (stationActions && line.kitchenStatus === 'Pending') {
      controls.append(this.createLineAction(order, line, 'start-preparation', 'Iniciar'));
    } else if (stationActions && line.kitchenStatus === 'InPreparation') {
      controls.append(this.createLineAction(order, line, 'ready', 'Listo'));
    }
  }

  createLineAction(order, line, action, label) {
    const button = document.createElement('button');
    button.className = 'line-action';
    button.textContent = label;
    button.disabled = this.options.isOrderPending(order.id);
    button.addEventListener('click', () => this.options.onAdvance(order, {
      path: 'lines/' + encodeURIComponent(line.id) + '/' + action,
      label
    }, button));
    return button;
  }

  renderLineClock(label, start, end) {
    const badge = document.createElement('small');
    badge.className = 'line-clock';
    badge.title = label;
    if (!start) {
      badge.textContent = '🕓 —';
      return badge;
    }
    const startMilliseconds = new Date(start).getTime();
    const endMilliseconds = end ? new Date(end).getTime() : null;
    badge.dataset.kitchenStart = String(startMilliseconds);
    if (endMilliseconds !== null) badge.dataset.kitchenEnd = String(endMilliseconds);
    updateKitchenClock(badge, startMilliseconds, endMilliseconds, label);
    return badge;
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
      header.append(name);

      const blocked = controlled.some(other =>
        other.priority < station.priority
        && other.status !== 'Dispatched'
        && other.status !== 'Cancelled'
      );
      const lines = document.createElement('ul');
      lines.className = 'product-lines pass-lines';
      for (const line of order.lines.filter(line => line.preparationStationCode === station.code)) {
        lines.append(this.renderProductLine(order, line, true, blocked, false));
      }
      pass.append(header, lines);

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
      badge.textContent = '🕓 —';
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
      updateKitchenClock(
        badge,
        Number(badge.dataset.kitchenStart),
        badge.dataset.kitchenEnd ? Number(badge.dataset.kitchenEnd) : null,
        badge.title
      );
    }
  }
}

function updateKitchenClock(badge, start, end, label = badge.title) {
  const duration = formatKitchenDuration(start, end);
  badge.textContent = '🕓 ' + duration;
  badge.setAttribute('aria-label', label + ': ' + duration);
}

function kitchenStatusLabel(status) {
  return ({
    Pending: 'Pendiente',
    InPreparation: 'Preparando',
    Ready: 'Listo para despacho',
    Dispatched: 'Despachado'
  })[status] || 'Pendiente';
}

function elapsedLabel(value) {
  const seconds = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 1000));
  if (seconds < 60) return 'Listo hace menos de un minuto';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return 'Listo hace ' + minutes + ' min';
  const hours = Math.floor(minutes / 60);
  return 'Listo hace ' + hours + ' h ' + (minutes % 60) + ' min';
}
