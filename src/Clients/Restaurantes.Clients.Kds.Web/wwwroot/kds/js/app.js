import { KdsApiClient } from './api-client.js';
import { orderDestinationLabel, OrderRenderer, productFormatLabel } from './order-renderer.js';
import { KdsRealtimeClient } from './realtime-client.js';
import { renderKitchenTicket } from './ticket-renderer.js';

const sessionKey = 'restaurantes.kds.login';
const restaurantKey = 'restaurantes.kds.restaurant';
const apiBaseUrl = window.location.origin;
const authLoading = document.querySelector('#authLoading');
const loginPanel = document.querySelector('#loginPanel');
const monitorPanel = document.querySelector('#monitorPanel');
const loginMicrosoft = document.querySelector('#loginMicrosoft');
const loginGoogle = document.querySelector('#loginGoogle');
const refreshButton = document.querySelector('#refresh');
const historyButton = document.querySelector('#historyButton');
const historyPanel = document.querySelector('#historyPanel');
const historyList = document.querySelector('#historyList');
const ticketDialog = document.querySelector('#ticketDialog');
const ticketContent = document.querySelector('#ticketContent');
const closeTicketButton = document.querySelector('#closeTicket');
const logoutButton = document.querySelector('#logout');
const restaurantSelect = document.querySelector('#restaurantId');
const stationSelect = document.querySelector('#stationCode');
const zoneSelect = document.querySelector('#zoneId');
const status = document.querySelector('#connectionStatus');
const eventText = document.querySelector('#event');
const ordersElement = document.querySelector('#orders');
const orderPollingIntervalMilliseconds = 30_000;
let historyOpen = false;

const api = new KdsApiClient(apiBaseUrl);
let login;
let selectedRestaurantId = '';
let restaurantAccesses = [];
let primaryStationCode = '';
let stationConfigurations = [];
let restaurantZones = [];
const pendingOrders = new Map();
let renderedOrders = [];
let renderedView = '';
let ordersRequestNumber = 0;
let lastAppliedRequestNumber = 0;

const renderer = new OrderRenderer(ordersElement, {
  getStationCode: () => stationSelect.value,
  getPrimaryStationCode: () => primaryStationCode,
  canRecover: canConfirmDelivery,
  isOrderPending: orderId => pendingOrders.has(orderId),
  onAdvance: advanceOrder,
  onRecover: recoverDelivery
});

const realtime = new KdsRealtimeClient(apiBaseUrl, {
  onStatus: setStatus,
  onConnected: loadOrders,
  onOrderUpdated: async notification => {
    eventText.textContent = 'Actualización recibida: pedido ' + notification.orderId
      + ', ' + notification.status + ' v' + notification.version;
    await new Promise(resolve => window.setTimeout(resolve, 150));
    await loadOrders();
  }
});

function setStatus(connected, text) {
  status.classList.toggle('connected', connected);
  status.querySelector('span:last-child').textContent = text;
}

async function loadOrders() {
  if (!login || !selectedRestaurantId || !stationSelect.value) return;
  const restaurantId = selectedRestaurantId;
  const stationCode = stationSelect.value;
  const zoneId = selectedZoneId();
  if (selectedStationConfiguration()?.managesZones && !zoneId) {
    clearOrdersView('No hay zonas activas para este local. Configúralas en Backoffice.');
    return;
  }
  const view = restaurantId + '/' + stationCode + '/' + (zoneId ?? 'all');
  if (renderedView !== view) {
    renderedView = view;
    renderedOrders = [];
  }
  const requestNumber = ++ordersRequestNumber;
  const response = await api.getOrders(restaurantId, stationCode, zoneId);
  if (!login || requestNumber < lastAppliedRequestNumber
    || restaurantId !== selectedRestaurantId || stationCode !== stationSelect.value
    || zoneId !== selectedZoneId()) return;
  lastAppliedRequestNumber = requestNumber;

  const previousOrders = new Map(renderedOrders.map(order => [order.id, order]));
  const orders = response.map(order => {
    const previous = previousOrders.get(order.id);
    return previous && previous.version > order.version ? previous : order;
  });
  for (const [orderId, pending] of pendingOrders) {
    if (pending.minimumVersion === null) continue;
    const current = orders.find(order => order.id === orderId);
    if (!current || current.version >= pending.minimumVersion) pendingOrders.delete(orderId);
  }
  renderedOrders = orders;
  renderer.render(orders);
}

