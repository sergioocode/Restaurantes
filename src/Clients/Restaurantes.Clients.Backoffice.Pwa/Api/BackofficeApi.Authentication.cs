using Restaurantes.Clients.Backoffice.Pwa.Models;
using Restaurantes.Clients.Shared.Operations;

namespace Restaurantes.Clients.Backoffice.Pwa.Api;

public sealed partial class BackofficeApi
{
    public Task<LoginResponse> ExchangeAsync(string code) =>
        ClientLoginFlow.ExchangeAsync<LoginResponse>(http, code);
}
