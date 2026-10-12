namespace Restaurantes.RestaurantOperations.Contracts.Responses;

public sealed record CompanyResponse(
    Guid Id,
    string LegalName,
    string TaxId,
    string FiscalAddress,
    string? Phone,
    string? Email,
    int Version,
    DateTime UpdatedAtUtc
);
