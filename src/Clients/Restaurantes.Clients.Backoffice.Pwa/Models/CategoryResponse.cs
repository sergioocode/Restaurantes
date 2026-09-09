namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class CategoryResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultStationCode { get; set; } = string.Empty;
    public string DefaultStationName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
