namespace Restaurantes.Orders.Contracts.Responses;

public sealed record OrderKitchenStationResponse(
    string Code,
    string Name,
    string Status,
    bool RequiresPrimaryDispatch,
    int Priority,
    DateTime? PreparationStartedAtUtc,
    DateTime? ReadyAtUtc,
    DateTime? DispatchedAtUtc
);
