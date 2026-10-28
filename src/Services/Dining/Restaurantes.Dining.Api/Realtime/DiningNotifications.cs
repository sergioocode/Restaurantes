using Microsoft.AspNetCore.SignalR;

namespace Restaurantes.Dining.Api.Realtime;

public sealed class DiningNotifications(
    IHubContext<DiningHub, IDiningRealtimeClient> hub,
    ILogger<DiningNotifications> logger
)
{
    public async Task DiningChanged(Guid restaurantId)
    {
        if (restaurantId == Guid.Empty)
            return;

        try
        {
            await hub
                .Clients.Group(DiningHub.RestaurantGroup(restaurantId))
                .DiningChanged(restaurantId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not notify restaurant clients of dining state change for {RestaurantId}",
                restaurantId
            );
        }
    }
}
