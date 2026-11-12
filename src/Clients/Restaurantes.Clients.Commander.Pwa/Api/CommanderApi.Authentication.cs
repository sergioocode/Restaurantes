using Restaurantes.Clients.Commander.Pwa.Models;
using Restaurantes.Clients.Shared.Operations;

namespace Restaurantes.Clients.Commander.Pwa.Api;

public sealed partial class CommanderApi
{
    public Task<LoginResponse> ExchangeAsync(string code) =>
        ClientLoginFlow.ExchangeAsync<LoginResponse>(http, code);
}
