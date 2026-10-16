using System.Net.Http.Json;
using Restaurantes.Dining.Application;

namespace Restaurantes.Dining.Infrastructure.Integrations;

public sealed class DiningCashRegister(HttpClient httpClient) : IDiningCashRegister
{
    public async Task EnsureOpenAsync(Guid restaurantId, CancellationToken ct)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"api/cash-register/restaurants/{restaurantId}/is-open",
            ct
        );
        response.EnsureSuccessStatusCode();
        CashRegisterAvailability availability =
            await response.Content.ReadFromJsonAsync<CashRegisterAvailability>(
                cancellationToken: ct
            )
            ?? throw new InvalidOperationException(
                "CashRegister returned an empty operational status."
            );
        if (!availability.IsOpen)
        {
            throw new DiningCashRegisterClosedException(
                $"La caja del local '{restaurantId}' está cerrada. Ábrela antes de iniciar una mesa."
            );
        }
    }

    private sealed record CashRegisterAvailability(bool IsOpen);
}
