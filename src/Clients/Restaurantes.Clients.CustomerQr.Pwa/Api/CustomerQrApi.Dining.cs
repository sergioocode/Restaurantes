using System.Net.Http.Json;
using Restaurantes.Clients.CustomerQr.Pwa.Models;

namespace Restaurantes.Clients.CustomerQr.Pwa.Api;

public sealed partial class CustomerQrApi
{
    public async Task<QrDiningSessionResponse> SetGuestCountAsync(
        Guid sessionId,
        int guestCount,
        string? customerAccessToken = null
    )
    {
        using HttpRequestMessage request = WithCustomerToken(
            HttpMethod.Put,
            $"/api/dining/sessions/{sessionId}/guests",
            customerAccessToken
        );
        request.Content = JsonContent.Create(new { guestCount });
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<QrDiningSessionResponse>(response);
    }

    public async Task<QrSessionEnvelope> PreviewAsync(string qrCode, string? customerAccessToken)
    {
        using HttpRequestMessage request = WithCustomerToken(
            HttpMethod.Get,
            $"/api/dining/qr/{Uri.EscapeDataString(qrCode)}",
            customerAccessToken
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<QrSessionEnvelope>(response);
    }

    public async Task<QrSessionEnvelope> OpenSessionAsync(
        string qrCode,
        string? customerAccessToken = null,
        int? guestCount = null
    )
    {
        using HttpRequestMessage request = WithCustomerToken(
            HttpMethod.Post,
            $"/api/dining/qr/{Uri.EscapeDataString(qrCode)}/sessions"
                + (guestCount.HasValue ? $"?guestCount={guestCount.Value}" : string.Empty),
            customerAccessToken
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<QrSessionEnvelope>(response);
    }
}
