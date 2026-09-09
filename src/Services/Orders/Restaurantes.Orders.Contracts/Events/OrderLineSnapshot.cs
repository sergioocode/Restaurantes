namespace Restaurantes.Orders.Contracts.Events;

public sealed record OrderLineSnapshot(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    string Notes,
    Guid? CategoryId,
    string CategoryName,
    string PreparationStationCode,
    string PreparationStationName,
    string Status = "Active",
    DateTime? CancelledAtUtc = null,
    string CancellationReason = ""
);
