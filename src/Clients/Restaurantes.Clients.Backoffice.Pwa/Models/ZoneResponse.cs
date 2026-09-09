namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class ZoneResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
