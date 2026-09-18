namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record ProviderSettingsResponse(
    string ActiveProvider,
    bool MicrosoftConfigured,
    bool GoogleConfigured
);
