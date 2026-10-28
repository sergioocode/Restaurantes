using Microsoft.AspNetCore.SignalR.Client;

namespace Restaurantes.Clients.Shared.Operations;

public class RestaurantConfigurationRealtimeClient(
    HttpClient http,
    Func<string?> accessToken,
    string hubPath,
    string eventName,
    string joinMethod = "JoinRestaurant",
    bool allowAnonymous = false,
    Func<HttpMessageHandler>? messageHandlerFactory = null
) : IAsyncDisposable
{
    private HubConnection? connection;
    private Guid restaurantId;

    public event Func<Guid, Task>? Changed;

    public async Task ConnectAsync(Guid newRestaurantId)
    {
        if (
            newRestaurantId == Guid.Empty
            || (!allowAnonymous && string.IsNullOrWhiteSpace(accessToken()))
        )
            return;

        if (connection is null)
        {
            connection = new HubConnectionBuilder()
                .WithUrl(
                    new Uri(http.BaseAddress!, hubPath),
                    options =>
                    {
                        options.AccessTokenProvider = () => Task.FromResult(accessToken());
                        if (messageHandlerFactory is not null)
                        {
                            options.HttpMessageHandlerFactory = _ => messageHandlerFactory();
                            options.Transports = Microsoft
                                .AspNetCore
                                .Http
                                .Connections
                                .HttpTransportType
                                .LongPolling;
                        }
                    }
                )
                .WithAutomaticReconnect([
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                ])
                .Build();
            connection.On<Guid>(eventName, NotifyAsync);
            connection.Reconnected += async _ =>
            {
                if (restaurantId != Guid.Empty)
                {
                    await connection.InvokeAsync(joinMethod, restaurantId);
                    await NotifyAsync(restaurantId);
                }
            };
            await connection.StartAsync();
        }

        if (restaurantId == newRestaurantId)
            return;
        if (restaurantId != Guid.Empty)
            await connection.InvokeAsync("LeaveRestaurant", restaurantId);
        restaurantId = newRestaurantId;
        await connection.InvokeAsync(joinMethod, restaurantId);
    }

    public async Task DisconnectAsync()
    {
        if (connection is null)
            return;
        await connection.DisposeAsync();
        connection = null;
        restaurantId = Guid.Empty;
    }

    private Task NotifyAsync(Guid changedRestaurantId) =>
        changedRestaurantId != Guid.Empty && changedRestaurantId != restaurantId
            ? Task.CompletedTask
            : AsyncEventDispatcher.InvokeAsync(Changed, changedRestaurantId);

    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
            await connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

public sealed class CatalogConfigurationRealtimeClient(
    HttpClient http,
    Func<string?> accessToken,
    Func<HttpMessageHandler>? messageHandlerFactory = null
)
    : RestaurantConfigurationRealtimeClient(
        http,
        accessToken,
        "/hubs/catalog",
        "CatalogChanged",
        messageHandlerFactory: messageHandlerFactory
    ) { }

public sealed class CustomerQrCatalogRealtimeClient(HttpClient http)
    : RestaurantConfigurationRealtimeClient(
        http,
        () => null,
        "/hubs/catalog",
        "CatalogChanged",
        "JoinPublicRestaurant",
        true
    ) { }

public sealed class DiningRealtimeClient(
    HttpClient http,
    Func<string?> accessToken,
    Func<HttpMessageHandler>? messageHandlerFactory = null
)
    : RestaurantConfigurationRealtimeClient(
        http,
        accessToken,
        "/hubs/dining",
        "DiningChanged",
        messageHandlerFactory: messageHandlerFactory
    ) { }
