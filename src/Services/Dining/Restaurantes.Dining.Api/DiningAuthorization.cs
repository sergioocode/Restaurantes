using System.Security.Claims;
using Restaurantes.Dining.Application;
using Restaurantes.Security;

namespace Restaurantes.Dining.Api;

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
            DiningPermission.KdsZonesRead => RestaurantPermissions.KdsUse,
            DiningPermission.TablesManage => RestaurantPermissions.TablesManage,
            DiningPermission.OrdersCreate => RestaurantPermissions.OrdersCreate,
            DiningPermission.TablesRelease => RestaurantPermissions.TablesRelease,
            DiningPermission.TablesReleasePaid => RestaurantPermissions.TablesReleasePaid,
            DiningPermission.PaymentsCapture => RestaurantPermissions.PaymentsCapture,
            DiningPermission.PaymentsCaptureCard => RestaurantPermissions.PaymentsCaptureCard,
            DiningPermission.DiscountsApply => RestaurantPermissions.DiscountsApply,
            _ => throw new ArgumentOutOfRangeException(nameof(permission)),
        };
        return principal.CanAccessRestaurant(restaurantId, requiredPermission);
    }
}
