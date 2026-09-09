namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class TableDraft
{
    public bool RequestGuestCount { get; set; }
    public Guid ZoneId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
