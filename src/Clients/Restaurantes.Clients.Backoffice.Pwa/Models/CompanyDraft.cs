namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class CompanyDraft
{
    public string LegalName { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string FiscalAddress { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
}
