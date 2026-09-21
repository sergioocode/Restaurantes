namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class StaffUserResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool AllRestaurants { get; set; }
    public Guid? RestaurantId { get; set; }
    public bool IsActive { get; set; }
    public bool IsLinked { get; set; }
}
