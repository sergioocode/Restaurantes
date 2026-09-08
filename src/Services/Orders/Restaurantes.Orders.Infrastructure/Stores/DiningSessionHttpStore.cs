using System.Net;
using Restaurantes.Orders.Application;

namespace Restaurantes.Orders.Infrastructure.Stores;

public sealed class DiningSessionHttpStore(HttpClient client) : IDiningSessionStore
{
    public async Task<int?> EnsureOpenAsync(
        Guid diningSessionId,
        Guid restaurantId,
        Guid tableId,
        string source,
        string serviceMode,
        string customerAccessToken,
        CancellationToken ct
    )
    {
        string path =
            $"/api/dining/sessions/{diningSessionId}/validate"
            + $"?restaurantId={restaurantId}&tableId={tableId}"
            + $"&source={Uri.EscapeDataString(source)}"
            + $"&serviceMode={Uri.EscapeDataString(serviceMode)}";
        HttpResponseMessage response;
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, path);
            if (!string.IsNullOrWhiteSpace(customerAccessToken))
            {
                request.Headers.Add("X-Customer-Session-Token", customerAccessToken);
            }
            response = await client.SendAsync(request, ct);
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException(
                "Dining is unavailable; the order cannot reserve an unverified table session.",
                e
            );
        }
        using (response)
        {
            return response.StatusCode == HttpStatusCode.NoContent
                ? response.Headers.TryGetValues(
                    "X-Dining-Guest-Count",
                    out IEnumerable<string>? values
                ) && int.TryParse(values.SingleOrDefault(), out int count)
                    ? count
                    : null
                : throw new InvalidOperationException(
                    "The dining session is not open or does not belong to the requested restaurant and table."
                );
        }
    }
}