function closeHistory() {
  historyOpen = false;
  historyPanel.classList.add('hidden');
  ordersElement.classList.remove('hidden');
  historyButton.setAttribute('aria-expanded', 'false');
}

function updateHistoryVisibility() {
  const selectedStation = stationSelect.options[stationSelect.selectedIndex];
  const available = Boolean(login && selectedStation
    && (!selectedStationConfiguration()?.managesZones || zoneSelect.value));
  historyButton.classList.toggle('hidden', !available);
  historyButton.disabled = false;
  historyButton.title = '';
  historyButton.textContent = 'Historial';
  historyButton.setAttribute('aria-expanded', String(historyOpen));
  if (!available) closeHistory();
}

function clearOrdersView(message) {
  lastAppliedRequestNumber = ++ordersRequestNumber;
  renderedOrders = [];
  renderedView = '';
  renderer.showEmpty(message);
}

async function refreshMonitor() {
  if (!login || !selectedRestaurantId || !stationSelect.value) return;
  refreshButton.disabled = true;
  eventText.textContent = 'Actualizando monitor…';
  try {
    if (await loadStationsAndConnect()) {
      await loadOrders();
      eventText.textContent = 'Monitor actualizado.';
    }
  } catch (error) {
    eventText.textContent = error.message;
  } finally {
    refreshButton.disabled = !login;
  }
}

function canConfirmDelivery(order) {
  if (order.serviceMode !== 'Takeaway' || order.status !== 'Ready') return false;
  const access = restaurantAccesses.find(item => item.restaurantId === selectedRestaurantId);
  return Boolean(access?.permissions?.includes('orders.recover'));
}

async function advanceOrder(order, action, actionButton) {
  if (pendingOrders.has(order.id)) return;
  const pending = { minimumVersion: null };
  pendingOrders.set(order.id, pending);
  for (const button of actionButton.closest('article.order')?.querySelectorAll('button') || []) {
    button.disabled = true;
  }
  eventText.textContent = 'Enviando comando para ' + orderDestinationLabel(order) + '…';
  try {
    const updatedOrder = await api.sendOrderCommand(order.id, action.path);
    pending.minimumVersion = Math.max(order.version + 1, updatedOrder.version);
    await showTicketAfterStationCompletion(updatedOrder, action.path);
    eventText.textContent = 'Comando aceptado; actualizando el pedido…';
  } catch (error) {
    if (error.status === 409) {
      pending.minimumVersion = order.version + 1;
      eventText.textContent = 'El estado del pedido cambió; esperando la actualización…';
    } else {
      pendingOrders.delete(order.id);
      eventText.textContent = error.message;
    }
  }
  try {
    await loadOrders();
  } catch (error) {
    if (!pendingOrders.has(order.id)) renderer.render(renderedOrders);
    eventText.textContent += ' No se pudo actualizar el monitor: ' + error.message;
  }
}

async function showTicketAfterStationCompletion(order, path) {
  if (!path.endsWith('/ready') && !path.endsWith('/dispatch')) return;
  const restaurantId = selectedRestaurantId;
  const selectedCode = stationSelect.value;
  try {
    stationConfigurations = await api.getStations(restaurantId);
    if (restaurantId !== selectedRestaurantId || selectedCode !== stationSelect.value) return;
    const selectedStation = stationConfigurations.find(station =>
      selectedCode === primaryStationCode ? station.isPrimary : station.code === selectedCode
    );
    if (!selectedStation?.printTicket) return;
    const lineId = decodeURIComponent(path.split('/').at(-2) || '');
    const line = order.lines.find(item => item.id === lineId);
    if (!line) return;
    renderKitchenTicket(ticketContent, order, line, selectedStation, path.endsWith('/dispatch'));
    if (!ticketDialog.open) ticketDialog.showModal();
  } catch (error) {
    console.warn('No se pudo mostrar el ticket KDS.', error);
  }
}

