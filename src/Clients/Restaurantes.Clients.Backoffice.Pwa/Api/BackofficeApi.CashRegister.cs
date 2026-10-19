using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Api;

public sealed partial class BackofficeApi
{
    public Task<List<CashRegisterReportResponse>> CashRegistersByDayAsync(
        Guid restaurantId,
        DateOnly date
    )
    {
        return GetAsync<List<CashRegisterReportResponse>>(
            $"/api/cash-register/restaurants/{restaurantId}/history?date={date:yyyy-MM-dd}"
        );
    }
}
