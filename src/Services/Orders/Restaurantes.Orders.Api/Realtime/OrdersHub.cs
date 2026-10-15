using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Restaurantes.Security;

namespace Restaurantes.Orders.Api.Realtime;

[Authorize]
public sealed class OrdersHub : Hub<IKdsClient>
{
    public Task JoinCommander(Guid restaurantId)
    {
        return Join(restaurantId, RestaurantPermissions.CommanderUse, CommanderGroup(restaurantId));
    }

    public Task LeaveCommander(Guid restaurantId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, CommanderGroup(restaurantId));
    }

    public Task JoinPos(Guid restaurantId)
    {
        return Join(restaurantId, RestaurantPermissions.PosUse, PosGroup(restaurantId));
    }

    public Task LeavePos(Guid restaurantId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, PosGroup(restaurantId));
    }

    public Task NotifyDiningTableUpdated(Guid restaurantId)
    {
        if (
            restaurantId == Guid.Empty
            || Context.User is not { } user
            || (
                !user.CanAccessRestaurant(restaurantId, RestaurantPermissions.CommanderUse)
                && !user.CanAccessRestaurant(restaurantId, RestaurantPermissions.PosUse)
            )
        )
        {
            throw new HubException("The user cannot notify dining table changes.");
        }

        return Task.WhenAll(
            Clients.Group(CommanderGroup(restaurantId)).DiningTableUpdated(restaurantId),
            Clients.Group(PosGroup(restaurantId)).DiningTableUpdated(restaurantId)
        );
    }

    private Task Join(Guid restaurantId, string permission, string group)
    {
        return restaurantId == Guid.Empty ? throw new HubException("RestaurantId is required.")
            : Context.User is not { } user || !user.CanAccessRestaurant(restaurantId, permission)
                ? throw new HubException("The user cannot access this restaurant order stream.")
            : Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public static string CommanderGroup(Guid restaurantId)
    {
        return $"restaurant:{restaurantId:N}:commander-orders";
    }

    public static string PosGroup(Guid restaurantId)
    {
        return $"restaurant:{restaurantId:N}:pos-orders";
    }
}
