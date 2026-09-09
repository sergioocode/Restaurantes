namespace Restaurantes.Dining.Domain;

public sealed class RestaurantTable
{
    public bool RequestGuestCount { get; set; }
    public Guid ZoneId { get; set; }
    public DiningZone Zone { get; set; } = null!;
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string QrCode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? DeletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
