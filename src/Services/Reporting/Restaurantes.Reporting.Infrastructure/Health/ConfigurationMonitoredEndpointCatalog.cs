using Microsoft.Extensions.Configuration;
using Restaurantes.Reporting.Application.Health;

namespace Restaurantes.Reporting.Infrastructure.Health;

public sealed class ConfigurationMonitoredEndpointCatalog(IConfiguration configuration)
    : IMonitoredEndpointCatalog
{
    public IReadOnlyCollection<MonitoredEndpoint> GetAll()
    {
        return configuration
            .GetSection("MonitoredEndpoints")
            .GetChildren()
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => new MonitoredEndpoint(x.Key, x.Value!))
            .ToArray();
    }
}
