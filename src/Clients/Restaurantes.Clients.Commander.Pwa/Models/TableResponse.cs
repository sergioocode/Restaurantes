namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record TableResponse(
    Guid Id,
    Guid RestaurantId,
    string Code,
    string Label,
    bool IsActive,
    string Status,
    Guid? ActiveSessionId,
    Guid ZoneId,
    string ZoneName,
    int ZoneSortOrder
);
