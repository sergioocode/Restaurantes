namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record ProviderSettingsResponse(
    string ActiveProvider,
    bool MicrosoftConfigured,
    bool GoogleConfigured
);
