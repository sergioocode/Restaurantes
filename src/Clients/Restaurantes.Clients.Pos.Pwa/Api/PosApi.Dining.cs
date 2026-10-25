using System.Net.Http.Json;
using Restaurantes.Clients.Pos.Pwa.Models;

namespace Restaurantes.Clients.Pos.Pwa.Api;

public sealed partial class PosApi
{
    public async Task<SessionBillResponse> SetDiscountAsync(Guid sessionId, decimal percentage)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/dining/sessions/{sessionId}/discount"
        );
        request.Content = JsonContent.Create(new { percentage });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<SessionBillResponse>(response);
    }

    public async Task<DiningSessionResponse> SetGuestCountAsync(Guid sessionId, int guestCount)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/dining/sessions/{sessionId}/guests"
        );
        request.Content = JsonContent.Create(new { guestCount });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<DiningSessionResponse>(response);
    }

    public Task<List<TableResponse>> TablesAsync(Guid restaurantId)
    {
        return GetAsync<List<TableResponse>>($"/api/dining/restaurants/{restaurantId}/tables");
    }

    public async Task<DiningSessionResponse> OpenSessionAsync(Guid tableId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/dining/tables/{tableId}/sessions"
        );
        request.Content = JsonContent.Create(new { source = "Pos" });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<DiningSessionResponse>(response);
    }

    public async Task<DiningSessionResponse> OpenTakeawaySessionAsync(
        Guid restaurantId,
        string customerName
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/dining/restaurants/{restaurantId}/takeaway-sessions"
        );
        request.Content = JsonContent.Create(new { customerName });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<DiningSessionResponse>(response);
    }

    public Task<DiningSessionResponse> ActiveSessionAsync(Guid tableId)
    {
        return GetAsync<DiningSessionResponse>($"/api/dining/tables/{tableId}/active-session");
    }

    public Task<List<DiningSessionResponse>> TakeawaySessionsAsync(Guid restaurantId)
    {
        return GetAsync<List<DiningSessionResponse>>(
            $"/api/dining/restaurants/{restaurantId}/takeaway-sessions"
        );
    }

    public Task<DiningSessionResponse> SessionAsync(Guid sessionId)
    {
        return GetAsync<DiningSessionResponse>($"/api/dining/sessions/{sessionId}");
    }

    public Task<SessionBillResponse> BillAsync(Guid sessionId)
    {
        return GetAsync<SessionBillResponse>($"/api/dining/sessions/{sessionId}/bill");
    }

    public async Task<SaleResponse?> TrySaleAsync(Guid saleId)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Get, $"/api/sales/{saleId}");
        using HttpResponseMessage response = await http.SendAsync(request);
        return response.StatusCode == System.Net.HttpStatusCode.NotFound
            ? null
            : await ReadAsync<SaleResponse>(response);
    }

    public async Task<DiningSessionResponse> CancelSessionAsync(Guid sessionId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/dining/sessions/{sessionId}/cancel"
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<DiningSessionResponse>(response);
    }

    public async Task<SessionBillResponse> CheckoutAsync(
        Guid sessionId,
        Guid idempotencyKey,
        string method,
        string externalReference,
        decimal tipAmount,
        decimal amountReceived,
        IReadOnlyList<CheckoutOrderAllocation> allocations
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/dining/sessions/{sessionId}/checkout"
        );
        request.Content = JsonContent.Create(
            new
            {
                idempotencyKey,
                method,
                externalReference,
                tipAmount,
                amountReceived,
                allocations,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<SessionBillResponse>(response);
    }

    public sealed record CheckoutOrderAllocation(Guid OrderId, decimal Amount);

    public async Task<DiningSessionResponse> ReleaseSessionAsync(Guid sessionId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/dining/sessions/{sessionId}/close"
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<DiningSessionResponse>(response);
    }
}