async function recoverDelivery(order, button) {
  if (!confirm('Confirma que el pedido de ' + (order.customerName || 'este cliente') + ' ya fue entregado.')) return;
  await advanceOrder(order, { path: 'deliver', label: button.textContent }, button);
}

function connect() {
  if (!login || !selectedRestaurantId || !stationSelect.value) return;
  const selectedStation = stationSelect.options[stationSelect.selectedIndex];
  realtime.connect({
    accessToken: login.accessToken,
    restaurantId: selectedRestaurantId,
    stationCode: stationSelect.value,
    stationName: selectedStation.dataset.statusText
  });
}

function addOption(select, value, text, statusText = text) {
  const option = document.createElement('option');
  option.value = value;
  option.textContent = text;
  option.dataset.statusText = statusText;
  select.append(option);
}

function selectedStationConfiguration() {
  return stationConfigurations.find(station =>
    stationSelect.value === 'CHEF' ? station.isPrimary : station.code === stationSelect.value
  );
}

function selectedZoneId() {
  return selectedStationConfiguration()?.managesZones ? zoneSelect.value : null;
}

function configureZoneSelector() {
  const managesZones = Boolean(selectedStationConfiguration()?.managesZones);
  zoneSelect.replaceChildren();
  if (managesZones) {
    for (const zone of restaurantZones.filter(zone => !zone.deletedAtUtc)
      .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name))) {
      addOption(zoneSelect, zone.id, zone.name);
    }
  }
  zoneSelect.disabled = !managesZones || zoneSelect.options.length === 0;
  if (!managesZones) return;
  const remembered = localStorage.getItem(
    'restaurantes.kds.zone.' + selectedRestaurantId + '.' + stationSelect.value
  );
  if (Array.from(zoneSelect.options).some(option => option.value === remembered)) {
    zoneSelect.value = remembered;
  }
}

async function loadStationsAndConnect() {
  const restaurantId = selectedRestaurantId;
  if (!restaurantId) return;
  realtime.disconnect();
  closeHistory();
  clearOrdersView('Cargando pedidos…');
  stationSelect.disabled = true;
  stationSelect.replaceChildren();
  setStatus(false, 'Cargando estaciones…');
  try {
    const stations = await api.getStations(restaurantId);
    if (restaurantId !== selectedRestaurantId) return;
    const activeStations = stations
      .filter(item => item.isActive)
      .sort((a, b) => a.priority - b.priority || a.name.localeCompare(b.name));
    const zones = activeStations.some(station => station.managesZones)
      ? await api.getZones(restaurantId) : [];
    if (restaurantId !== selectedRestaurantId) return;
    stationConfigurations = stations;
    restaurantZones = zones;
    const primary = activeStations.find(item => item.isPrimary);
    primaryStationCode = primary ? 'CHEF' : '';
    if (primary) {
      addOption(stationSelect, 'CHEF', primary.code + ' · ' + primary.name, primary.name);
    }
    for (const station of activeStations.filter(item => !item.isPrimary)) {
      addOption(stationSelect, station.code, station.code + ' · ' + station.name, station.name);
    }
    if (stationSelect.options.length === 0) {
      stationSelect.disabled = true;
      clearOrdersView('Este local no tiene estaciones KDS activas. Configúralas en Backoffice.');
      setStatus(false, 'Sin estaciones KDS activas');
      updateHistoryVisibility();
      return false;
    }
    const stationKey = 'restaurantes.kds.station.' + restaurantId;
    const remembered = localStorage.getItem(stationKey);
    stationSelect.value = Array.from(stationSelect.options).some(option => option.value === remembered)
      ? remembered
      : (primary ? 'CHEF' : (stationSelect.options[0]?.value || ''));
    stationSelect.disabled = false;
    configureZoneSelector();
    updateHistoryVisibility();
    eventText.textContent = '';
    connect();
    return true;
  } catch (error) {
    if (restaurantId !== selectedRestaurantId) return;
    eventText.textContent = error.message;
    setStatus(false, 'No se pudieron cargar las estaciones');
    return false;
  }
}

