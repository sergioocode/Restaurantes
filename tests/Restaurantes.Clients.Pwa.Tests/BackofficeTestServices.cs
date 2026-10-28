using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Clients.Backoffice.Pwa.Realtime;

namespace Restaurantes.Clients.Pwa.Tests;

internal static class BackofficeTestServices
{
    public static void AddTablesRealtime(BunitContext context)
    {
        context.Services.AddSingleton(CreateTablesRealtime());
    }

    public static ITablesRealtimeClient CreateTablesRealtime() => new StubTablesRealtimeClient();

    private sealed class StubTablesRealtimeClient : ITablesRealtimeClient
    {
        public event Func<Guid, Task>? DiningTableUpdated
        {
            add { }
            remove { }
        }

        public Task ConnectAsync(Guid restaurantId) => Task.CompletedTask;

        public Task DisconnectAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
