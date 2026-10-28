using Microsoft.AspNetCore.SignalR;

namespace Restaurantes.Catalog.Api.Realtime;

public sealed class CatalogNotifications(
    IHubContext<CatalogHub, ICatalogRealtimeClient> hub,
    ILogger<CatalogNotifications> logger
)
{
    public async Task CatalogChanged(Guid restaurantId)
    {
        try
        {
            if (restaurantId == Guid.Empty)
                await hub.Clients.All.CatalogChanged(restaurantId);
            else
                await hub
                    .Clients.Group(CatalogHub.RestaurantGroup(restaurantId))
                    .CatalogChanged(restaurantId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not notify restaurant clients of catalog change for {RestaurantId}",
                restaurantId
            );
        }
    }
}
