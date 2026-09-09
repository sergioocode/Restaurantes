using System.Security.Claims;

namespace Restaurantes.Dining.Application;

public enum DiningPermission
{
    TablesRead,
    TablesManage,
    OrdersCreate,
    TablesRelease,
    PaymentsCapture,
}

public interface IDiningAuthorization
{
    bool CanAccessRestaurant(
        ClaimsPrincipal principal,
        Guid restaurantId,
        DiningPermission permission
    );
}
