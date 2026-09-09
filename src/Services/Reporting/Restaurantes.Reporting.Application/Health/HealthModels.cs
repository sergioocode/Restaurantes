namespace Restaurantes.Reporting.Application.Health;

public sealed record MonitoredEndpoint(string Service, string Url);

public sealed record EndpointHealth(string Service, string Url, bool Healthy, int Status);
