using Microsoft.AspNetCore.SignalR.Client;

namespace Restaurantes.Clients.Shared.Operations;

public abstract class RestaurantOrderRealtimeClient<TNotification>(
    HttpClient http,
    Func<string?> accessToken,
    string groupName,
    Func<TNotification, Guid> notificationRestaurantId
) : IAsyncDisposable
{
    private HubConnection? connection;
    private Guid restaurantId;

    public event Func<TNotification, Task>? OrderUpdated;
    public event Func<Guid, Task>? DiningTableUpdated;

    public async Task ConnectAsync(Guid newRestaurantId)
    {
        if (newRestaurantId == Guid.Empty || string.IsNullOrWhiteSpace(accessToken()))
        {
            return;
        }

        if (connection is null)
        {
            connection = new HubConnectionBuilder()
                .WithUrl(
                    new Uri(http.BaseAddress!, "/hubs/orders"),
                    options => options.AccessTokenProvider = () => Task.FromResult(accessToken())
                )
                .WithAutomaticReconnect([
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                ])
                .Build();
            connection.On<TNotification>("OrderUpdated", NotifyAsync);
            connection.On<Guid>("DiningTableUpdated", NotifyDiningTableUpdatedAsync);
            connection.Reconnected += async _ =>
            {
                if (restaurantId != Guid.Empty)
                {
                    await connection.InvokeAsync($"Join{groupName}", restaurantId);
                }
            };
            await connection.StartAsync();
        }

        if (restaurantId == newRestaurantId)
        {
            return;
        }
        if (restaurantId != Guid.Empty)
        {
            await connection.InvokeAsync($"Leave{groupName}", restaurantId);
        }
        restaurantId = newRestaurantId;
        await connection.InvokeAsync($"Join{groupName}", restaurantId);
    }

    public async Task DisconnectAsync()
    {
        if (connection is null)
        {
            return;
        }
        await connection.DisposeAsync();
        connection = null;
        restaurantId = Guid.Empty;
    }

    public Task NotifyDiningTableUpdatedAsync() =>
        connection is { State: HubConnectionState.Connected } && restaurantId != Guid.Empty
            ? connection.InvokeAsync("NotifyDiningTableUpdated", restaurantId)
            : Task.CompletedTask;

    private Task NotifyAsync(TNotification notification) =>
        AsyncEventDispatcher.InvokeForRestaurantAsync(
            OrderUpdated,
            notification,
            restaurantId,
            notificationRestaurantId(notification)
        );

    private Task NotifyDiningTableUpdatedAsync(Guid changedRestaurantId) =>
        AsyncEventDispatcher.InvokeForRestaurantAsync(
            DiningTableUpdated,
            changedRestaurantId,
            restaurantId,
            changedRestaurantId
        );

    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
        {
            await connection.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
