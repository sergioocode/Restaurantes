using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Clients.Commander.Pwa.Api;
using Restaurantes.Clients.Pos.Pwa.Api;
using Xunit;
using CommanderRealtime = Restaurantes.Clients.Commander.Pwa.Realtime.OrderRealtimeClient;
using PosNotification = Restaurantes.Clients.Pos.Pwa.Models.OrderRealtimeNotification;
using PosRealtime = Restaurantes.Clients.Pos.Pwa.Realtime.OrderRealtimeClient;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class OrderRealtimeConnectionTests
{
    [Fact]
    public async Task Pos_connection_joins_switches_and_leaves_restaurant_groups()
    {
        await using WebApplication app = await StartHubAsync();
        HubCalls calls = app.Services.GetRequiredService<HubCalls>();
        using HttpClient http = new() { BaseAddress = new Uri(app.Urls.Single()) };
        PosApi api = new(http);
        await using PosRealtime client = new(http, api);
        Guid firstRestaurantId = Guid.NewGuid();
        Guid secondRestaurantId = Guid.NewGuid();
        TaskCompletionSource<Guid> receivedOrder = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        client.OrderUpdated += notification =>
        {
            receivedOrder.TrySetResult(notification.RestaurantId);
            return Task.CompletedTask;
        };

        await client.ConnectAsync(Guid.Empty);
        await client.ConnectAsync(firstRestaurantId);
        Assert.Empty(calls.Items);

        api.AccessToken = "test-token";
        await client.ConnectAsync(firstRestaurantId);
        await app
            .Services.GetRequiredService<IHubContext<RecordingOrderHub>>()
            .Clients.All.SendAsync(
                "OrderUpdated",
                new PosNotification(
                    Guid.NewGuid(),
                    firstRestaurantId,
                    "Submitted",
                    1,
                    DateTime.UtcNow,
                    []
                ),
                TestContext.Current.CancellationToken
            );
        Assert.Equal(
            firstRestaurantId,
            await receivedOrder.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken
            )
        );
        await client.ConnectAsync(firstRestaurantId);
        await client.NotifyDiningTableUpdatedAsync();
        await client.ConnectAsync(secondRestaurantId);
        await client.DisconnectAsync();
        await client.NotifyDiningTableUpdatedAsync();

        Assert.Equal(
            [
                $"JoinPos:{firstRestaurantId}",
                $"NotifyDiningTableUpdated:{firstRestaurantId}",
                $"LeavePos:{firstRestaurantId}",
                $"JoinPos:{secondRestaurantId}",
            ],
            calls.Items
        );
    }

    [Fact]
    public async Task Commander_connection_uses_its_own_group_names()
    {
        await using WebApplication app = await StartHubAsync();
        HubCalls calls = app.Services.GetRequiredService<HubCalls>();
        using HttpClient http = new() { BaseAddress = new Uri(app.Urls.Single()) };
        CommanderApi api = new(http) { AccessToken = "test-token" };
        await using CommanderRealtime client = new(http, api);
        Guid restaurantId = Guid.NewGuid();

        await client.ConnectAsync(restaurantId);
        await client.DisconnectAsync();

        Assert.Equal([$"JoinCommander:{restaurantId}"], calls.Items);
    }

    private static async Task<WebApplication> StartHubAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<HubCalls>();
        WebApplication app = builder.Build();
        app.MapHub<RecordingOrderHub>("/hubs/orders");
        await app.StartAsync();
        return app;
    }

    public sealed class HubCalls
    {
        private readonly ConcurrentQueue<string> calls = new();
        public string[] Items => calls.ToArray();

        public void Add(string call) => calls.Enqueue(call);
    }

    public sealed class RecordingOrderHub(HubCalls calls) : Hub
    {
        public Task JoinPos(Guid restaurantId)
        {
            calls.Add($"JoinPos:{restaurantId}");
            return Task.CompletedTask;
        }

        public Task LeavePos(Guid restaurantId)
        {
            calls.Add($"LeavePos:{restaurantId}");
            return Task.CompletedTask;
        }

        public Task JoinCommander(Guid restaurantId)
        {
            calls.Add($"JoinCommander:{restaurantId}");
            return Task.CompletedTask;
        }

        public Task LeaveCommander(Guid restaurantId)
        {
            calls.Add($"LeaveCommander:{restaurantId}");
            return Task.CompletedTask;
        }

        public Task NotifyDiningTableUpdated(Guid restaurantId)
        {
            calls.Add($"NotifyDiningTableUpdated:{restaurantId}");
            return Task.CompletedTask;
        }
    }
}