async function activateLogin(loginResponse) {
  login = loginResponse;
  api.setAccessToken(login.accessToken);
  sessionStorage.setItem(sessionKey, JSON.stringify(login));
  authLoading.classList.add('hidden');
  loginPanel.classList.add('hidden');
  monitorPanel.classList.remove('hidden');
  refreshButton.disabled = false;
  setStatus(false, 'Cargando monitor…');
  const loginAccesses = Array.isArray(loginResponse.restaurants)
    ? new Map(loginResponse.restaurants.map(access => [access.restaurantId, access]))
    : new Map();
  const allRestaurantsRole = Array.isArray(loginResponse.allRestaurantsRoles)
    ? loginResponse.allRestaurantsRoles[0]
    : null;
  const restaurants = await api.getKdsRestaurants();
  restaurantAccesses = restaurants.map(restaurant => {
    const access = loginAccesses.get(restaurant.id);
    return {
      restaurantId: restaurant.id,
      role: access?.role || allRestaurantsRole || 'Kds',
      permissions: access?.permissions || loginResponse.allRestaurantsPermissions || [],
      name: restaurant.name,
      code: restaurant.code
    };
  });
  if (restaurantAccesses.length === 0) {
    throw new Error('El usuario no tiene acceso KDS en ningún local.');
  }
  restaurantSelect.replaceChildren();
  for (const access of restaurantAccesses) {
    const label = access.name
      ? access.name + (access.code ? ' · ' + access.code : '')
      : access.restaurantId + ' · ' + access.role;
    addOption(restaurantSelect, access.restaurantId, label);
  }
  const remembered = localStorage.getItem(restaurantKey);
  selectedRestaurantId = restaurantAccesses.some(access => access.restaurantId === remembered)
    ? remembered
    : restaurantAccesses[0].restaurantId;
  restaurantSelect.value = selectedRestaurantId;
  restaurantSelect.disabled = restaurantAccesses.length <= 1;
  await loadStationsAndConnect();
}

async function openHistory() {
  if (!login || !selectedRestaurantId || !stationSelect.value) return;
  historyButton.disabled = true;
  historyList.replaceChildren();
  try {
    const orders = await api.getHistory(selectedRestaurantId, stationSelect.value, selectedZoneId());
    for (const order of orders) {
      const card = document.createElement('article');
      card.className = 'history-order';
      const title = document.createElement('strong');
      const location = order.tableLabel || order.customerName || 'Pedido';
      title.textContent = location + ' · ' + order.orderId.slice(0, 8);
      const time = document.createElement('small');
      time.textContent = new Date(order.processedAtUtc || order.createdAtUtc)
        .toLocaleString('es-ES') + ' · ' + order.status + ' · Caja '
        + (order.cashRegisterSessionId ? order.cashRegisterSessionId.slice(0, 8) : 'sin vínculo');
      card.append(title, time);
      for (const line of order.lines) {
        const row = document.createElement('p');
        row.textContent = line.quantity + ' × ' + line.productName
          + productFormatLabel(line.format)
          + (line.status === 'Cancelled' ? ' · Cancelado' : '');
        card.append(row);
      }
      historyList.append(card);
    }
    if (!orders.length) {
      const empty = document.createElement('p');
      empty.className = 'empty';
      empty.textContent = 'No hay pedidos procesados en esta estación durante el día operativo.';
      historyList.append(empty);
    }
    historyOpen = true;
    ordersElement.classList.add('hidden');
    historyPanel.classList.remove('hidden');
    historyButton.setAttribute('aria-expanded', 'true');
  } catch (error) {
    eventText.textContent = error.message;
  } finally {
    historyButton.disabled = false;
  }
}
function startLogin(provider) {
  const returnPath = encodeURIComponent('/kds/');
  window.location.assign('/api/identity/auth/start/' + provider + '?returnPath=' + returnPath);
}

async function loadProvider() {
  const settings = await api.provider();
  loginMicrosoft.disabled = settings.activeProvider !== 'Microsoft' || !settings.microsoftConfigured;
  loginGoogle.disabled = settings.activeProvider !== 'Google' || !settings.googleConfigured;
}

