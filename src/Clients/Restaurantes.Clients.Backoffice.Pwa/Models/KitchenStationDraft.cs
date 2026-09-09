namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class KitchenStationDraft
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool RequiresPrimaryDispatch { get; set; }
    public int Priority { get; set; } = 1;
}
