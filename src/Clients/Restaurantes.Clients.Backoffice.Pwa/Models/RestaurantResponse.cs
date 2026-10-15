namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class RestaurantResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public RestaurantCompanyResponse? Company { get; set; }
}

public sealed class RestaurantCompanyResponse
{
    public Guid Id { get; set; }
    public string LegalName { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string FiscalAddress { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
}