function logout() {
  login = null;
  selectedRestaurantId = '';
  restaurantAccesses = [];
  restaurantZones = [];
  stationConfigurations = [];
  pendingOrders.clear();
  api.setAccessToken('');
  realtime.disconnect();
  sessionStorage.removeItem(sessionKey);
  authLoading.classList.add('hidden');
  loginPanel.classList.remove('hidden');
  monitorPanel.classList.add('hidden');
  refreshButton.disabled = true;
  restaurantSelect.replaceChildren();
  restaurantSelect.disabled = true;
  zoneSelect.replaceChildren();
  zoneSelect.disabled = true;
  eventText.textContent = '';
  clearOrdersView('Accede para cargar el monitor.');
  historyButton.classList.add('hidden');
  closeHistory();
  setStatus(false, 'Sin autenticar');
}

async function restoreLogin() {
  const storedLogin = sessionStorage.getItem(sessionKey);
  if (!storedLogin) {
    authLoading.classList.add('hidden');
    loginPanel.classList.remove('hidden');
    return;
  }
  try {
    const session = JSON.parse(storedLogin);
    if (new Date(session.expiresAtUtc) <= new Date()) throw new Error('La sesión ha caducado.');
    await activateLogin(session);
  } catch (error) {
    if (!login) {
      logout();
    } else {
      authLoading.classList.add('hidden');
      loginPanel.classList.add('hidden');
      monitorPanel.classList.remove('hidden');
    }
    eventText.textContent = error.message;
  }
}

loginMicrosoft.addEventListener('click', () => startLogin('Microsoft'));
loginGoogle.addEventListener('click', () => startLogin('Google'));
refreshButton.addEventListener('click', refreshMonitor);
historyButton.addEventListener('click', () => {
  if (historyOpen) closeHistory();
  else openHistory().catch(error => { eventText.textContent = error.message; });
});
logoutButton.addEventListener('click', logout);
closeTicketButton.addEventListener('click', () => ticketDialog.close());
ticketDialog.addEventListener('click', event => {
  if (event.target === ticketDialog) ticketDialog.close();
});
restaurantSelect.addEventListener('change', () => {
  const restaurantId = restaurantSelect.value;
  if (!restaurantAccesses.some(access => access.restaurantId === restaurantId)) return;
  selectedRestaurantId = restaurantId;
  localStorage.setItem(restaurantKey, restaurantId);
  pendingOrders.clear();
  loadStationsAndConnect().catch(error => { eventText.textContent = error.message; });
});
stationSelect.addEventListener('change', () => {
  closeHistory();
  localStorage.setItem('restaurantes.kds.station.' + selectedRestaurantId, stationSelect.value);
  eventText.textContent = '';
  clearOrdersView('Cargando pedidos…');
  configureZoneSelector();
  updateHistoryVisibility();
  connect();
});
zoneSelect.addEventListener('change', () => {
  localStorage.setItem(
    'restaurantes.kds.zone.' + selectedRestaurantId + '.' + stationSelect.value,
    zoneSelect.value
  );
  closeHistory();
  clearOrdersView('Cargando pedidos…');
  loadOrders().catch(error => { eventText.textContent = error.message; });
});

async function initialize() {
  try {
    await loadProvider();
    const query = new URLSearchParams(window.location.hash.slice(1));
    const code = query.get('login_code');
    if (code) {
      history.replaceState(null, '', window.location.pathname);
      await activateLogin(await api.exchange(code));
      return;
    }
    if (query.has('login_error')) {
      history.replaceState(null, '', window.location.pathname);
      eventText.textContent = 'La cuenta no está autorizada o el proveedor rechazó el acceso.';
      authLoading.classList.add('hidden');
      loginPanel.classList.remove('hidden');
      return;
    }
    await restoreLogin();
  } catch (error) {
    eventText.textContent = error.message;
    authLoading.classList.add('hidden');
    if (login) {
      loginPanel.classList.add('hidden');
      monitorPanel.classList.remove('hidden');
    } else {
      loginPanel.classList.remove('hidden');
      monitorPanel.classList.add('hidden');
    }
  }
}
initialize();
window.setInterval(async () => {
  if (!login || !selectedRestaurantId || !stationSelect.value) return;
  try {
    await loadOrders();
  } catch {
    // SignalR mostrará el estado de conexión.
  }
}, orderPollingIntervalMilliseconds);

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.addEventListener('controllerchange', () => window.location.reload());
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('./service-worker.js', { updateViaCache: 'none' });
  });
}
