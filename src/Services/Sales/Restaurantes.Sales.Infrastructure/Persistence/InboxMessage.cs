namespace Restaurantes.Sales.Infrastructure.Persistence;

public sealed class InboxMessage
{
    public Guid Id { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
