using System.Net.Http.Json;
using Restaurantes.Clients.Commander.Pwa.Models;

namespace Restaurantes.Clients.Commander.Pwa.Api;

public sealed partial class CommanderApi
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
