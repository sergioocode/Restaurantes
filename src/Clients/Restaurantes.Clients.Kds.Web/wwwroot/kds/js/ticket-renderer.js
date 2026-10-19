export function renderKitchenTicket(ticketContent, order, line, station, dispatched) {
  ticketContent.replaceChildren();
  const title = document.createElement('h2');
  title.id = 'ticketTitle';
  title.textContent = station.name;
  const destination = document.createElement('p');
  const destinationLabel = order.tableLabel || order.customerName || 'Pedido';
  let separatorIndex = destinationLabel.indexOf('-');
  while (
    separatorIndex !== -1
    && !(
      separatorIndex > 0
      && separatorIndex < destinationLabel.length - 1
      && /\s/u.test(destinationLabel[separatorIndex - 1])
      && /\s/u.test(destinationLabel[separatorIndex + 1])
    )
  ) {
    separatorIndex = destinationLabel.indexOf('-', separatorIndex + 1);
  }
  const tableParts =
    separatorIndex === -1
      ? [destinationLabel]
      : [
          destinationLabel.slice(0, separatorIndex).trimEnd(),
          destinationLabel.slice(separatorIndex + 1).trimStart(),
        ];
  destination.textContent =
    tableParts.length === 2
      ? 'Mesa: ' + tableParts[0] + ' · Zona: ' + tableParts[1]
      : 'Destino: ' + tableParts[0];
  const separator = document.createElement('hr');
  const product = document.createElement('strong');
  product.className = 'ticket-product';
  product.textContent =
    line.quantity + ' × ' + line.productName
    + (line.format ? ' · ' + line.format.replaceAll('_', ' ').toLowerCase() : '');
  ticketContent.append(title, destination, separator, product);
  if (line.notes?.trim()) {
    const notes = document.createElement('p');
    notes.className = 'ticket-notes';
    notes.textContent = line.notes.trim();
    ticketContent.append(notes);
  }
  const arrival = document.createElement('p');
  arrival.textContent =
    'Llega: ' + formatTicketTime(line.preparationStartedAtUtc || order.submittedAtUtc);
  const pass = document.createElement('p');
  pass.textContent =
    'Pase: ' + formatTicketTime(dispatched ? line.dispatchedAtUtc : line.readyAtUtc);
  ticketContent.append(arrival, pass);
  if (station.isPrimary && line.preparationStartedAtUtc && line.readyAtUtc) {
    const previousStation = order.stations.find(
      item => item.code === line.preparationStationCode && item.code !== station.code
    );
    if (previousStation) {
      const previousTime = document.createElement('p');
      previousTime.textContent =
        'Tiempo en ' + previousStation.name + ': '
        + formatTicketDuration(line.preparationStartedAtUtc, line.readyAtUtc);
      ticketContent.append(previousTime);
    }
  }
}

function formatTicketTime(value) {
  if (!value) return '--:--';
  return new Intl.DateTimeFormat('es-ES', {
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).format(new Date(value));
}

function formatTicketDuration(start, end) {
  const totalSeconds = Math.max(
    0,
    Math.floor((new Date(end).getTime() - new Date(start).getTime()) / 1000)
  );
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return minutes + ':' + String(seconds).padStart(2, '0');
}
