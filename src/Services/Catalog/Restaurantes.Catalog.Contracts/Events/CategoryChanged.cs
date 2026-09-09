namespace Restaurantes.Catalog.Contracts.Events;

public sealed record CategoryChanged(
    Guid CategoryId,
    string Code,
    string Name,
    string DefaultStationCode,
    string DefaultStationName,
    bool IsActive,
    int Version,
    DateTime OccurredAtUtc
);
