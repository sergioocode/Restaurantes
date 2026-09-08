using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Restaurantes.Clients.CustomerQr.Pwa;

public sealed class CustomerQrApi(HttpClient http)
{
    public async Task<QrDiningSessionResponse> SetGuestCountAsync(
        Guid sessionId,
        int guestCount,
        string? customerAccessToken = null
    )
    {
        using HttpRequestMessage request = WithCustomerToken(
            HttpMethod.Put,
            $"/api/dining/sessions/{sessionId}/guests",
            customerAccessToken
        );
        request.Content = JsonContent.Create(new { guestCount });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<QrDiningSessionResponse>(response);
    }

    public async Task<QrSessionEnvelope> PreviewAsync(string qrCode, string? customerAccessToken)
    {
        using HttpRequestMessage request = WithCustomerToken(
            HttpMethod.Get,
            $"/api/dining/qr/{Uri.EscapeDataString(qrCode)}",
            customerAccessToken
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<QrSessionEnvelope>(response);
    }

    public async Task<QrSessionEnvelope> OpenSessionAsync(
        string qrCode,
        string? customerAccessToken = null,
        int? guestCount = null
    )
    {
        using HttpRequestMessage request = WithCustomerToken(
            HttpMethod.Post,
            $"/api/dining/qr/{Uri.EscapeDataString(qrCode)}/sessions"
                + (guestCount.HasValue ? $"?guestCount={guestCount.Value}" : string.Empty),
            customerAccessToken
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<QrSessionEnvelope>(response);
    }

    public async Task<List<MenuItemResponse>> MenuAsync(Guid restaurantId)
    {
        using HttpResponseMessage response = await http.GetAsync(
            $"/api/catalog/restaurants/{restaurantId}/menu"
        );
        return await ReadAsync<List<MenuItemResponse>>(response);
    }

    public async Task<RestaurantResponse> RestaurantAsync(Guid restaurantId)
    {
        using HttpResponseMessage response = await http.GetAsync(
            $"/api/restaurant-operations/restaurants/{restaurantId}"
        );
        return await ReadAsync<RestaurantResponse>(response);
    }

    public async Task<OrderResponse> CreateOrderAsync(
        QrSessionEnvelope context,
        IReadOnlyDictionary<Guid, int> quantities,
        IReadOnlyDictionary<Guid, string> notes
    )
    {
        if (context.Session is null)
        {
            throw new InvalidOperationException("Confirma el pedido para abrir una sesión.");
        }

        object body = new
        {
            restaurantId = context.Table.RestaurantId,
            tableId = context.Table.Id,
            diningSessionId = context.Session.Id,
            tableLabel = context.Table.Label,
            source = "CustomerQr",
            paymentTiming = context.QrRequiresImmediatePayment ? "Immediate" : "OnAccount",
            lines = quantities
                .Where(x => x.Value > 0)
                .Select(x => new
                {
                    productId = x.Key,
                    quantity = x.Value,
                    notes = notes.GetValueOrDefault(x.Key) ?? string.Empty,
                }),
        };
        using HttpRequestMessage request = WithCustomerToken(
            HttpMethod.Post,
            "/api/orders",
            context.CustomerAccessToken
        );
        request.Content = JsonContent.Create(body);
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<OrderResponse>(response);
    }

    public async Task<PaymentResponse> CaptureOnlineAsync(
        Guid orderId,
        Guid sessionId,
        Guid idempotencyKey,
        string customerAccessToken
    )
    {
        using HttpRequestMessage request = WithCustomerToken(
            HttpMethod.Post,
            $"/api/payments/orders/{orderId}/capture-customer-qr",
            customerAccessToken
        );
        request.Content = JsonContent.Create(
            new
            {
                idempotencyKey,
                diningSessionId = sessionId,
                externalReference = $"QR-PWA-{orderId:N}",
            }
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<PaymentResponse>(response);
    }

    public async Task<OrderResponse> SubmitAsync(Guid orderId, string customerAccessToken)
    {
        using HttpRequestMessage request = WithCustomerToken(
            HttpMethod.Post,
            $"/api/orders/{orderId}/submit",
            customerAccessToken
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<OrderResponse>(response);
    }

    public async Task<OrderResponse> OrderAsync(Guid orderId, string customerAccessToken)
    {
        using HttpRequestMessage request = WithCustomerToken(
            HttpMethod.Get,
            $"/api/orders/{orderId}",
            customerAccessToken
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<OrderResponse>(response);
    }

    private static HttpRequestMessage WithCustomerToken(
        HttpMethod method,
        string path,
        string? token
    )
    {
        HttpRequestMessage request = new(method, path);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Add("X-Customer-Session-Token", token);
        }
        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>()
                ?? throw new CustomerApiException(
                    response.StatusCode,
                    "The API returned an empty response."
                );
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
            else if (json.RootElement.TryGetProperty("title", out value))
            {
                detail = value.GetString() ?? content;
            }
        }
        catch (JsonException) { }

        throw new CustomerApiException(response.StatusCode, detail);
    }
}

public sealed class CustomerApiException(HttpStatusCode statusCode, string message)
    : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed record QrSessionEnvelope(
    QrTableResponse Table,
    QrDiningSessionResponse? Session,
    string CustomerAccessToken,
    bool QrRequiresImmediatePayment
);

public sealed record QrTableResponse(
    Guid Id,
    Guid RestaurantId,
    string Code,
    string Label,
    bool RequestGuestCount = false
);

public sealed record RestaurantResponse(
    Guid Id,
    string Code,
    string Name,
    string Address,
    bool IsActive,
    int Version,
    DateTime UpdatedAtUtc
);

public sealed record QrDiningSessionResponse(
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
    string Status,
    decimal Total,
    int Version,
    DateTime CreatedAtUtc
);

public sealed record PaymentResponse(
    Guid PaymentId,
    Guid OrderId,
    Guid RestaurantId,
    decimal Amount,
    string Method,
    string Status,
    DateTime? CapturedAtUtc,
    DateTime? RefundedAtUtc
);
