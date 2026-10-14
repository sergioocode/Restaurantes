namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class CategoryResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultStationCode { get; set; } = string.Empty;
    public string DefaultStationName { get; set; } = string.Empty;
    public bool RequiresKds { get; set; } = true;
    public bool IsActive { get; set; }
    public bool ShowInPos { get; set; } = true;
    public bool ShowInCommander { get; set; } = true;
    public bool ShowInCustomerQr { get; set; } = true;
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
