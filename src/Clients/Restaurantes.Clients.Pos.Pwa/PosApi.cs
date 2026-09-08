using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Restaurantes.Clients.Pos.Pwa;

public sealed class PosApi(HttpClient http)
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

    public Task<DiningPolicyResponse> PolicyAsync(Guid restaurantId)
    {
        return GetAsync<DiningPolicyResponse>($"/api/dining/restaurants/{restaurantId}/policy");
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
        request.Content = JsonContent.Create(new { source = "Pos" });
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

    public async Task<OrderResponse> CreateAndSubmitAsync(
        Guid restaurantId,
        TableResponse table,
        Guid sessionId,
        string serviceMode,
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
            serviceMode,
            source = "Pos",
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

    public async Task<OrderResponse> CreatePayAndSubmitQuickSaleAsync(
        Guid restaurantId,
        string serviceMode,
        string customerName,
        string paymentMethod,
        Guid? cashRegisterSessionId,
        IReadOnlyDictionary<Guid, int> quantities,
        IReadOnlyDictionary<Guid, string> notes
    )
    {
        object body = new
        {
            restaurantId,
            tableId = (Guid?)null,
            diningSessionId = (Guid?)null,
            tableLabel = string.Empty,
            customerName,
            serviceMode,
            source = "Pos",
            paymentTiming = "Immediate",
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

        Guid paymentKey = Guid.NewGuid();
        await RetryAsync(
            async () =>
            {
                using HttpRequestMessage payment = Authorized(
                    HttpMethod.Post,
                    $"/api/payments/orders/{order.Id}/capture"
                );
                payment.Content = JsonContent.Create(
                    new
                    {
                        idempotencyKey = paymentKey,
                        method = paymentMethod,
                        externalReference = PaymentReference(
                            paymentMethod,
                            cashRegisterSessionId,
                            $"POS-{serviceMode.ToUpperInvariant()}-{order.Id:N}"
                        ),
                    }
                );
                return await http.SendAsync(payment);
            },
            [404, 425],
            "La proyección de pago no estuvo disponible a tiempo."
        );

        return await RetryAsync(
            async () =>
            {
                using HttpRequestMessage submit = Authorized(
                    HttpMethod.Post,
                    $"/api/orders/{order.Id}/submit"
                );
                return await http.SendAsync(submit);
            },
            [409],
            "El pago no se proyectó en Orders a tiempo.",
            ReadAsync<OrderResponse>
        );
    }

    public async Task<OrderResponse> CreateAndSubmitTakeawayOnAccountAsync(
        Guid restaurantId,
        string customerName,
        IReadOnlyDictionary<Guid, int> quantities,
        IReadOnlyDictionary<Guid, string> notes
    )
    {
        object body = new
        {
            restaurantId,
            tableId = (Guid?)null,
            diningSessionId = (Guid?)null,
            tableLabel = string.Empty,
            customerName,
            serviceMode = "Takeaway",
            source = "Pos",
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

    public async Task<OrderResponse> DeliverPickupAsync(
        Guid orderId,
        string? paymentMethod,
        Guid? cashRegisterSessionId
    )
    {
        if (!string.IsNullOrWhiteSpace(paymentMethod))
        {
            Guid paymentKey = Guid.NewGuid();
            await RetryAsync(
                async () =>
                {
                    using HttpRequestMessage payment = Authorized(
                        HttpMethod.Post,
                        $"/api/payments/orders/{orderId}/capture"
                    );
                    payment.Content = JsonContent.Create(
                        new
                        {
                            idempotencyKey = paymentKey,
                            method = paymentMethod,
                            externalReference = PaymentReference(
                                paymentMethod,
                                cashRegisterSessionId,
                                $"POS-PICKUP-{orderId:N}"
                            ),
                        }
                    );
                    return await http.SendAsync(payment);
                },
                [404, 425],
                "La proyección de pago no estuvo disponible a tiempo."
            );
        }

        return await RetryAsync(
            async () =>
            {
                using HttpRequestMessage deliver = Authorized(
                    HttpMethod.Post,
                    $"/api/orders/{orderId}/deliver"
                );
                return await http.SendAsync(deliver);
            },
            [409],
            "El pago no se proyectó en Orders a tiempo.",
            ReadAsync<OrderResponse>
        );
    }

    private static async Task RetryAsync(
        Func<Task<HttpResponseMessage>> send,
        int[] retryStatusCodes,
        string timeoutMessage
    )
    {
        for (int attempt = 1; attempt <= 20; attempt++)
        {
            using HttpResponseMessage response = await send();
            if (response.IsSuccessStatusCode)
            {
                return;
            }
            if (!retryStatusCodes.Contains((int)response.StatusCode))
            {
                await ReadAsync<object>(response);
            }
            await Task.Delay(250);
        }
        throw new InvalidOperationException(timeoutMessage);
    }

    public static string PaymentReference(
        string method,
        Guid? cashRegisterSessionId,
        string reference
    )
    {
        return cashRegisterSessionId.HasValue
            ? $"CASHREGISTER:{cashRegisterSessionId.Value};{reference}"
            : reference;
    }

    private static async Task<T> RetryAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        int[] retryStatusCodes,
        string timeoutMessage,
        Func<HttpResponseMessage, Task<T>> read
    )
    {
        for (int attempt = 1; attempt <= 20; attempt++)
        {
            using HttpResponseMessage response = await send();
            if (response.IsSuccessStatusCode)
            {
                return await read(response);
            }
            if (!retryStatusCodes.Contains((int)response.StatusCode))
            {
                return await read(response);
            }
            await Task.Delay(250);
        }
        throw new InvalidOperationException(timeoutMessage);
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
                ?? throw new InvalidOperationException("La API devolvió una respuesta vacía.");
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
    string Source,
    string Status,
    List<DiningSessionOrderResponse> Orders,
    bool RequestGuestCount = false,
    int? GuestCount = null
);

public sealed record DiningSessionOrderResponse(
    Guid OrderId,
    string OrderStatus,
    string PaymentStatus,
    decimal Amount
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

public sealed record DiningPolicyResponse(
    Guid RestaurantId,
    bool QrRequiresImmediatePayment,
    bool TakeawayRequiresPrepayment
);

public sealed record CashRegisterResponse(
    Guid Id,
    Guid RestaurantId,
    DateOnly BusinessDate,
    string Status,
    decimal OpeningFloat,
    decimal TotalSales,
    decimal TotalRefunds,
    decimal CashSales,
    decimal CashRefunds,
    decimal ExpectedCash,
    decimal ExpectedTotal,
    decimal? ReconciledTotal,
    decimal? CountedCash,
    decimal? Difference,
    DateTime OpenedAtUtc,
    string OpenedByName,
    DateTime? ClosedAtUtc,
    string? ClosedByName,
    List<PaymentMethodTotal> PaymentMethods,
    List<PaymentMethodExpected> ExpectedByMethod,
    List<PaymentMethodReconciliation> Reconciliations,
    List<CashMovementResponse> Movements
);

public sealed record PaymentMethodTotal(string Method, decimal Sales, decimal Refunds, decimal Net);

public sealed record PaymentMethodExpected(string Method, decimal Expected);

public sealed record PaymentMethodReconciliation(
    string Method,
    decimal Expected,
    decimal Reconciled,
    decimal Difference
);

public sealed record CashMovementResponse(
    Guid Id,
    string Type,
    string Method,
    decimal Amount,
    string Reference,
    DateTime OccurredAtUtc
);

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

public sealed record SessionBillOrderResponse(
    Guid OrderId,
    string OrderStatus,
    string PaymentStatus,
    decimal Amount,
    DateTime AddedAtUtc
);
