using System.Net.Http.Json;
using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Api;

public sealed partial class BackofficeApi
{
    public Task<List<ZoneResponse>> ZonesAsync(Guid restaurantId)
    {
        return GetAsync<List<ZoneResponse>>($"/api/dining/restaurants/{restaurantId}/zones");
    }

    public async Task<ZoneResponse> SaveZoneAsync(Guid restaurantId, ZoneResponse zone)
    {
        string path = $"/api/dining/restaurants/{restaurantId}/zones";
        using HttpRequestMessage request = Authorized(
            zone.Id == Guid.Empty ? HttpMethod.Post : HttpMethod.Put,
            zone.Id == Guid.Empty ? path : $"{path}/{zone.Id}"
        );
        request.Content = JsonContent.Create(new { zone.Name, zone.SortOrder });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<ZoneResponse>(response);
    }

    public async Task DeleteZoneAsync(Guid restaurantId, Guid zoneId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Delete,
            $"/api/dining/restaurants/{restaurantId}/zones/{zoneId}"
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            await ReadAsync<object>(response);
        }
    }

    public Task<List<TableResponse>> TablesAsync(Guid restaurantId)
    {
        return GetAsync<List<TableResponse>>($"/api/dining/restaurants/{restaurantId}/tables");
    }

    public Task<DiningPolicyResponse> DiningPolicyAsync(Guid restaurantId)
    {
        return GetAsync<DiningPolicyResponse>($"/api/dining/restaurants/{restaurantId}/policy");
    }

    public async Task<TableResponse> CreateTableAsync(Guid restaurantId, TableDraft draft)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/dining/restaurants/{restaurantId}/tables"
        );
        request.Content = JsonContent.Create(draft);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<TableResponse>(response);
    }

    public async Task<TableResponse> UpdateTableAsync(TableResponse item)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/dining/tables/{item.Id}"
        );
        request.Content = JsonContent.Create(
            new
            {
                item.Label,
                item.ZoneId,
                item.RequestGuestCount,
                item.IsActive,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<TableResponse>(response);
    }

    public async Task DeleteTableAsync(Guid tableId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Delete,
            $"/api/dining/tables/{tableId}"
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            await ReadAsync<object>(response);
        }
    }

    public async Task<TableQrResponse> RotateQrAsync(Guid tableId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/dining/tables/{tableId}/qr/rotate"
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<TableQrResponse>(response);
    }

    public async Task<DiningPolicyResponse> UpdateDiningPolicyAsync(
        Guid restaurantId,
        DiningPolicyResponse policy
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Put,
            $"/api/dining/restaurants/{restaurantId}/policy"
        );
        request.Content = JsonContent.Create(
            new
            {
                policy.QrRequiresImmediatePayment,
                policy.RequireTrustedNetworkForQr,
                policy.TakeawayRequiresPrepayment,
                policy.AllowCheckoutBeforeKitchenCompletion,
                policy.QrAllowedNetworks,
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<DiningPolicyResponse>(response);
    }
}
