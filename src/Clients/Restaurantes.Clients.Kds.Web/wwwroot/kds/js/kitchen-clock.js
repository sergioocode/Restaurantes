// Derived entirely from existing timestamps. Never persist elapsed time.
function timestamp(value) {
  if (!value) return null;
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

export function kitchenClock(order, stationCode) {
  const start = timestamp(order.submittedAtUtc);
  if (start === null) return null;
  const stations = (order.stations || []).filter(station => station.status !== 'Cancelled');
  const selected = stationCode === 'CHEF'
    ? stations
    : stations.filter(station => station.code === stationCode);
  if (!selected.length || order.status === 'Cancelled') return null;
  const completed = selected.every(station => station.status === 'Ready' || station.status === 'Dispatched');
  const ends = selected.map(station => timestamp(station.readyAtUtc));
  // Do not substitute dispatch time or keep counting on incomplete historical data.
  if (completed && ends.some(end => end === null)) return null;
  return { start, end: completed ? Math.max(start, ...ends) : null };
}

export function formatKitchenDuration(start, end, now = Date.now()) {
  const seconds = Math.max(0, Math.floor(((end ?? now) - start) / 1000));
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor(seconds / 60) % 60;
  const remainder = seconds % 60;
  return (hours ? hours + ':' : '')
    + String(hours ? minutes : Math.floor(seconds / 60)).padStart(2, '0')
    + ':' + String(remainder).padStart(2, '0');
}