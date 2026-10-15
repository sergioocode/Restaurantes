namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class CategoryDraft
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultStationCode { get; set; } = string.Empty;
    public string DefaultStationName { get; set; } = string.Empty;
    public bool RequiresKds { get; set; } = true;
    public bool ShowInPos { get; set; } = true;
    public bool ShowInCommander { get; set; } = true;
    public bool ShowInCustomerQr { get; set; } = true;
}
