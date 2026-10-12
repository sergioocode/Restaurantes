using Microsoft.AspNetCore.SignalR;

namespace Restaurantes.Catalog.Api.Realtime;

public sealed class CatalogNotifications(
    IHubContext<CatalogHub, ICatalogRealtimeClient> hub,
    ILogger<CatalogNotifications> logger
)
{
    public async Task MenuChanged(Guid restaurantId)
    {
        try
        {
            if (restaurantId == Guid.Empty)
                await hub.Clients.All.MenuChanged(restaurantId);
            else
                await hub
                    .Clients.Group(CatalogHub.RestaurantGroup(restaurantId))
                    .MenuChanged(restaurantId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not notify TPVs of menu change for {RestaurantId}",
                restaurantId
            );
        }
    }
}
