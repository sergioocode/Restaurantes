const $ = id => document.getElementById(id),
    sep = String.fromCharCode(30),
    money = new Intl.NumberFormat('es-ES', {
        style: 'currency',
        currency: 'EUR'
    }),
    gatewayOrigin = location.origin,
    sessionKey = 'restaurantes.dashboard.login';
let timer, access, socket, realtimeGeneration = 0, deadLetterRefreshAt = 0;
const today = new Date();
$('date').value = [today.getFullYear(), String(today.getMonth() + 1).padStart(2, '0'), String(today.getDate()).padStart(2, '0')].join('-');

function showLogin(message = '') {
    $('authLoading').hidden = true;
    $('loginView').hidden = false;
    $('dashboardHeader').hidden = true;
    $('dashboardContent').hidden = true;
    $('loginError').textContent = message;
    document.body.classList.add('login-mode')
}

function showDashboard() {
    $('authLoading').hidden = true;
    $('loginView').hidden = true;
    $('dashboardHeader').hidden = false;
    $('dashboardContent').hidden = false;
    document.body.classList.remove('login-mode')
}

function startLogin(provider) {
    window.location.assign('/api/identity/auth/start/' + provider + '?returnPath=' + encodeURIComponent('/dashboard/'));
}
$('loginMicrosoft').onclick = () => startLogin('Microsoft');
$('loginGoogle').onclick = () => startLogin('Google');

async function loadProvider() {
    const response = await fetch(gatewayOrigin + '/api/identity/auth/provider');
    if (!response.ok) throw new Error('No se pudo consultar el proveedor.');
    const settings = await response.json();
    $('loginMicrosoft').disabled = settings.activeProvider !== 'Microsoft' || !settings.microsoftConfigured;
    $('loginGoogle').disabled = settings.activeProvider !== 'Google' || !settings.googleConfigured;
}

async function load() {
    try {
        const params = new URLSearchParams();
        if ($('date').value) params.set('date', $('date').value);
        if ($('restaurant').value) params.set('restaurantId', $('restaurant').value);
        params.set('orderLimit', $('orderLimit').value);
        const [d, h] = await Promise.all([authorizedFetch('/api/reporting/dashboard/daily?' + params).then(r => r.json()), authorizedFetch('/api/reporting/health').then(r => r.json()), loadDeadLetters()]);
        $('sales').textContent = money.format(d.totalSales);
        $('orders').textContent = d.completedSales;
        $('average').textContent = money.format(d.averageTicket);
        fill($('products'), d.topProducts, x => `<td>${escapeHtml(x.name)}</td><td>${x.quantity}</td><td>${money.format(x.sales)}</td>`);
        fill($('restaurants'), d.restaurantRanking, x => `<td>${short(x.restaurantId)}</td><td>${x.tickets}</td><td>${money.format(x.sales)}</td>`);
        fill($('channels'), d.channels, x => `<td>${escapeHtml(x.source)}</td><td>${x.tickets}</td><td>${money.format(x.sales)}</td>`);
        fill($('paymentMethods'), d.paymentMethods, x => `<td>${escapeHtml(paymentMethodLabel(x.method))}</td><td>${x.tickets}</td><td>${money.format(x.sales)}</td>`);
        fill($('health'), h, x => `<td>${escapeHtml(x.service)}</td><td class="${x.healthy ? 'ok' : 'bad'}">${x.healthy ? 'OK' : 'Error'} ${x.status || ''}</td>`);
        renderOperations(d.operationalOrders);
        $('state').textContent = 'Actualizado ' + new Date().toLocaleTimeString();
    } catch (e) {
        $('state').textContent = 'Error: ' + e.message
    }
}

