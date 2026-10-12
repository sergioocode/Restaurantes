namespace Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read;

public sealed class CompanyReadModel
{
    public Guid Id { get; set; }
    public string LegalName { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string FiscalAddress { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
