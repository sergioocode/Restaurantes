using System.Net.Http.Json;
using Restaurantes.Clients.CustomerQr.Pwa.Models;

namespace Restaurantes.Clients.CustomerQr.Pwa.Api;

public sealed partial class CustomerQrApi
{
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
}
