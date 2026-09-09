namespace Restaurantes.Reporting.Application.Health;

public interface IMonitoredEndpointCatalog
{
    IReadOnlyCollection<MonitoredEndpoint> GetAll();
}

public interface IEndpointHealthProbe
{
    Task<EndpointHealth> CheckAsync(MonitoredEndpoint endpoint, CancellationToken ct);
}

public sealed class ReportingHealthService(
    IMonitoredEndpointCatalog catalog,
    IEndpointHealthProbe probe
)
{
    public async Task<IReadOnlyList<EndpointHealth>> GetAsync(CancellationToken ct)
    {
        Task<EndpointHealth>[] checks = catalog
            .GetAll()
            .Select(endpoint => probe.CheckAsync(endpoint, ct))
            .ToArray();
        return await Task.WhenAll(checks);
    }
}
