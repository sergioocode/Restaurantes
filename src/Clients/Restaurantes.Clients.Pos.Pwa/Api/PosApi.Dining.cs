using System.Net.Http.Json;
using Restaurantes.Clients.Pos.Pwa.Models;

namespace Restaurantes.Clients.Pos.Pwa.Api;

public sealed partial class PosApi
{
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

    public Task<DiningPolicyResponse> PolicyAsync(Guid restaurantId)
    {
        return GetAsync<DiningPolicyResponse>($"/api/dining/restaurants/{restaurantId}/policy");
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

    public Task<DiningSessionResponse> ActiveSessionAsync(Guid tableId)
    {
        return GetAsync<DiningSessionResponse>($"/api/dining/tables/{tableId}/active-session");
    }

    public Task<SessionBillResponse> BillAsync(Guid sessionId)
    {
        return GetAsync<SessionBillResponse>($"/api/dining/sessions/{sessionId}/bill");
    }

    public async Task<DiningSessionResponse> CancelSessionAsync(Guid sessionId, string reason)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/dining/sessions/{sessionId}/cancel"
        );
        request.Content = JsonContent.Create(new { reason });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<DiningSessionResponse>(response);
    }

    public async Task<SessionBillResponse> CheckoutAsync(
        Guid sessionId,
        Guid idempotencyKey,
        string method,
        string externalReference
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
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<SessionBillResponse>(response);
    }

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
