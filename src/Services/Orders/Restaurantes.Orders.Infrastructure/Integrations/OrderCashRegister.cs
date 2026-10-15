using System.Net.Http.Json;
using Restaurantes.Orders.Application;

namespace Restaurantes.Orders.Infrastructure.Integrations;

public sealed class OrderCashRegister(HttpClient httpClient) : IOrderCashRegister
{
    public async Task<DateOnly> GetBusinessDateAsync(
        Guid restaurantId,
        CancellationToken cancellationToken
    )
    {
        return (await GetAvailabilityAsync(restaurantId, cancellationToken)).BusinessDate;
    }

    public async Task<OrderCashRegisterSession> EnsureOpenAsync(
        Guid restaurantId,
        CancellationToken cancellationToken
    )
    {
        CashRegisterAvailability availability = await GetAvailabilityAsync(
            restaurantId,
            cancellationToken
        );
        return !availability.IsOpen || availability.SessionId is null
            ? throw new OrderCashRegisterClosedException(restaurantId)
            : new OrderCashRegisterSession(availability.SessionId.Value, availability.BusinessDate);
    }

    private async Task<CashRegisterAvailability> GetAvailabilityAsync(
        Guid restaurantId,
        CancellationToken cancellationToken
    )
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
        return availability;
    }

    private sealed record CashRegisterAvailability(
        bool IsOpen,
        Guid? SessionId,
        DateOnly BusinessDate
    );
}
