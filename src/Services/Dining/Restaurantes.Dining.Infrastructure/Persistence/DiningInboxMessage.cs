namespace Restaurantes.Dining.Infrastructure.Persistence;

public sealed class DiningInboxMessage
{
    public Guid Id { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
