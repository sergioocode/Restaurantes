using System.Net.Http.Json;
using Restaurantes.Payments.Application;

namespace Restaurantes.Payments.Infrastructure.Integrations;

public sealed class PaymentCashRegister(HttpClient httpClient) : IPaymentCashRegister
{
    public async Task<CashRegisterSessionDetails> GetOpenSessionAsync(
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
        return !availability.IsOpen ? throw new PaymentCashRegisterClosedException(restaurantId)
            : availability.SessionId is not Guid sessionId || sessionId == Guid.Empty
                ? throw new InvalidOperationException(
                    "CashRegister did not return an open session id."
                )
            : new CashRegisterSessionDetails(sessionId, availability.BusinessDate);
    }

    private sealed record CashRegisterAvailability(
        bool IsOpen,
        Guid? SessionId,
        DateOnly BusinessDate
    );
}
