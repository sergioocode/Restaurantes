using Restaurantes.Clients.Backoffice.Pwa.Api;
using Restaurantes.Clients.Shared.Operations;

namespace Restaurantes.Clients.Backoffice.Pwa.Realtime;

public sealed record TableOrderNotification(
    Guid OrderId,
    Guid RestaurantId,
    string Status,
    int Version,
    DateTime OccurredAtUtc,
    IReadOnlyList<string> StationCodes
);

public sealed class TablesRealtimeClient(HttpClient http, BackofficeApi api)
    : RestaurantOrderRealtimeClient<TableOrderNotification>(
        http,
        () => api.AccessToken,
        "Tables",
        notification => notification.RestaurantId
    ),
        ITablesRealtimeClient;
