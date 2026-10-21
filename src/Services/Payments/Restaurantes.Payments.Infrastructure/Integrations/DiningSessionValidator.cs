using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Restaurantes.Payments.Application;

namespace Restaurantes.Payments.Infrastructure.Integrations;

public sealed class DiningSessionValidator(HttpClient httpClient) : ICustomerDiningSessionValidator
{
    public async Task<bool> IsChargeAmountValidAsync(
        Guid sessionId,
        Guid orderId,
        Guid restaurantId,
        decimal amount,
        string? authorization,
        CancellationToken cancellationToken
    )
    {
        if (
            !AuthenticationHeaderValue.TryParse(
                authorization,
                out AuthenticationHeaderValue? header
            )
        )
        {
            return false;
        }

        using HttpRequestMessage request = new(
            HttpMethod.Get,
            $"/api/dining/sessions/{sessionId}/bill"
        );
        request.Headers.Authorization = header;
        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            using JsonDocument document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken)
            );
            JsonElement root = document.RootElement;
            if (
                !root.TryGetProperty("restaurantId", out JsonElement restaurant)
                || !restaurant.TryGetGuid(out Guid actualRestaurantId)
                || actualRestaurantId != restaurantId
                || !root.TryGetProperty("orders", out JsonElement orders)
                || orders.ValueKind != JsonValueKind.Array
            )
            {
                return false;
            }

            return orders
                .EnumerateArray()
                .Any(order =>
                    order.TryGetProperty("orderId", out JsonElement id)
                    && id.TryGetGuid(out Guid actualOrderId)
                    && actualOrderId == orderId
                    && order.TryGetProperty("amount", out JsonElement expected)
                    && expected.TryGetDecimal(out decimal expectedAmount)
                    && expectedAmount == amount
                );
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<bool> IsValidAsync(
        Guid sessionId,
        Guid restaurantId,
        Guid tableId,
        string serviceMode,
        string customerAccessToken,
        CancellationToken cancellationToken
    )
    {
        string path =
            $"/api/dining/sessions/{sessionId}/validate"
            + $"?restaurantId={restaurantId}&tableId={tableId}&source=CustomerQr"
            + $"&serviceMode={Uri.EscapeDataString(serviceMode)}";
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Add("X-Customer-Session-Token", customerAccessToken);
        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                cancellationToken
            );
            return response.StatusCode == HttpStatusCode.NoContent;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
