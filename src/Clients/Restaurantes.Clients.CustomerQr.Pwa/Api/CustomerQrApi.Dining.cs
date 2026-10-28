using System.Net.Http.Json;
using Restaurantes.Clients.CustomerQr.Pwa.Models;

namespace Restaurantes.Clients.CustomerQr.Pwa.Api;

public sealed partial class CustomerQrApi
{
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
        string? customerAccessToken = null
    )
    {
        using HttpRequestMessage request = WithCustomerToken(
            HttpMethod.Post,
            $"/api/dining/qr/{Uri.EscapeDataString(qrCode)}/sessions",
            customerAccessToken
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<QrSessionEnvelope>(response);
    }

    public async Task<QrReservationActivityResponse> TouchReservationAsync(
        Guid sessionId,
        string customerAccessToken
    )
    {
        using HttpRequestMessage request = WithCustomerToken(
            HttpMethod.Post,
            $"/api/dining/sessions/{sessionId}/qr/activity",
            customerAccessToken
        );
        using HttpResponseMessage response = await http.SendAsync(request);
        return await ReadAsync<QrReservationActivityResponse>(response);
    }
}
