using System.Net;

namespace Restaurantes.Payments.Api.Write;

public sealed class CustomerDiningSessionValidator(HttpClient http)
{
    public async Task<bool> IsValidAsync(
        Guid sessionId,
        Guid restaurantId,
        Guid tableId,
        string serviceMode,
        string customerAccessToken,
        CancellationToken ct
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
            using HttpResponseMessage response = await http.SendAsync(request, ct);
            return response.StatusCode == HttpStatusCode.NoContent;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
