using System.Net.Http.Headers;
using System.Net.Http.Json;
using Restaurantes.Dining.Application;

namespace Restaurantes.Dining.Infrastructure.Integrations;

public sealed class DiningPayments(IHttpClientFactory clients) : IDiningPayments
{
    public async Task<DiningPaymentResult> CaptureAsync(
        Guid orderId, DiningPaymentRequest request, string? authorization, CancellationToken ct
    )
    {
        HttpClient payments = clients.CreateClient("payments");
        using HttpRequestMessage payment = new(HttpMethod.Post, $"/api/payments/orders/{orderId}/capture");
        if (AuthenticationHeaderValue.TryParse(authorization, out AuthenticationHeaderValue? header))
        {
            payment.Headers.Authorization = header;
        }

        payment.Content = JsonContent.Create(request);
        using HttpResponseMessage response = await payments.SendAsync(payment, ct);
        string body = response.IsSuccessStatusCode ? string.Empty : await response.Content.ReadAsStringAsync(ct);
        return new(response.IsSuccessStatusCode, (int)response.StatusCode, body);
    }
}
