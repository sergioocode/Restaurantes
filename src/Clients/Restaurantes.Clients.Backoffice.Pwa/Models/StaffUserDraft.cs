namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class StaffUserDraft
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public Guid RestaurantId { get; set; }
    public string Role { get; set; } = "PosComandero";
}
