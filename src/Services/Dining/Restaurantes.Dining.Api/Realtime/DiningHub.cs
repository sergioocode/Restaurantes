using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Restaurantes.Security;

namespace Restaurantes.Dining.Api.Realtime;

public interface IDiningRealtimeClient
{
    Task DiningChanged(Guid restaurantId);
}

[Authorize]
public sealed class DiningHub : Hub<IDiningRealtimeClient>
{
    public Task JoinRestaurant(Guid restaurantId)
    {
        return restaurantId == Guid.Empty ? throw new HubException("RestaurantId is required.")
            : Context.User is not { } user || !CanAccessDining(user, restaurantId)
                ? throw new HubException("The user cannot access this restaurant's dining setup.")
            : Groups.AddToGroupAsync(Context.ConnectionId, RestaurantGroup(restaurantId));
    }

    public Task LeaveRestaurant(Guid restaurantId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RestaurantGroup(restaurantId));

    public static string RestaurantGroup(Guid restaurantId) =>
        $"restaurant:{restaurantId:N}:dining-configuration";

    private static bool CanAccessDining(
        System.Security.Claims.ClaimsPrincipal user,
        Guid restaurantId
    ) =>
        user.CanAccessRestaurant(restaurantId, RestaurantPermissions.TablesRead)
        || user.CanAccessRestaurant(restaurantId, RestaurantPermissions.TablesManage)
        || user.CanAccessRestaurant(restaurantId, RestaurantPermissions.PosUse)
        || user.CanAccessRestaurant(restaurantId, RestaurantPermissions.CommanderUse)
        || user.CanAccessRestaurant(restaurantId, RestaurantPermissions.BackofficeAccess)
        || user.CanAccessRestaurant(restaurantId, RestaurantPermissions.KdsUse);
}
