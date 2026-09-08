using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Restaurantes.Clients.Commander.Pwa;

public sealed class CommanderApi(HttpClient http)
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

    public string? AccessToken { get; set; }

    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "/api/identity/login",
            new { username, password }
        );
        return await ReadAsync<LoginResponse>(response);
    }

    public Task<List<TableResponse>> TablesAsync(Guid restaurantId)
    {
        return GetAsync<List<TableResponse>>($"/api/dining/restaurants/{restaurantId}/tables");
    }

    public Task<List<OrderResponse>> OrdersAsync(Guid restaurantId)
    {
        return GetAsync<List<OrderResponse>>($"/api/orders?restaurantId={restaurantId}");
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

    public Task<List<MenuItemResponse>> MenuAsync(Guid restaurantId)
    {
        return GetAsync<List<MenuItemResponse>>($"/api/catalog/restaurants/{restaurantId}/menu");
    }

    public Task<SessionBillResponse> BillAsync(Guid sessionId)
    {
        return GetAsync<SessionBillResponse>($"/api/dining/sessions/{sessionId}/bill");
    }

    public Task<OrderDetailResponse> OrderAsync(Guid orderId)
    {
        return GetAsync<OrderDetailResponse>($"/api/orders/{orderId}");
    }

    public async Task<OrderDetailResponse> CancelOrderLineAsync(
        Guid orderId,
        Guid lineId,
        string reason
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/orders/{orderId}/lines/{lineId}/cancel"
        );
        request.Content = JsonContent.Create(new { reason });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<OrderDetailResponse>(response);
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

    public async Task<DiningSessionResponse> ReleaseSessionAsync(Guid sessionId)
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/dining/sessions/{sessionId}/close"
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<DiningSessionResponse>(response);
    }

    public async Task<OrderResponse> CreateAndSubmitAsync(
        Guid restaurantId,
        TableResponse table,
        Guid sessionId,
        IReadOnlyDictionary<Guid, int> quantities,
        IReadOnlyDictionary<Guid, string> notes
    )
    {
        object body = new
        {
            restaurantId,
            tableId = table.Id,
            diningSessionId = sessionId,
            tableLabel = table.Label,
            serviceMode = "DineIn",
            source = "WaiterMobile",
            paymentTiming = "OnAccount",
            lines = quantities
                .Where(x => x.Value > 0)
                .Select(x => new
                {
                    productId = x.Key,
                    quantity = x.Value,
                    notes = notes.GetValueOrDefault(x.Key) ?? string.Empty,
                }),
        };
        using HttpRequestMessage create = Authorized(HttpMethod.Post, "/api/orders");
        create.Content = JsonContent.Create(body);
        using HttpResponseMessage createResponse = await http.SendAsync(create);
        OrderResponse order = await ReadAsync<OrderResponse>(createResponse);

        using HttpRequestMessage submit = Authorized(
            HttpMethod.Post,
            $"/api/orders/{order.Id}/submit"
        );
        using HttpResponseMessage submitResponse = await http.SendAsync(submit);
        return await ReadAsync<OrderResponse>(submitResponse);
    }

    private async Task<T> GetAsync<T>(string path)
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Get, path);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<T>(response);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        HttpRequestMessage request = new(method, path);
        if (!string.IsNullOrWhiteSpace(AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>()
                ?? throw new InvalidOperationException("The API returned an empty response.");
        }

        string content = await response.Content.ReadAsStringAsync();
        string detail = content;
        try
        {
            using JsonDocument json = JsonDocument.Parse(content);
            if (json.RootElement.TryGetProperty("detail", out JsonElement value))
            {
                detail = value.GetString() ?? content;
            }
        }
        catch (JsonException) { }
        throw new InvalidOperationException($"API {(int)response.StatusCode}: {detail}");
    }
}

public sealed record LoginResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    LoginUser User,
    List<RestaurantAccess> Restaurants
);

public sealed record LoginUser(Guid Id, string Username, string DisplayName);

public sealed record RestaurantAccess(Guid RestaurantId, string Role, List<string> Permissions);

public sealed record TableResponse(
    Guid Id,
    Guid RestaurantId,
    string Code,
    string Label,
    bool IsActive,
    string Status,
    Guid? ActiveSessionId,
    Guid ZoneId,
    string ZoneName,
    int ZoneSortOrder
);

public sealed record DiningSessionResponse(
    Guid Id,
    Guid RestaurantId,
    Guid TableId,
    string Status,
    bool RequestGuestCount = false,
    int? GuestCount = null
);

public sealed record MenuItemResponse(
    Guid RestaurantId,
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid CategoryId,
    string CategoryCode,
    string CategoryName,
    decimal Price,
    bool IsAvailable,
    string PreparationStationCode,
    string PreparationStationName
);

public sealed record OrderResponse(
    Guid Id,
    Guid RestaurantId,
    Guid? TableId,
    Guid? DiningSessionId,
    string CustomerName,
    string ServiceMode,
    string PaymentTiming,
    string Status,
    decimal Total,
    DateTime CreatedAtUtc
);

public sealed record OrderDetailResponse(
    Guid Id,
    Guid RestaurantId,
    string Status,
    decimal Total,
    List<OrderLineDetailResponse> Lines,
    List<OrderStationDetailResponse> Stations
);

public sealed record OrderLineDetailResponse(
    Guid Id,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    string PreparationStationCode,
    string PreparationStationName,
    string Status,
    DateTime? CancelledAtUtc,
    string CancellationReason
);

public sealed record OrderStationDetailResponse(string Code, string Name, string Status);

public sealed record SessionBillResponse(
    Guid Id,
    Guid RestaurantId,
    Guid TableId,
    string Status,
    string PaymentMethod,
    DateTime? PaidAtUtc,
    decimal Total,
    decimal Outstanding,
    bool CanCheckout,
    List<SessionBillOrderResponse> Orders
);

public sealed record SessionBillOrderResponse(
    Guid OrderId,
    string OrderStatus,
    string PaymentStatus,
    decimal Amount,
    DateTime AddedAtUtc
);
