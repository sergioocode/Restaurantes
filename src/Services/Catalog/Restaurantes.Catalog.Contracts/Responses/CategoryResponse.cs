namespace Restaurantes.Catalog.Contracts.Responses;

public sealed record CategoryResponse(
    Guid Id,
    string Code,
    string Name,
    string DefaultStationCode,
    string DefaultStationName,
    bool IsActive,
    int Version,
    DateTime UpdatedAtUtc
);
