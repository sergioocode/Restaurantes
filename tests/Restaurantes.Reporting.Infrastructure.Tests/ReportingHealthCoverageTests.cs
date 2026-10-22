using System.Net;
using Microsoft.Extensions.Http;
using Restaurantes.Reporting.Application.Health;
using Restaurantes.Reporting.Infrastructure.Health;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class ReportingHealthCoverageTests
{
    [Fact]
    public async Task Http_probe_maps_success_and_unavailable_responses()
    {
        HttpEndpointHealthProbe healthyProbe = new(
            new StaticHttpClientFactory(
                new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent))
            )
        );
        HttpEndpointHealthProbe failedProbe = new(
            new StaticHttpClientFactory(
                new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
            )
        );
        MonitoredEndpoint endpoint = new("orders", "http://orders/health");

        EndpointHealth healthy = await healthyProbe.CheckAsync(endpoint, CancellationToken.None);
        EndpointHealth failed = await failedProbe.CheckAsync(endpoint, CancellationToken.None);

        Assert.Equal(new EndpointHealth("orders", endpoint.Url, true, 204), healthy);
        Assert.Equal(new EndpointHealth("orders", endpoint.Url, false, 503), failed);
    }

    [Fact]
    public async Task Http_probe_returns_unavailable_for_transport_errors_and_rethrows_cancellation()
    {
        HttpEndpointHealthProbe probe = new(
            new StaticHttpClientFactory(
                new StubHandler(_ => throw new HttpRequestException("Connection refused."))
            )
        );
        MonitoredEndpoint endpoint = new("payments", "http://payments/health");

        EndpointHealth unavailable = await probe.CheckAsync(endpoint, CancellationToken.None);

        Assert.Equal(new EndpointHealth("payments", endpoint.Url, false, 0), unavailable);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            probe.CheckAsync(endpoint, cancellation.Token)
        );
    }

    [Fact]
    public async Task Reporting_health_service_checks_every_catalogued_endpoint_and_allows_empty_catalog()
    {
        MonitoredEndpoint[] endpoints =
        [
            new("orders", "http://orders/health"),
            new("payments", "http://payments/health"),
        ];
        RecordingProbe probe = new();
        ReportingHealthService service = new(new StaticCatalog(endpoints), probe);

        IReadOnlyList<EndpointHealth> result = await service.GetAsync(CancellationToken.None);

        Assert.Equal(endpoints, probe.Checked);
        Assert.Equal(endpoints.Length, result.Count);
        Assert.All(result, item => Assert.True(item.Healthy));

        ReportingHealthService emptyService = new(new StaticCatalog([]), probe);
        Assert.Empty(await emptyService.GetAsync(CancellationToken.None));
    }

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }

    private sealed class StaticCatalog(IReadOnlyCollection<MonitoredEndpoint> endpoints)
        : IMonitoredEndpointCatalog
    {
        public IReadOnlyCollection<MonitoredEndpoint> GetAll() => endpoints;
    }

    private sealed class RecordingProbe : IEndpointHealthProbe
    {
        public List<MonitoredEndpoint> Checked { get; } = [];

        public Task<EndpointHealth> CheckAsync(MonitoredEndpoint endpoint, CancellationToken ct)
        {
            Checked.Add(endpoint);
            return Task.FromResult(new EndpointHealth(endpoint.Service, endpoint.Url, true, 200));
        }
    }
}
