namespace Restaurantes.Clients.Pos.Pwa.Api;

public sealed partial class PosApi(HttpClient http)
{
    public string? AccessToken { get; set; }
}
