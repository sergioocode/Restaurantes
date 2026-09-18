namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class StaffUserDraft
{
    public string Email { get; set; } = string.Empty;
    public string Provider { get; set; } = "Microsoft";
    public string DisplayName { get; set; } = string.Empty;
    public Guid? RestaurantId { get; set; }
    public string Role { get; set; } = "PosComandero";
}
