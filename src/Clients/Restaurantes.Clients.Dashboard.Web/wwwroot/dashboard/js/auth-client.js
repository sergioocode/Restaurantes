// Cliente de inicio de sesión compartido por KDS y Dashboard.
// Mantén este archivo idéntico en ambos clientes: una prueba lo comprueba.
(() => {
  const loginRejectedMessage = 'La cuenta no está autorizada o el proveedor rechazó el acceso.';
  const exchangeRejectedMessage = 'La cuenta no está autorizada.';

  function createAccessDeniedError(message) {
    const error = new Error(message);
    error.reauthenticate = true;
    return error;
  }

  function createAuthClient({ appPath, sessionKey, baseUrl }) {
    const returnPath = encodeURIComponent(appPath);

    function startLogin(reauthenticate = false) {
      const query = reauthenticate ? '&reauthenticate=true' : '';
      window.location.assign('/api/identity/auth/start?returnPath=' + returnPath + query);
    }

    function bindLoginButton(button) {
      button.addEventListener('click', () =>
        startLogin(button.dataset.reauthenticate === 'true'));
    }

    function prepareLoginButton(button, reauthenticate = false) {
      button.textContent = reauthenticate ? 'Cambiar usuario' : 'Iniciar sesión';
      if (reauthenticate) button.dataset.reauthenticate = 'true';
      else delete button.dataset.reauthenticate;
    }

    function saveSession(session) {
      sessionStorage.setItem(sessionKey, JSON.stringify(session));
    }

    function clearSession() {
      sessionStorage.removeItem(sessionKey);
    }

    function logout() {
      clearSession();
      window.location.assign('/api/identity/auth/logout?returnPath=' + returnPath);
    }

    async function exchangeCode(code) {
      const response = await fetch(baseUrl + '/api/identity/auth/exchange', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code })
      });
      if (!response.ok) throw createAccessDeniedError(exchangeRejectedMessage);
      return response.json();
    }

    function readStoredSession() {
      const stored = sessionStorage.getItem(sessionKey);
      if (!stored) return null;
      try {
        const session = JSON.parse(stored);
        if (new Date(session.expiresAtUtc) > new Date()) return session;
      } catch {
        // Una sesión ilegible se descarta igual que una caducada.
      }
      clearSession();
      return null;
    }

    // Devuelve { session } si hay sesión activa o { message } si el proveedor rechazó el acceso.
    async function resolveSession() {
      const query = new URLSearchParams(window.location.hash.slice(1));
      const code = query.get('login_code');
      if (code) {
        history.replaceState(null, '', window.location.pathname);
        const session = await exchangeCode(code);
        saveSession(session);
        return { session };
      }
      if (query.has('login_error')) {
        history.replaceState(null, '', window.location.pathname);
        return { message: loginRejectedMessage };
      }
      const session = readStoredSession();
      return session ? { session } : {};
    }

    return {
      bindLoginButton,
      prepareLoginButton,
      startLogin,
      logout,
      clearSession,
      resolveSession
    };
  }

  globalThis.RestaurantesAuth = { createAuthClient, createAccessDeniedError };
})();
