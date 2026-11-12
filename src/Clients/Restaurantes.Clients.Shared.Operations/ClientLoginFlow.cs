using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Restaurantes.Clients.Shared.Operations;

public sealed class ClientLoginFlow(NavigationManager navigation, IJSRuntime js)
{
    public const string LoginRejectedMessage =
        "La cuenta no está autorizada o el proveedor rechazó el acceso.";

    public async Task<ClientLoginResolution<TSession>> ResolveAsync<TSession>(
        string sessionKey,
        Func<string, Task<TSession>> exchange,
        Func<TSession, DateTime> getExpiration
    )
        where TSession : class
    {
        string? code = FragmentValue("login_code");
        if (string.IsNullOrWhiteSpace(code))
        {
            code = null;
        }
        bool loginError = FragmentValue("login_error") is not null;
        if (code is not null || loginError)
        {
            await js.InvokeVoidAsync("history.replaceState", null, "", navigation.BaseUri);
        }

        if (code is not null)
        {
            TSession session = await exchange(code);
            await SaveSessionAsync(sessionKey, session);
            return new(session, false, false, true);
        }

        string? json = await js.InvokeAsync<string?>("sessionStorage.getItem", sessionKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new(null, loginError, false, false);
        }

        TSession? savedSession = JsonSerializer.Deserialize<TSession>(json);
        if (savedSession is null || getExpiration(savedSession) <= DateTime.UtcNow)
        {
            await ClearSessionAsync(sessionKey);
            return new(null, loginError, true, false);
        }

        return new(savedSession, loginError, false, false);
    }

    public Task SaveSessionAsync<TSession>(string sessionKey, TSession session) =>
        js.InvokeVoidAsync("sessionStorage.setItem", sessionKey, JsonSerializer.Serialize(session))
            .AsTask();

    public Task ClearSessionAsync(string sessionKey) =>
        js.InvokeVoidAsync("sessionStorage.removeItem", sessionKey).AsTask();

    public void StartLogin(string returnPath) =>
        navigation.NavigateTo(LoginUrl("auth/start", returnPath), forceLoad: true);

    public async Task LogoutAsync(string sessionKey, string returnPath)
    {
        await ClearSessionAsync(sessionKey);
        navigation.NavigateTo(LoginUrl("auth/logout", returnPath), forceLoad: true);
    }

    public static async Task<TResponse> ExchangeAsync<TResponse>(HttpClient http, string code)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "/api/identity/auth/exchange",
            new { code }
        );
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<TResponse>()
                ?? throw new InvalidOperationException("La API devolvió una respuesta vacía.");
        }

        string content = await response.Content.ReadAsStringAsync();
        string detail = content;
        try
        {
            using JsonDocument json = JsonDocument.Parse(content);
            if (
                json.RootElement.TryGetProperty("detail", out JsonElement value)
                || json.RootElement.TryGetProperty("title", out value)
            )
            {
                detail = value.GetString() ?? content;
            }
        }
        catch (JsonException) { }
        throw new InvalidOperationException($"API {(int)response.StatusCode}: {detail}");
    }

    private string? FragmentValue(string key)
    {
        string fragment = new Uri(navigation.Uri).Fragment.TrimStart('#');
        foreach (string part in fragment.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            if (pair[0] == key)
            {
                return Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : "");
            }
        }

        return null;
    }

    private static string LoginUrl(string endpoint, string returnPath) =>
        $"/api/identity/{endpoint}?returnPath={Uri.EscapeDataString(returnPath)}";
}

public sealed record ClientLoginResolution<TSession>(
    TSession? Session,
    bool LoginError,
    bool SessionExpired,
    bool ExchangeCompleted
)
    where TSession : class;
