import { KdsApiClient } from './api-client.js';
import { orderDestinationLabel, OrderRenderer, productFormatLabel } from './order-renderer.js';
import { KdsRealtimeClient } from './realtime-client.js';

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
const logoutButton = document.querySelector('#logout');
const restaurantSelect = document.querySelector('#restaurantId');
const stationSelect = document.querySelector('#stationCode');
const status = document.querySelector('#connectionStatus');
const identityStatus = document.querySelector('#identityStatus');
const eventText = document.querySelector('#event');
const ordersElement = document.querySelector('#orders');
const orderPollingIntervalMilliseconds = 30_000;
let historyOpen = false;

const api = new KdsApiClient(apiBaseUrl);
let login;
let restaurantAccesses = [];
let primaryStationCode = '';
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
  if (!login || !restaurantSelect.value || !stationSelect.value) return;
  const restaurantId = restaurantSelect.value;
  const stationCode = stationSelect.value;
  const view = restaurantId + '/' + stationCode;
  if (renderedView !== view) {
    renderedView = view;
    renderedOrders = [];
  }
  const requestNumber = ++ordersRequestNumber;
  const response = await api.getOrders(restaurantId, stationCode);
  if (!login || requestNumber < lastAppliedRequestNumber
    || restaurantId !== restaurantSelect.value || stationCode !== stationSelect.value) return;
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
  const available = Boolean(login && selectedStation);
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
  if (!login || !restaurantSelect.value || !stationSelect.value) return;
  refreshButton.disabled = true;
  eventText.textContent = 'Actualizando monitor…';
  try {
    await loadOrders();
    eventText.textContent = 'Monitor actualizado.';
  } catch (error) {
    eventText.textContent = error.message;
  } finally {
    refreshButton.disabled = !login;
  }
}

function canConfirmDelivery(order) {
  if (order.serviceMode !== 'Takeaway' || order.status !== 'Ready') return false;
  const access = restaurantAccesses.find(item => item.restaurantId === restaurantSelect.value);
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

async function recoverDelivery(order, button) {
  if (!confirm('Confirma que el pedido de ' + (order.customerName || 'este cliente') + ' ya fue entregado.')) return;
  await advanceOrder(order, { path: 'deliver', label: button.textContent }, button);
}

function connect() {
  if (!login || !restaurantSelect.value || !stationSelect.value) return;
  const selectedStation = stationSelect.options[stationSelect.selectedIndex];
  realtime.connect({
    accessToken: login.accessToken,
    restaurantId: restaurantSelect.value,
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

function updateRestaurantIdentity() {
  const access = restaurantAccesses.find(item => item.restaurantId === restaurantSelect.value);
  identityStatus.textContent = access?.name
    ? 'Local: ' + access.name
    : 'Local: ' + (access?.restaurantId || '');
}

async function loadStationsAndConnect() {
  const restaurantId = restaurantSelect.value;
  if (!restaurantId) return;
  realtime.disconnect();
  closeHistory();
  clearOrdersView('Cargando pedidos…');
  localStorage.setItem(restaurantKey, restaurantId);
  updateRestaurantIdentity();
  stationSelect.disabled = true;
  stationSelect.replaceChildren();
  setStatus(false, 'Cargando estaciones…');
  try {
    const stations = await api.getStations(restaurantId);
    const activeStations = stations
      .filter(item => item.isActive)
      .sort((a, b) => a.priority - b.priority || a.name.localeCompare(b.name));
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
      return;
    }
    const stationKey = 'restaurantes.kds.station.' + restaurantId;
    const remembered = localStorage.getItem(stationKey);
    stationSelect.value = Array.from(stationSelect.options).some(option => option.value === remembered)
      ? remembered
      : (primary ? 'CHEF' : (stationSelect.options[0]?.value || ''));
    stationSelect.disabled = false;
    updateHistoryVisibility();
    eventText.textContent = '';
    connect();
  } catch (error) {
    eventText.textContent = error.message;
    setStatus(false, 'No se pudieron cargar las estaciones');
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
  if (restaurantAccesses.some(access => access.restaurantId === remembered)) {
    restaurantSelect.value = remembered;
  }
  await loadStationsAndConnect();
}

async function openHistory() {
  if (!login || !restaurantSelect.value || !stationSelect.value) return;
  historyButton.disabled = true;
  historyList.replaceChildren();
  try {
    const orders = await api.getHistory(restaurantSelect.value, stationSelect.value);
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
  restaurantAccesses = [];
  pendingOrders.clear();
  api.setAccessToken('');
  realtime.disconnect();
  sessionStorage.removeItem(sessionKey);
  authLoading.classList.add('hidden');
  loginPanel.classList.remove('hidden');
  monitorPanel.classList.add('hidden');
  refreshButton.disabled = true;
  identityStatus.textContent = '';
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
  else openHistory();
});
logoutButton.addEventListener('click', logout);
restaurantSelect.addEventListener('change', loadStationsAndConnect);
stationSelect.addEventListener('change', () => {
  closeHistory();
  localStorage.setItem('restaurantes.kds.station.' + restaurantSelect.value, stationSelect.value);
  eventText.textContent = '';
  clearOrdersView('Cargando pedidos…');
  updateHistoryVisibility();
  connect();
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
  if (!login || !restaurantSelect.value || !stationSelect.value) return;
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
