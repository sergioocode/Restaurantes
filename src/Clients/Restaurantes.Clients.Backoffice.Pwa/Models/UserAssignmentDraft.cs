namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class UserAssignmentDraft
{
    public Guid RestaurantId { get; set; }
    public string Role { get; set; } = "PosComandero";
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidUntilUtc { get; set; }
}
