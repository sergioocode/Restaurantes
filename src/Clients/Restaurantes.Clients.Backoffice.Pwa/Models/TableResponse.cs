namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class TableResponse
{
    public bool RequestGuestCount { get; set; }
    public Guid ZoneId { get; set; }
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? QrCode { get; set; }
    public string? QrPath { get; set; }
    public string? CustomerQrPath { get; set; }
    public bool IsActive { get; set; }
    public string Status { get; set; } = "Available";
    public Guid? ActiveSessionId { get; set; }
}
