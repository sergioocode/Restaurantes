namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class StaffUserDraft
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool AllRestaurants { get; set; } = true;
    public Guid? RestaurantId { get; set; }
    public string Role { get; set; } = "Camarero";
}
