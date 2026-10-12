namespace Restaurantes.Orders.Contracts.Responses;

public sealed record KitchenHistoryLineResponse(
    string ProductName,
    int Quantity,
    string Status,
    string Format = ""
);

public sealed record KitchenHistoryResponse(
    Guid OrderId,
    string TableLabel,
    string CustomerName,
    Guid? CashRegisterSessionId,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ProcessedAtUtc,
    IReadOnlyList<KitchenHistoryLineResponse> Lines
);
