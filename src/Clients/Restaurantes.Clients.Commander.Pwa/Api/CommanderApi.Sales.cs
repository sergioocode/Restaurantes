using Restaurantes.Clients.Commander.Pwa.Models;

namespace Restaurantes.Clients.Commander.Pwa.Api;

public sealed partial class CommanderApi
{
    public Task<SaleResponse> SaleAsync(Guid saleId)
    {
        return GetAsync<SaleResponse>($"/api/sales/{saleId}");
    }
}
