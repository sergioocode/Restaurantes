using Microsoft.AspNetCore.SignalR.Client;
using Restaurantes.Clients.Pos.Pwa.Api;
using Restaurantes.Clients.Shared.Operations;

namespace Restaurantes.Clients.Pos.Pwa.Realtime;

public sealed class CatalogRealtimeClient(HttpClient http, PosApi api) : IAsyncDisposable
{
    private HubConnection? connection;
    private Guid restaurantId;

    public event Func<Guid, Task>? MenuChanged;

    public async Task ConnectAsync(Guid newRestaurantId)
    {
        if (newRestaurantId == Guid.Empty || string.IsNullOrWhiteSpace(api.AccessToken))
            return;

        if (connection is null)
        {
            connection = new HubConnectionBuilder()
                .WithUrl(
                    new Uri(http.BaseAddress!, "/hubs/catalog"),
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
            connection.On<Guid>("MenuChanged", NotifyAsync);
            connection.Reconnected += async _ =>
            {
                if (restaurantId != Guid.Empty)
                {
                    await connection.InvokeAsync("JoinRestaurant", restaurantId);
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
        await connection.InvokeAsync("JoinRestaurant", restaurantId);
    }

    public async Task DisconnectAsync()
    {
        if (connection is null)
            return;
        await connection.DisposeAsync();
        connection = null;
        restaurantId = Guid.Empty;
    }

    private Task NotifyAsync(Guid changedRestaurantId)
    {
        if (changedRestaurantId != Guid.Empty && changedRestaurantId != restaurantId)
            return Task.CompletedTask;
        return AsyncEventDispatcher.InvokeAsync(MenuChanged, changedRestaurantId);
    }

    public ValueTask DisposeAsync() => connection?.DisposeAsync() ?? ValueTask.CompletedTask;
}
