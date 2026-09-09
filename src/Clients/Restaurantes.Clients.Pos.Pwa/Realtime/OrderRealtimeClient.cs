using Microsoft.AspNetCore.SignalR.Client;
using Restaurantes.Clients.Pos.Pwa.Api;
using Restaurantes.Clients.Pos.Pwa.Models;

namespace Restaurantes.Clients.Pos.Pwa.Realtime;

public sealed class OrderRealtimeClient(HttpClient http, PosApi api) : IAsyncDisposable
{
    private HubConnection? connection;
    private Guid restaurantId;

    public event Func<OrderRealtimeNotification, Task>? OrderUpdated;

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
                    new Uri(http.BaseAddress!, "/hubs/kds"),
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

    private async Task NotifyAsync(OrderRealtimeNotification notification)
    {
        if (notification.RestaurantId != restaurantId || OrderUpdated is null)
        {
            return;
        }
        foreach (Func<OrderRealtimeNotification, Task> handler in OrderUpdated.GetInvocationList())
        {
            await handler(notification);
        }
    }

    public ValueTask DisposeAsync()
    {
        return connection?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
