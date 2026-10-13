namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record RestaurantResponse(
    Guid Id,
    string Code,
    string Name,
    string Address,
    bool IsActive,
    RestaurantCompanyResponse? Company = null
);

public sealed record RestaurantCompanyResponse(
    Guid Id,
    string LegalName,
    string TaxId,
    string FiscalAddress,
    string? Phone,
    string? Email
);
