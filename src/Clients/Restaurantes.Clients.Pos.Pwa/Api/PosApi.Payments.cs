using System.Net.Http.Json;
using Restaurantes.Clients.Pos.Pwa.Models;

namespace Restaurantes.Clients.Pos.Pwa.Api;

public sealed partial class PosApi
{
    public async Task<List<OrderPaymentStatusResponse>> PaymentStatusesAsync(
        Guid restaurantId,
        Guid orderId
    )
    {
        return await PaymentStatusesAsync(restaurantId, [orderId]);
    }

    public async Task<List<OrderPaymentStatusResponse>> PaymentStatusesAsync(
        Guid restaurantId,
        IReadOnlyCollection<Guid> orderIds
    )
    {
        using HttpRequestMessage request = Authorized(
            HttpMethod.Post,
            $"/api/payments/restaurants/{restaurantId}/order-statuses"
        );
        request.Content = JsonContent.Create(new { orderIds });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<List<OrderPaymentStatusResponse>>(response);
    }

    public async Task CapturePickupAsync(
        Guid orderId,
        string paymentMethod,
        Guid? cashRegisterSessionId,
        string reference,
        Guid idempotencyKey
    )
    {
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
                        idempotencyKey,
                        method = paymentMethod,
                        externalReference = PaymentReference(
                            paymentMethod,
                            cashRegisterSessionId,
                            string.IsNullOrWhiteSpace(reference)
                                ? $"POS-PICKUP-{orderId:N}"
                                : reference.Trim()
                        ),
                    }
                );
                return await http.SendAsync(payment);
            },
            [404, 425],
            "La proyección de pago no estuvo disponible a tiempo."
        );
    }
}
