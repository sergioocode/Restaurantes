namespace Restaurantes.Catalog.Infrastructure.Persistence.Read;

public sealed class InboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public DateTime ProcessedAtUtc { get; set; }
}
