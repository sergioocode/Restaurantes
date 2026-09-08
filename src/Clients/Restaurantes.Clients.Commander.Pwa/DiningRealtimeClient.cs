using Microsoft.AspNetCore.SignalR.Client;

namespace Restaurantes.Clients.Commander.Pwa;

public sealed record DiningTableChangedNotification(
    Guid RestaurantId,
    Guid TableId,
    string Status,
    Guid? ActiveSessionId,
    string Source,
    DateTime OccurredAtUtc
);

public sealed class DiningRealtimeClient(HttpClient http, CommanderApi api) : IAsyncDisposable
{
    private HubConnection? connection;
    private Guid restaurantId;

    public event Func<DiningTableChangedNotification, Task>? TableChanged;

    public async Task ConnectAsync(Guid newRestaurantId)
    {
        if (newRestaurantId == Guid.Empty || string.IsNullOrWhiteSpace(api.AccessToken))
        {
            return;
        }

        if (connection is null)
        {
            connection = new HubConnectionBuilder()
                .WithUrl(
                    new Uri(http.BaseAddress!, "/hubs/dining"),
                    options =>
                        options.AccessTokenProvider = () =>
                            Task.FromResult<string?>(api.AccessToken)
                )
                .WithAutomaticReconnect([
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                ])
                .Build();
            connection.On<DiningTableChangedNotification>("TableChanged", NotifyAsync);
            connection.Reconnected += async _ =>
            {
                if (restaurantId != Guid.Empty)
                {
                    await connection.InvokeAsync("JoinRestaurant", restaurantId);
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
            await connection.InvokeAsync("LeaveRestaurant", restaurantId);
        }
        restaurantId = newRestaurantId;
        await connection.InvokeAsync("JoinRestaurant", restaurantId);
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

    private async Task NotifyAsync(DiningTableChangedNotification notification)
    {
        if (notification.RestaurantId != restaurantId || TableChanged is null)
        {
            return;
        }
        foreach (
            Func<DiningTableChangedNotification, Task> handler in TableChanged.GetInvocationList()
        )
        {
            await handler(notification);
        }
    }

    public ValueTask DisposeAsync()
    {
        return connection?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
