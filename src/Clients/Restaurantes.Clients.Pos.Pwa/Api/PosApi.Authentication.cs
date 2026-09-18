using System.Net.Http.Json;
using Restaurantes.Clients.Pos.Pwa.Models;

namespace Restaurantes.Clients.Pos.Pwa.Api;

public sealed partial class PosApi
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
