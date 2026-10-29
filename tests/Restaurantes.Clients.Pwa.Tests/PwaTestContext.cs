using Bunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Restaurantes.Clients.Shared.Operations;

namespace Restaurantes.Clients.Pwa.Tests;

public abstract class PwaTestContext : BunitContext
{
    private static readonly Lazy<Task<Uri>> CatalogHubAddress = new(StartCatalogHubAsync);

    protected PwaTestContext()
    {
        Services.AddSingleton(CreateDiningRealtime());
        Services.AddSingleton(CreateCatalogConfigurationRealtime());
        Services.AddSingleton(_ => new CustomerQrCatalogRealtimeClient(
            new HttpClient { BaseAddress = CatalogHubAddress.Value.GetAwaiter().GetResult() }
        ));
    }

    public static DiningRealtimeClient CreateDiningRealtime() =>
        new(new HttpClient { BaseAddress = new Uri("https://test.local") }, () => null);

    public static CatalogConfigurationRealtimeClient CreateCatalogConfigurationRealtime() =>
        new(new HttpClient { BaseAddress = new Uri("https://test.local") }, () => null);

    private static async Task<Uri> StartCatalogHubAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSignalR();

        WebApplication application = builder.Build();
        application.MapHub<PwaTestCatalogHub>("/hubs/catalog");
        await application.StartAsync();

        string address = application
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        return new Uri($"{address.TrimEnd('/')}/");
    }
}

public sealed class PwaTestCatalogHub : Hub
{
    public Task JoinPublicRestaurant(Guid restaurantId) => Task.CompletedTask;
}
