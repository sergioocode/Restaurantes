export class KdsApiClient {
  constructor(baseUrl = window.location.origin) {
    this.baseUrl = baseUrl;
    this.accessToken = '';
  }

  setAccessToken(accessToken) {
    this.accessToken = accessToken || '';
  }

  async provider() {
    const response = await fetch(this.baseUrl + '/api/identity/auth/provider');
    if (!response.ok) throw new Error('No se pudo consultar el proveedor.');
    return response.json();
  }

  async exchange(code) {
    const response = await fetch(this.baseUrl + '/api/identity/auth/exchange', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ code })
    });
    if (!response.ok) throw new Error('La cuenta no está autorizada.');
    return response.json();
  }

  async getOrders(restaurantId, stationCode) {
    const query = new URLSearchParams({ restaurantId, stationCode });
    const response = await this.fetch('/api/orders?' + query);
    if (!response.ok) throw new Error('No se pudo consultar el KDS: HTTP ' + response.status);
    const payload = await response.json();
    return Array.isArray(payload) ? payload : [payload];
  }

  async getStations(restaurantId) {
    const response = await this.fetch('/api/catalog/restaurants/' + restaurantId + '/stations');
    if (!response.ok) throw new Error('No se pudieron cargar las estaciones KDS: HTTP ' + response.status);
    return response.json();
  }

  async getKdsRestaurants() {
    const response = await this.fetch('/api/restaurant-operations/restaurants/accessible/kds');
    if (!response.ok) throw new Error('No se pudieron cargar los locales KDS: HTTP ' + response.status);
    return response.json();
  }

  async sendOrderCommand(orderId, path) {
    const response = await this.fetch('/api/orders/' + orderId + '/' + path, { method: 'POST' });
    if (!response.ok) throw new Error('El comando fue rechazado: HTTP ' + response.status);
  }

  fetch(path, options = {}) {
    const headers = new Headers(options.headers || {});
    if (this.accessToken) headers.set('Authorization', 'Bearer ' + this.accessToken);
    return fetch(this.baseUrl + path, { ...options, headers });
  }
}
