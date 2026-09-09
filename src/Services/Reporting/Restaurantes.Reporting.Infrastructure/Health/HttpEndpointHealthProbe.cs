using Restaurantes.Reporting.Application.Health;

namespace Restaurantes.Reporting.Infrastructure.Health;

public sealed class HttpEndpointHealthProbe(IHttpClientFactory clients) : IEndpointHealthProbe
{
    public async Task<EndpointHealth> CheckAsync(MonitoredEndpoint endpoint, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await clients
                .CreateClient(nameof(HttpEndpointHealthProbe))
                .GetAsync(endpoint.Url, ct);
            return new(
                endpoint.Service,
                endpoint.Url,
                response.IsSuccessStatusCode,
                (int)response.StatusCode
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(endpoint.Service, endpoint.Url, false, 0);
        }
    }
}
