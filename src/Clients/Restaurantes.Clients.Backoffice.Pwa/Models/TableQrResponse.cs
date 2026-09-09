namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed record TableQrResponse(
    Guid Id,
    Guid RestaurantId,
    string Code,
    string Label,
    string QrCode,
    string QrPath,
    string CustomerQrPath
);
