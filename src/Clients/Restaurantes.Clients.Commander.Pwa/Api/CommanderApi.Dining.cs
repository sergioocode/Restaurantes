using System.Net.Http.Json;
using Restaurantes.Clients.Commander.Pwa.Models;

namespace Restaurantes.Clients.Commander.Pwa.Api;

public sealed partial class CommanderApi
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

    public async Task<DiningSessionResponse> OpenSessionAsync(Guid tableId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/dining/tables/{tableId}/sessions"
        );
        request.Content = JsonContent.Create(new { source = "WaiterMobile" });
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
}
