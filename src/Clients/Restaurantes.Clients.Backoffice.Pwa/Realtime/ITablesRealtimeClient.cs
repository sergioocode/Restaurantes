namespace Restaurantes.Clients.Backoffice.Pwa.Realtime;

public interface ITablesRealtimeClient : IAsyncDisposable
{
    event Func<Guid, Task>? DiningTableUpdated;

    Task ConnectAsync(Guid restaurantId);

    Task DisconnectAsync();
}
