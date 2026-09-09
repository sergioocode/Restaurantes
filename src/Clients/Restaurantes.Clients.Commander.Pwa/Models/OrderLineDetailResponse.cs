namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record OrderLineDetailResponse(
    Guid Id,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    string PreparationStationCode,
    string PreparationStationName,
    string Status,
    DateTime? CancelledAtUtc,
    string CancellationReason
);
