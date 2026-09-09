namespace Restaurantes.Orders.Contracts.Events;

public sealed record OrderKitchenStationSnapshot(
    string Code,
    string Name,
    string Status,
    bool RequiresPrimaryDispatch,
    int Priority,
    DateTime? PreparationStartedAtUtc,
    DateTime? ReadyAtUtc,
    DateTime? DispatchedAtUtc
);
