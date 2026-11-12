using Restaurantes.Clients.Pos.Pwa.Models;
using Restaurantes.Clients.Shared.Operations;

namespace Restaurantes.Clients.Pos.Pwa.Api;

public sealed partial class PosApi
{
    public Task<LoginResponse> ExchangeAsync(string code) =>
        ClientLoginFlow.ExchangeAsync<LoginResponse>(http, code);
}
