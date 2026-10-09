namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record OrderStationProgressResponse(
    string Code,
    string Name,
    string Status,
    DateTime? ReadyAtUtc = null
);
