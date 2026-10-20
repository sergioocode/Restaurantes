using Restaurantes.Clients.Pos.Pwa.Api;
using Restaurantes.Clients.Pos.Pwa.Models;
using Restaurantes.Clients.Shared.Operations;

namespace Restaurantes.Clients.Pos.Pwa.Realtime;

public sealed class OrderRealtimeClient(
    HttpClient http,
    PosApi api,
    Func<HttpMessageHandler>? messageHandlerFactory = null
)
    : RestaurantOrderRealtimeClient<OrderRealtimeNotification>(
        http,
        () => api.AccessToken,
        "Pos",
        notification => notification.RestaurantId,
        messageHandlerFactory
    );
