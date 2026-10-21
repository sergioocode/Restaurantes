using Restaurantes.Clients.Commander.Pwa.Models;

namespace Restaurantes.Clients.Commander.Pwa.Api;

public sealed partial class CommanderApi
{
    public async Task<SaleResponse?> TrySaleAsync(Guid saleId)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Get, $"/api/sales/{saleId}");
        using HttpResponseMessage response = await http.SendAsync(request);
        return response.StatusCode == System.Net.HttpStatusCode.NotFound
            ? null
            : await ReadAsync<SaleResponse>(response);
    }
}
