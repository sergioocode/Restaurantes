using Microsoft.AspNetCore.SignalR.Client;
using Restaurantes.Clients.Commander.Pwa.Api;
using Restaurantes.Clients.Commander.Pwa.Models;

namespace Restaurantes.Clients.Commander.Pwa.Realtime;

public sealed class OrderRealtimeClient(HttpClient http, CommanderApi api) : IAsyncDisposable
{
    private HubConnection? connection;
    private Guid restaurantId;

    public event Func<OrderRealtimeNotification, Task>? OrderUpdated;

    public event Func<Guid, Task>? DiningTableUpdated;

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
                    new Uri(http.BaseAddress!, "/hubs/orders"),
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
            connection.On<OrderRealtimeNotification>("OrderUpdated", NotifyAsync);
            connection.On<Guid>("DiningTableUpdated", NotifyDiningTableUpdatedAsync);
            connection.Reconnected += async _ =>
            {
                if (restaurantId != Guid.Empty)
                {
                    await connection.InvokeAsync("JoinCommander", restaurantId);
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
            await connection.InvokeAsync("LeaveCommander", restaurantId);
        }
        restaurantId = newRestaurantId;
        await connection.InvokeAsync("JoinCommander", restaurantId);
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

    public Task NotifyDiningTableUpdatedAsync()
    {
        return connection is { State: HubConnectionState.Connected } && restaurantId != Guid.Empty
            ? connection.InvokeAsync("NotifyDiningTableUpdated", restaurantId)
            : Task.CompletedTask;
    }

    private Task NotifyAsync(OrderRealtimeNotification notification)
    {
        return notification.RestaurantId == restaurantId
            ? Restaurantes.Clients.Shared.Operations.AsyncEventDispatcher.InvokeAsync(
                OrderUpdated,
                notification
            )
            : Task.CompletedTask;
    }

    private Task NotifyDiningTableUpdatedAsync(Guid changedRestaurantId)
    {
        return changedRestaurantId == restaurantId
            ? Restaurantes.Clients.Shared.Operations.AsyncEventDispatcher.InvokeAsync(
                DiningTableUpdated,
                changedRestaurantId
            )
            : Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return connection?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
