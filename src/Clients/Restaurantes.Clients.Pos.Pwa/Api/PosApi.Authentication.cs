using System.Net.Http.Json;
using Restaurantes.Clients.Pos.Pwa.Models;

namespace Restaurantes.Clients.Pos.Pwa.Api;

public sealed partial class PosApi
{
    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "/api/identity/login",
            new { username, password }
        );
        return await ReadAsync<LoginResponse>(response);
    }
}
