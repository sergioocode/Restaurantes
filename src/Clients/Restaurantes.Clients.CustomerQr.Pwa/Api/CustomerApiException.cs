using System.Net;

namespace Restaurantes.Clients.CustomerQr.Pwa.Api;

public sealed class CustomerApiException(HttpStatusCode statusCode, string message)
    : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
