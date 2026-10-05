using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Contracts.Requests;
using Restaurantes.Payments.Contracts.Responses;
using Restaurantes.Security;

namespace Restaurantes.Payments.Api.Controllers;

[ApiController, Authorize, Route("api/payments/restaurants/{restaurantId:guid}")]
public sealed class PaymentsReadController(IPaymentStatusQuery query) : ControllerBase
{
    [HttpPost("order-statuses")]
    public async Task<ActionResult<IReadOnlyList<OrderPaymentStatusResponse>>> OrderStatuses(
        Guid restaurantId,
        OrderPaymentStatusesRequest request,
        CancellationToken ct
    )
    {
        return !User.CanAccessRestaurant(restaurantId, RestaurantPermissions.CashRegisterManage)
            ? Forbid()
            : Ok(await query.ListAsync(restaurantId, request.OrderIds, ct));
    }
}
