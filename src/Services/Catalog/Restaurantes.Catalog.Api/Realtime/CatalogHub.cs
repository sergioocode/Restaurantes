using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Restaurantes.Security;

namespace Restaurantes.Catalog.Api.Realtime;

public interface ICatalogRealtimeClient
{
    Task MenuChanged(Guid restaurantId);
}

[Authorize]
public sealed class CatalogHub : Hub<ICatalogRealtimeClient>
{
    public Task JoinRestaurant(Guid restaurantId)
    {
        return restaurantId == Guid.Empty ? throw new HubException("RestaurantId is required.")
            : Context.User is not { } user
            || !user.CanAccessRestaurant(restaurantId, RestaurantPermissions.PosUse)
                ? throw new HubException("The user cannot access this restaurant's menu.")
            : Groups.AddToGroupAsync(Context.ConnectionId, RestaurantGroup(restaurantId));
    }

    public Task LeaveRestaurant(Guid restaurantId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RestaurantGroup(restaurantId));

    public static string RestaurantGroup(Guid restaurantId) =>
        $"restaurant:{restaurantId:N}:catalog";
}
