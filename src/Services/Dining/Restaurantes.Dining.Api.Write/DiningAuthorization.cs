using System.Security.Claims;
using Restaurantes.Dining.Application;
using Restaurantes.Security;

namespace Restaurantes.Dining.Api.Write;

public sealed class DiningAuthorization : IDiningAuthorization
{
    public bool CanAccessRestaurant(
        ClaimsPrincipal principal,
        Guid restaurantId,
        DiningPermission permission
    )
    {
        string requiredPermission = permission switch
        {
            DiningPermission.TablesRead => RestaurantPermissions.TablesRead,
            DiningPermission.TablesManage => RestaurantPermissions.TablesManage,
            DiningPermission.OrdersCreate => RestaurantPermissions.OrdersCreate,
            DiningPermission.TablesRelease => RestaurantPermissions.TablesRelease,
            DiningPermission.PaymentsCapture => RestaurantPermissions.PaymentsCapture,
            _ => throw new ArgumentOutOfRangeException(nameof(permission)),
        };
        return principal.CanAccessRestaurant(restaurantId, requiredPermission);
    }
}