async function loadDeadLetters() {
    const alert = $('deadLetterAlert');
    if (!(access?.allRestaurantsRoles || []).includes('Admin')) {
        alert.hidden = true;
        return
    }

    const now = Date.now();
    if (now < deadLetterRefreshAt) return;
    deadLetterRefreshAt = now + 30000;

    try {
        const queues = await authorizedFetch('/api/reporting/dead-letters').then(r => r.json()),
            pending = queues.filter(x => x.available && x.messageCount > 0),
            unavailable = queues.filter(x => !x.available),
            total = pending.reduce((sum, x) => sum + x.messageCount, 0),
            hasAlert = total > 0 || unavailable.length > 0;

        alert.hidden = false;
        alert.className = 'dead-letter-alert ' + (hasAlert ? 'bad' : 'ok');
        alert.textContent = total > 0 ? `⚠ DLQ ${total}` : unavailable.length > 0 ? '⚠ DLQ ?' : '✓ DLQ 0';
        alert.title = [
            ...pending.map(x => `${x.queue}: ${x.messageCount} pendiente${x.messageCount === 1 ? '' : 's'}`),
            ...unavailable.map(x => `${x.queue}: no disponible`)
        ].join('\n') || 'No hay mensajes muertos pendientes.'
    } catch (error) {
        alert.hidden = false;
        alert.className = 'dead-letter-alert bad';
        alert.textContent = '⚠ DLQ ?';
        alert.title = 'No se pudo consultar el estado de las colas de mensajes muertos.'
    }
}

function check(r) {
    if (!r.ok) throw new Error('HTTP ' + r.status);
    return r
}

function authorizedFetch(path, options = {}) {
    const headers = new Headers(options.headers || {});
    headers.set('Authorization', 'Bearer ' + access.accessToken);
    return fetch(gatewayOrigin + path, { ...options, headers }).then(check)
}

function fill(node, items, row) {
    node.innerHTML = items.map(x => `<tr>${row(x)}</tr>`).join('') || '<tr><td>Sin datos</td></tr>'
}

function short(id) {
    return id ? id.slice(0, 8) : '-'
}

function escapeHtml(v) {
    return String(v ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        '"': '&quot;',
        "'": '&#39;'
    }[c]))
}

function badge(value) {
    return `<span class="badge ${escapeHtml(value)}">${escapeHtml(value)}</span>`
}

function paymentMethodLabel(method) {
    return ({
        Card: 'Tarjeta',
        Cash: 'Efectivo',
        Online: 'Pago online',
        Check: 'Cheque',
        Cheque: 'Cheque',
        Unknown: 'Sin especificar'
    })[method] || method
}

function renderOperations(items) {
    const groups = items.reduce((all, item) => {
        if (!all[item.restaurantId]) all[item.restaurantId] = [];
        all[item.restaurantId].push(item);
    return all
}, { });
$('operations').innerHTML = Object.entries(groups).map(([local, orders]) => `<section><h3 class="local">Local ${short(local)}</h3><div class="orders">${orders.map(renderOrder).join('')}</div></section>`).join('') || '<p>Sin pedidos para este día.</p>'
  }

function renderOrder(o) {
    const stations = o.stations.map(s => badge(s.code + ': ' + s.status)).join('');
    const lines = o.lines.map(l => `<li>${l.quantity} × ${escapeHtml(l.productName)} <small>${escapeHtml(l.categoryName)} · ${escapeHtml(l.preparationStationCode)}</small></li>`).join('');
    const label = o.serviceMode === 'DineIn' ? o.tableLabel : o.serviceMode === 'Bar' ? (o.tableLabel || 'Barra') : ('Para llevar · ' + (o.customerName || 'Sin nombre'));
    return `<article class="order"><div class="order-head"><strong>${escapeHtml(label)}</strong><small>${short(o.orderId)}</small></div><div class="badges">${badge(o.serviceMode)}${badge(o.source)}${badge(o.orderStatus)}${badge(o.paymentStatus)}${o.paymentMethod ? badge(o.paymentMethod) : ''}</div><div class="badges">${stations}</div><ul>${lines}</ul><strong>${money.format(o.total)}</strong></article>`
}

function schedule() {
    clearInterval(timer);
    const ms = +$('interval').value;
    if (ms) timer = setInterval(load, ms)
}
$('refresh').onclick = load;
$('interval').onchange = schedule;
$('date').onchange = load;
$('restaurant').onchange = async () => { await load(); realtime() };
$('orderLimit').onchange = load;

$('logout').onclick = logout;

