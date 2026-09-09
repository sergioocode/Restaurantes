using System.Net.Http.Json;
using Restaurantes.Clients.Pos.Pwa.Models;

namespace Restaurantes.Clients.Pos.Pwa.Api;

public sealed partial class PosApi
{
    public async Task<CashRegisterResponse?> CurrentCashRegisterAsync(Guid restaurantId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Get,
            $"/api/cash-register/restaurants/{restaurantId}/current"
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return response.StatusCode == System.Net.HttpStatusCode.NoContent
            ? null
            : await ReadAsync<CashRegisterResponse>(response);
    }

    public async Task<CashRegisterResponse> OpenCashRegisterAsync(
        Guid restaurantId,
        decimal openingFloat
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/cash-register/restaurants/{restaurantId}/open"
        );
        request.Content = JsonContent.Create(new { openingFloat });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<CashRegisterResponse>(response);
    }

    public async Task<CashRegisterResponse> CloseCashRegisterAsync(
        Guid restaurantId,
        Guid sessionId,
        Dictionary<string, decimal> reconciledByMethod
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/cash-register/restaurants/{restaurantId}/sessions/{sessionId}/close"
        );
        request.Content = JsonContent.Create(new { reconciledByMethod });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<CashRegisterResponse>(response);
    }
}
