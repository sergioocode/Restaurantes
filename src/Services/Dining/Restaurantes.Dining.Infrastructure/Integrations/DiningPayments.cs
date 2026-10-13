using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Restaurantes.Dining.Application;

namespace Restaurantes.Dining.Infrastructure.Integrations;

public sealed class DiningPayments(IHttpClientFactory clients) : IDiningPayments
{
    public async Task<DiningPaymentResult> AdjustAmountAsync(
        Guid orderId,
        decimal amount,
        string? authorization,
        CancellationToken ct
    )
    {
        HttpClient payments = clients.CreateClient("payments");
        using HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/payments/orders/{orderId}/amount"
        );
        if (
            AuthenticationHeaderValue.TryParse(authorization, out AuthenticationHeaderValue? header)
        )
        {
            request.Headers.Authorization = header;
        }
        request.Content = JsonContent.Create(new { amount });
        using HttpResponseMessage response = await payments.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        return new(response.IsSuccessStatusCode, (int)response.StatusCode, body);
    }

    public async Task<DiningPaymentResult> CaptureAsync(
        Guid orderId,
        DiningPaymentRequest request,
        string? authorization,
        CancellationToken ct
    )
    {
        HttpClient payments = clients.CreateClient("payments");
        using HttpRequestMessage payment = new(
            HttpMethod.Post,
            $"/api/payments/orders/{orderId}/capture"
        );
        if (
            AuthenticationHeaderValue.TryParse(authorization, out AuthenticationHeaderValue? header)
        )
        {
            payment.Headers.Authorization = header;
        }

        payment.Content = JsonContent.Create(request);
        using HttpResponseMessage response = await payments.SendAsync(payment, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return new(false, (int)response.StatusCode, body);
        }

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        decimal amountPaid = root.TryGetProperty("amountPaid", out JsonElement paid)
            ? paid.GetDecimal()
            : 0;
        decimal tipPaid = root.TryGetProperty("tipAmountPaid", out JsonElement tip)
            ? tip.GetDecimal()
            : 0;
        return new(true, (int)response.StatusCode, body, amountPaid, tipPaid);
    }
}
