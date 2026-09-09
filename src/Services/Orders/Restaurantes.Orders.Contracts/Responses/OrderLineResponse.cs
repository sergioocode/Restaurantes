namespace Restaurantes.Orders.Contracts.Responses;

public sealed record OrderLineResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    string Notes,
    Guid? CategoryId,
    string CategoryName,
    string PreparationStationCode,
    string PreparationStationName,
    string Status = "Active",
    DateTime? CancelledAtUtc = null,
    string CancellationReason = ""
);
