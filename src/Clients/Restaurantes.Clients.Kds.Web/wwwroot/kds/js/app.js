import { KdsApiClient } from './api-client.js';
import { orderDestinationLabel, OrderRenderer } from './order-renderer.js';
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
const logoutButton = document.querySelector('#logout');
const restaurantSelect = document.querySelector('#restaurantId');
const stationSelect = document.querySelector('#stationCode');
const status = document.querySelector('#connectionStatus');
const identityStatus = document.querySelector('#identityStatus');
const eventText = document.querySelector('#event');
const ordersElement = document.querySelector('#orders');

const api = new KdsApiClient(apiBaseUrl);
let login;
let restaurantAccesses = [];
let primaryStationCode = '';

const renderer = new OrderRenderer(ordersElement, {
  getStationCode: () => stationSelect.value,
  getPrimaryStationCode: () => primaryStationCode,
  canRecover: canConfirmDelivery,
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
  if (!restaurantSelect.value || !stationSelect.value) return;
  renderer.render(await api.getOrders(restaurantSelect.value, stationSelect.value));
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
  actionButton.disabled = true;
  eventText.textContent = 'Enviando comando para ' + orderDestinationLabel(order) + '…';
  try {
    await api.sendOrderCommand(order.id, action.path);
    eventText.textContent = 'Comando aceptado; esperando la actualización en tiempo real…';
  } catch (error) {
    eventText.textContent = error.message;
    actionButton.disabled = false;
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
  localStorage.setItem(restaurantKey, restaurantId);
  updateRestaurantIdentity();
  stationSelect.disabled = true;
  stationSelect.replaceChildren();
  setStatus(false, 'Cargando estaciones…');
  try {
    const stations = await api.getStations(restaurantId);
    const primary = stations.find(item => item.isActive && item.isPrimary);
    primaryStationCode = primary ? 'CHEF' : '';
    if (primary) {
      addOption(stationSelect, 'CHEF', primary.code + ' · ' + primary.name, primary.name);
    }
    for (const station of stations
      .filter(item => item.isActive && !item.isPrimary)
      .sort((a, b) => a.priority - b.priority || a.name.localeCompare(b.name))) {
      addOption(stationSelect, station.code, station.code + ' · ' + station.name, station.name);
    }
    const stationKey = 'restaurantes.kds.station.' + restaurantId;
    const remembered = localStorage.getItem(stationKey);
    stationSelect.value = Array.from(stationSelect.options).some(option => option.value === remembered)
      ? remembered
      : (primary ? 'CHEF' : (stationSelect.options[0]?.value || ''));
    stationSelect.disabled = false;
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
      permissions: access?.permissions || (
        allRestaurantsRole === 'Admin' || allRestaurantsRole === 'Manager'
          ? ['kds.use', 'orders.recover']
          : ['kds.use']
      ),
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
  api.setAccessToken('');
  realtime.disconnect();
  sessionStorage.removeItem(sessionKey);
  authLoading.classList.add('hidden');
  loginPanel.classList.remove('hidden');
  monitorPanel.classList.add('hidden');
  refreshButton.disabled = true;
  identityStatus.textContent = '';
  eventText.textContent = '';
  renderer.showEmpty('Inicia sesión para cargar el monitor.');
  setStatus(false, 'Inicia sesión');
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
logoutButton.addEventListener('click', logout);
restaurantSelect.addEventListener('change', loadStationsAndConnect);
stationSelect.addEventListener('change', () => {
  localStorage.setItem('restaurantes.kds.station.' + restaurantSelect.value, stationSelect.value);
  eventText.textContent = '';
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
}, 15000);

if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('./service-worker.js', { updateViaCache: 'none' });
  });
}
