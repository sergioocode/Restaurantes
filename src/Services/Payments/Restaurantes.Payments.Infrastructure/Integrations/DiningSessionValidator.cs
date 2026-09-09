using System.Net;
using Restaurantes.Payments.Application;

namespace Restaurantes.Payments.Infrastructure.Integrations;

public sealed class DiningSessionValidator(HttpClient httpClient) : ICustomerDiningSessionValidator
{
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
