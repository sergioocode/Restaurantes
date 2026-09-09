using System.Net.Http.Json;
using Restaurantes.Clients.CustomerQr.Pwa.Models;

namespace Restaurantes.Clients.CustomerQr.Pwa.Api;

public sealed partial class CustomerQrApi
{
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
}
