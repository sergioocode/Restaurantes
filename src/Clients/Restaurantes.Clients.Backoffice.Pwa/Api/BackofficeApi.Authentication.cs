using System.Net.Http.Json;
using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Api;

public sealed partial class BackofficeApi
{
    public Task<ProviderSettingsResponse> ProviderAsync()
    {
        return GetAsync<ProviderSettingsResponse>("/api/identity/auth/provider");
    }

    public async Task<LoginResponse> ExchangeAsync(string code)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "/api/identity/auth/exchange",
            new { code }
        );
        return await ReadAsync<LoginResponse>(response);
    }

    public async Task ChangeProviderAsync(string activeProvider)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            "/api/identity/auth/provider"
        );
        request.Content = JsonContent.Create(new { activeProvider });
        using HttpResponseMessage response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }
}
