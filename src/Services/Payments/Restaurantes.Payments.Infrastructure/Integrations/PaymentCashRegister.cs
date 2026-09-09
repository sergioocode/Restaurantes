using System.Net.Http.Json;
using Restaurantes.Payments.Application;

namespace Restaurantes.Payments.Infrastructure.Integrations;

public sealed class PaymentCashRegister(HttpClient httpClient) : IPaymentCashRegister
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
            throw new PaymentCashRegisterClosedException(restaurantId);
        }
    }

    private sealed record CashRegisterAvailability(bool IsOpen);
}
