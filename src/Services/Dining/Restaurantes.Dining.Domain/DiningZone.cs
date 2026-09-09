namespace Restaurantes.Dining.Domain;

public sealed class DiningZone
{
    public DateTime? DeletedAtUtc { get; set; }
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
