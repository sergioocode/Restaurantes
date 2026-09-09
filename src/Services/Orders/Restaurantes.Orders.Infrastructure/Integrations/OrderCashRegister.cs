using System.Net.Http.Json;
using Restaurantes.Orders.Application;

namespace Restaurantes.Orders.Infrastructure.Integrations;

public sealed class OrderCashRegister(HttpClient httpClient) : IOrderCashRegister
{
    public async Task EnsureOpenAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"api/cash-register/restaurants/{restaurantId}/is-open",
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        CashRegisterAvailability availability =
            await response.Content.ReadFromJsonAsync<CashRegisterAvailability>(
                cancellationToken: cancellationToken
            )
            ?? throw new InvalidOperationException(
                "CashRegister returned an empty operational status."
            );
        if (!availability.IsOpen)
        {
            throw new OrderCashRegisterClosedException(restaurantId);
        }
    }

    private sealed record CashRegisterAvailability(bool IsOpen);
}
