using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Api;

public sealed partial class BackofficeApi
{
    public Task<List<ManagementOrderResponse>> ManagementOrdersAsync(
        Guid restaurantId,
        DateOnly date,
        int offset
    )
    {
        return GetAsync<List<ManagementOrderResponse>>(
            $"/api/orders/management?restaurantId={restaurantId}&date={date:yyyy-MM-dd}&offset={offset}"
        );
    }

    public Task<List<SaleResponse>> SalesAsync(Guid restaurantId, DateOnly date, int offset)
    {
        return GetAsync<List<SaleResponse>>(
            $"/api/sales?restaurantId={restaurantId}&date={date:yyyy-MM-dd}&offset={offset}"
        );
    }
}
