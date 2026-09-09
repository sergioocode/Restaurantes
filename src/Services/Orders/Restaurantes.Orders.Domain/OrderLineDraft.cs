namespace Restaurantes.Orders.Domain;

public sealed record OrderLineDraft(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    string Notes,
    Guid? CategoryId,
    string CategoryName,
    string PreparationStationCode,
    string PreparationStationName,
    bool RequiresPrimaryDispatch,
    int Priority
);
