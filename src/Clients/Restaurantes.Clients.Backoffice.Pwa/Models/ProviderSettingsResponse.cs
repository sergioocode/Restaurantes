namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed record ProviderSettingsResponse(
    string ActiveProvider,
    bool MicrosoftConfigured,
    bool GoogleConfigured
);
