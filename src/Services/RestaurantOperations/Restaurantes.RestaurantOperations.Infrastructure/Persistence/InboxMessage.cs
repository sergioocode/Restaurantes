namespace Restaurantes.RestaurantOperations.Infrastructure.Persistence;

public sealed class InboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }
}
