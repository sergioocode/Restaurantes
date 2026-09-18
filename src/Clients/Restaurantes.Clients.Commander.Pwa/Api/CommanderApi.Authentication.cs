using System.Net.Http.Json;
using Restaurantes.Clients.Commander.Pwa.Models;

namespace Restaurantes.Clients.Commander.Pwa.Api;

public sealed partial class CommanderApi
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
}
