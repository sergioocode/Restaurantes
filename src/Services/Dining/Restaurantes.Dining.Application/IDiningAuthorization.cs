using System.Security.Claims;

namespace Restaurantes.Dining.Application;

public enum DiningPermission
{
    TablesRead,
    KdsZonesRead,
    TablesManage,
    OrdersCreate,
    TablesRelease,
    TablesReleasePaid,
    PaymentsCapture,
    PaymentsCaptureCard,
    DiscountsApply,
}

public interface IDiningAuthorization
{
    bool CanAccessRestaurant(
        ClaimsPrincipal principal,
        Guid restaurantId,
        DiningPermission permission
    );
}
