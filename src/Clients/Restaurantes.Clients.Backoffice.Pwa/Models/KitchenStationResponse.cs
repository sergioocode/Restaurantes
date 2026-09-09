namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class KitchenStationResponse
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool RequiresPrimaryDispatch { get; set; }
    public int Priority { get; set; } = 1;
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