async function openDashboard(session) {
    const hasAllRestaurants = (session.allRestaurantsRoles || []).some(role => ['Admin', 'Gerente', 'Contabilidad', 'Marketing'].includes(role));
    const allowed = new Set((session.restaurants || []).filter(x => (x.permissions || []).includes('dashboard.read')).map(x => x.restaurantId.toLowerCase()));
    if (!hasAllRestaurants && allowed.size === 0) throw new Error('Este perfil no tiene acceso al Dashboard.');
    access = session;
    deadLetterRefreshAt = 0;
    showDashboard();

    let knownRestaurants = [];
    try {
        knownRestaurants = await authorizedFetch('/api/restaurant-operations/restaurants').then(r => r.json())
    } catch { }
    const visible = knownRestaurants.filter(x => hasAllRestaurants || allowed.has(x.id.toLowerCase()));
    const missing = (access.restaurants || []).filter(x => allowed.has(x.restaurantId.toLowerCase()) && !visible.some(r => r.id.toLowerCase() === x.restaurantId.toLowerCase())).map(x => ({ id: x.restaurantId, name: 'Local ' + short(x.restaurantId) }));
    const restaurants = [...visible, ...missing].sort((a, b) => a.name.localeCompare(b.name));
    $('restaurant').innerHTML = `<option value="">${hasAllRestaurants ? 'Todos los Locales' : 'Local asignado'}</option>` + restaurants.map(x => `<option value="${escapeHtml(x.id)}">${escapeHtml(x.name)} · ${escapeHtml(x.id)}</option>`).join('');
    $('currentUser').textContent = access.user.displayName;
    schedule();
    await load();
    realtime()
}

function logout() {
    realtimeGeneration++;
    if (socket) socket.close();
    socket = null;
    access = null;
    deadLetterRefreshAt = 0;
    clearInterval(timer);
    sessionStorage.removeItem(sessionKey);
    showLogin()
}

async function realtime() {
    const generation = ++realtimeGeneration;
    if (socket) socket.close();
    if (!access) return;
    try {
        const gateway = new URL(gatewayOrigin),
            protocol = gateway.protocol === 'https:' ? 'wss' : 'ws',
            neg = await authorizedFetch('/hubs/reporting/negotiate?negotiateVersion=1', {
                method: 'POST'
            }).then(check).then(r => r.json()),
            ws = new WebSocket(`${protocol}://${gateway.host}/hubs/reporting?id=${encodeURIComponent(neg.connectionToken)}&access_token=${encodeURIComponent(access.accessToken)}`);
        socket = ws;
        let joined = false;
        ws.onopen = () => ws.send(JSON.stringify({
            protocol: 'json',
            version: 1
        }) + sep);
        ws.onmessage = e => {
            for (const raw of e.data.split(sep)) {
                if (!raw) continue;
                const m = JSON.parse(raw);
                if (!joined && m.type === undefined) {
                    const restaurantIds = $('restaurant').value ? [$('restaurant').value] : Array.from($('restaurant').options).map(x => x.value).filter(Boolean);
                    ws.send(JSON.stringify({ type: 1, target: 'JoinRestaurants', arguments: [restaurantIds] }) + sep);
                    joined = true;
                    $('state').textContent = 'Conectado';
                }
                if (m.type === 1 && m.target === 'DashboardUpdated') setTimeout(load, 200)
            }
        };
        ws.onclose = () => { if (access && generation === realtimeGeneration) setTimeout(realtime, 2000) }
    } catch {
        if (access && generation === realtimeGeneration) setTimeout(realtime, 2000)
    }
}

(async function bootstrap() {
    try {
        await loadProvider();
        const query = new URLSearchParams(window.location.hash.slice(1));
        const code = query.get('login_code');
        if (code) {
            history.replaceState(null, '', window.location.pathname);
            const response = await fetch(gatewayOrigin + '/api/identity/auth/exchange', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ code })
            });
            if (!response.ok) throw new Error('La cuenta no está autorizada.');
            const session = await response.json();
            sessionStorage.setItem(sessionKey, JSON.stringify(session));
            await openDashboard(session);
            return;
        }
        if (query.has('login_error')) {
            history.replaceState(null, '', window.location.pathname);
            return showLogin('La cuenta no está autorizada o el proveedor rechazó el acceso.');
        }
        const saved = sessionStorage.getItem(sessionKey);
        if (!saved) return showLogin();
        const session = JSON.parse(saved);
        if (new Date(session.expiresAtUtc) <= new Date()) return logout();
        await openDashboard(session);
    } catch (error) {
        if (access) {
            showDashboard();
            $('state').textContent = error.message;
        } else {
            sessionStorage.removeItem(sessionKey);
            showLogin(error.message);
        }
    }
})();

if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.register('./service-worker.js', { updateViaCache: 'none' });
    });
}

