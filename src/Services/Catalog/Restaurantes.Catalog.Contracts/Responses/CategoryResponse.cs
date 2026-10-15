namespace Restaurantes.Catalog.Contracts.Responses;

public sealed record CategoryResponse(
    Guid Id,
    string Code,
    string Name,
    string DefaultStationCode,
    string DefaultStationName,
    bool IsActive,
    int Version,
    DateTime UpdatedAtUtc,
    bool ShowInPos = true,
    bool ShowInCommander = true,
    bool ShowInCustomerQr = true,
    bool RequiresKds = true
);
