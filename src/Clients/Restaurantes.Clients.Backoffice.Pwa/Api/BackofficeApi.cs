namespace Restaurantes.Clients.Backoffice.Pwa.Api;

public sealed partial class BackofficeApi(HttpClient http)
{
    public string? AccessToken { get; set; }
}
