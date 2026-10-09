namespace Restaurantes.Integrations.Contracts.Responses;

public sealed record IntegrationsResponse(string Service, IReadOnlyCollection<string> Capabilities);
