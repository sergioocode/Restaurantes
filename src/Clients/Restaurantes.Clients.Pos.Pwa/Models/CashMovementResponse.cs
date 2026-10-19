namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record CashMovementResponse(
    Guid Id,
    string Type,
    string Method,
    decimal Amount,
    string Reference,
    DateTime OccurredAtUtc,
    string Category = "",
    string RecordedByName = ""
);
