using Restaurantes.Clients.Commander.Pwa.Api;
using Restaurantes.Clients.Commander.Pwa.Models;
using Restaurantes.Clients.Shared.Operations;

namespace Restaurantes.Clients.Commander.Pwa.Realtime;

public sealed class OrderRealtimeClient(
    HttpClient http,
    CommanderApi api,
    Func<HttpMessageHandler>? messageHandlerFactory = null
)
    : RestaurantOrderRealtimeClient<OrderRealtimeNotification>(
        http,
        () => api.AccessToken,
        "Commander",
        notification => notification.RestaurantId,
        messageHandlerFactory
    );
