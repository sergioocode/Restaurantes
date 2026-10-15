namespace Restaurantes.RestaurantOperations.Contracts.Events;

public sealed record CompanyChanged(
    Guid CompanyId,
    string LegalName,
    string TaxId,
    string FiscalAddress,
    string? Phone,
    string? Email,
    int Version,
    DateTime OccurredAtUtc
);
