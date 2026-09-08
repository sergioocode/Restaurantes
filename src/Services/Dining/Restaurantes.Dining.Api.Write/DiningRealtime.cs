using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Restaurantes.Security;

namespace Restaurantes.Dining.Api.Write;

public sealed record DiningTableChanged(
    Guid RestaurantId,
    Guid TableId,
    string Status,
    Guid? ActiveSessionId,
    string Source,
    DateTime OccurredAtUtc
);

public interface IDiningRealtimeClient
{
    Task TableChanged(DiningTableChanged notification);
}

[Authorize]
public sealed class DiningHub : Hub<IDiningRealtimeClient>
{
    public Task JoinRestaurant(Guid restaurantId)
    {
        System.Security.Claims.ClaimsPrincipal? user = Context.User;
        return
            user is null
            || !user.CanAccessRestaurant(restaurantId, RestaurantPermissions.TablesRead)
            ? throw new HubException("The user cannot access this restaurant's tables.")
            : Groups.AddToGroupAsync(Context.ConnectionId, RestaurantGroup(restaurantId));
    }

    public Task LeaveRestaurant(Guid restaurantId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, RestaurantGroup(restaurantId));
    }

    public static string RestaurantGroup(Guid restaurantId)
    {
        return $"restaurant:{restaurantId:N}:dining";
    }
}
