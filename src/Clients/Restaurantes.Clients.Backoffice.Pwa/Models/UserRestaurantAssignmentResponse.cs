namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class UserRestaurantAssignmentResponse
{
    public Guid RestaurantId { get; set; }
    public string Role { get; set; } = "PosComandero";
    public bool IsActive { get; set; }
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidUntilUtc { get; set; }
}
