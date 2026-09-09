namespace Restaurantes.Clients.Commander.Pwa.Api;

public sealed partial class CommanderApi(HttpClient http)
{
    public string? AccessToken { get; set; }
}
