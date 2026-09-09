namespace Restaurantes.Catalog.Infrastructure.Persistence.Read;

public sealed class CategoryReadModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DefaultStationCode { get; set; } = "";
    public string DefaultStationName { get; set; } = "";
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
