namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class StaffUserResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<UserRestaurantAssignmentResponse> Restaurants { get; set; } = [];
}
