const separator = String.fromCharCode(30);

function send(socket, message) {
  socket.send(JSON.stringify(message) + separator);
}

export class KdsRealtimeClient {
  constructor(baseUrl, handlers) {
    this.baseUrl = baseUrl;
    this.handlers = handlers;
    this.socket = null;
    this.connection = null;
    this.version = 0;
    this.reconnectTimer = null;
  }

  connect(connection) {
    this.connection = connection;
    const version = ++this.version;
    if (this.reconnectTimer) window.clearTimeout(this.reconnectTimer);
    if (this.socket) this.socket.close();
    this.handlers.onStatus(false, 'Conectando…');

    const hubOrigin = new URL(this.baseUrl);
    const protocol = hubOrigin.protocol === 'https:' ? 'wss:' : 'ws:';
    const token = encodeURIComponent(connection.accessToken);
    const socket = new WebSocket(protocol + '//' + hubOrigin.host + '/hubs/kds?access_token=' + token);
    let handshakeComplete = false;
    this.socket = socket;

    socket.addEventListener('open', () => send(socket, { protocol: 'json', version: 1 }));
    socket.addEventListener('message', async event => {
      if (version !== this.version) return;
      for (const frame of event.data.split(separator).filter(Boolean)) {
        const message = JSON.parse(frame);
        if (!handshakeComplete && message.type === undefined) {
          handshakeComplete = true;
          send(socket, {
            type: 1,
            invocationId: 'join',
            target: 'JoinStation',
            arguments: [connection.restaurantId, connection.stationCode]
          });
          continue;
        }
        if (message.type === 3 && message.invocationId === 'join') {
          this.handlers.onStatus(true, 'Conectado a ' + connection.stationName);
          await this.handlers.onConnected();
        }
        if (message.type === 1 && message.target === 'OrderUpdated') {
          await this.handlers.onOrderUpdated(message.arguments[0]);
        }
      }
    });
    socket.addEventListener('close', () => {
      if (version !== this.version || this.connection !== connection) return;
      this.handlers.onStatus(false, 'Desconectado; reintentando…');
      this.reconnectTimer = window.setTimeout(() => {
        if (version === this.version && this.connection === connection) this.connect(connection);
      }, 2000);
    });
    socket.addEventListener('error', () => {
      if (version === this.version) this.handlers.onStatus(false, 'Error de conexión');
    });
  }

  disconnect() {
    this.connection = null;
    this.version++;
    if (this.reconnectTimer) window.clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
    if (this.socket) this.socket.close();
    this.socket = null;
  }
}
