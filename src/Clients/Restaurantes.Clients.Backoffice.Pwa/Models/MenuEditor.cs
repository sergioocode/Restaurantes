namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class MenuEditor
{
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; }
    public string PreparationStationCode { get; set; } = "GENERAL";
    public string PreparationStationName { get; set; } = "General";
}
