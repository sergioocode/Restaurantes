namespace Restaurantes.RestaurantOperations.Contracts.Responses;

public sealed record RestaurantCompanyResponse(
    Guid Id,
    string LegalName,
    string TaxId,
    string FiscalAddress,
    string? Phone,
    string? Email
);
