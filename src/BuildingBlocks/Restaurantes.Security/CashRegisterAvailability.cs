using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Restaurantes.Security;

public sealed record CashRegisterAvailability(
    Guid RestaurantId,
    bool IsOpen,
    DateOnly BusinessDate,
    Guid? SessionId
);

public sealed class CashRegisterClosedException(Guid restaurantId)
    : InvalidOperationException(
        $"La caja del local '{restaurantId}' está cerrada. Ábrela antes de iniciar una mesa o tomar pedidos."
    );

public sealed class CashRegisterAvailabilityClient(HttpClient http)
{
    public async Task<CashRegisterAvailability> GetAsync(Guid restaurantId, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync(
            $"api/cash-register/restaurants/{restaurantId}/is-open",
            ct
        );
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CashRegisterAvailability>(
                cancellationToken: ct
            )
            ?? throw new InvalidOperationException(
                "CashRegister returned an empty operational status."
            );
    }

    public async Task EnsureOpenAsync(Guid restaurantId, CancellationToken ct)
    {
        if (!(await GetAsync(restaurantId, ct)).IsOpen)
        {
            throw new CashRegisterClosedException(restaurantId);
        }
    }
}

public static class CashRegisterAvailabilityDependencyInjection
{
    public static IServiceCollection AddCashRegisterAvailability(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        Uri baseAddress = new(configuration["CashRegister:BaseAddress"] ?? "http://localhost:5105");
        services.AddHttpClient<CashRegisterAvailabilityClient>(client =>
            client.BaseAddress = baseAddress
        );
        return services;
    }
}
