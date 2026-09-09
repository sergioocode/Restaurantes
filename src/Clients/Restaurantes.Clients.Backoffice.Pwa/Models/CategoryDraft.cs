namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class CategoryDraft
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultStationCode { get; set; } = string.Empty;
    public string DefaultStationName { get; set; } = string.Empty;
}
