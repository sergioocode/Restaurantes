using System.Security.Claims;
using Restaurantes.Security;

namespace Restaurantes.Orders.Api.Security;

public static class OrderClientAccess
{
    public static string? PermissionForSource(string source)
    {
        return source switch
        {
            "Comandero" => RestaurantPermissions.CommanderUse,
            "Pos" or "CustomerQr" => RestaurantPermissions.PosUse,
            _ => null,
        };
    }

    public static bool CanManageOrder(this ClaimsPrincipal user, Guid restaurantId, string source)
    {
        string? clientPermission = PermissionForSource(source);
        return clientPermission is not null
            && user.CanAccessRestaurant(restaurantId, RestaurantPermissions.OrdersManage)
            && user.CanAccessRestaurant(restaurantId, clientPermission);
    }
}
