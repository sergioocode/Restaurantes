using System.Net.Http.Json;
using Restaurantes.Clients.Pos.Pwa.Models;

namespace Restaurantes.Clients.Pos.Pwa.Api;

public sealed partial class PosApi
{
    public Task<List<OrderResponse>> OrdersAsync(Guid restaurantId)
    {
        return GetAsync<List<OrderResponse>>($"/api/orders?restaurantId={restaurantId}");
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

    public async Task<OrderResponse> CreateAndSubmitAsync(
        Guid restaurantId,
        TableResponse table,
        Guid sessionId,
        string serviceMode,
        string customerName,
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
            customerName,
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

    public async Task<OrderResponse> CreateAndSubmitTakeawayAsync(
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
            tableLabel = "PARA LLEVAR",
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

    public async Task<OrderResponse> DeliverPickupAsync(Guid orderId)
    {
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
}
