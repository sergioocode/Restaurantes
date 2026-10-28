using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Restaurantes.Security;

namespace Restaurantes.Catalog.Api.Realtime;

public interface ICatalogRealtimeClient
{
    Task CatalogChanged(Guid restaurantId);
}

public sealed class CatalogHub : Hub<ICatalogRealtimeClient>
{
    [Authorize]
    public Task JoinRestaurant(Guid restaurantId)
    {
        return restaurantId == Guid.Empty ? throw new HubException("RestaurantId is required.")
            : Context.User is not { } user || !CanAccessCatalog(user, restaurantId)
                ? throw new HubException("The user cannot access this restaurant's menu.")
            : Groups.AddToGroupAsync(Context.ConnectionId, RestaurantGroup(restaurantId));
    }

    private static bool CanAccessCatalog(
        System.Security.Claims.ClaimsPrincipal user,
        Guid restaurantId
    ) =>
        user.CanAccessRestaurant(restaurantId, RestaurantPermissions.PosUse)
        || user.CanAccessRestaurant(restaurantId, RestaurantPermissions.CommanderUse)
        || user.CanAccessRestaurant(restaurantId, RestaurantPermissions.BackofficeAccess)
        || user.CanAccessRestaurant(restaurantId, RestaurantPermissions.KdsUse)
        || user.CanAccessRestaurant(restaurantId, RestaurantPermissions.CatalogManage);

    public Task LeaveRestaurant(Guid restaurantId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RestaurantGroup(restaurantId));

    [AllowAnonymous]
    public Task JoinPublicRestaurant(Guid restaurantId) =>
        restaurantId == Guid.Empty
            ? throw new HubException("RestaurantId is required.")
            : Groups.AddToGroupAsync(Context.ConnectionId, RestaurantGroup(restaurantId));

    public static string RestaurantGroup(Guid restaurantId) =>
        $"restaurant:{restaurantId:N}:catalog";
}
