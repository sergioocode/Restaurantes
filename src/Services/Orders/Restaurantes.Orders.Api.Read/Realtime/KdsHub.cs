using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Restaurantes.Security;

namespace Restaurantes.Orders.Api.Read.Realtime;

[Authorize]
public sealed class KdsHub : Hub<IKdsClient>
{
    public Task JoinRestaurant(Guid restaurantId)
    {
        return JoinStation(restaurantId, "CHEF");
    }

    public Task JoinStation(Guid restaurantId, string stationCode)
    {
        return restaurantId == Guid.Empty ? throw new HubException("RestaurantId is required.")
            : string.IsNullOrWhiteSpace(stationCode)
                ? throw new HubException("StationCode is required.")
            : Context.User is not { } user
            || (
                !user.CanAccessRestaurant(restaurantId, RestaurantPermissions.KdsUse)
                && !user.CanAccessRestaurant(restaurantId, RestaurantPermissions.OrdersManage)
            )
                ? throw new HubException("The user cannot access this restaurant KDS.")
            : Groups.AddToGroupAsync(Context.ConnectionId, StationGroup(restaurantId, stationCode));
    }

    public Task LeaveRestaurant(Guid restaurantId)
    {
        return LeaveStation(restaurantId, "CHEF");
    }

    public Task LeaveStation(Guid restaurantId, string stationCode)
    {
        return Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            StationGroup(restaurantId, stationCode)
        );
    }

    public static string StationGroup(Guid restaurantId, string stationCode)
    {
        return $"restaurant:{restaurantId:N}:station:{stationCode.Trim().ToUpperInvariant()}";
    }
}
