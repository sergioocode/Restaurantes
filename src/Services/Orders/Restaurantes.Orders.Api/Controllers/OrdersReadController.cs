using Microsoft.AspNetCore.Mvc;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Security;

namespace Restaurantes.Orders.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersReadController(
    IKitchenOrderReadStore store,
    IOrderSettlementQuery settlements
) : ControllerBase
{
    [HttpGet("restaurants/{restaurantId:guid}/cash-close-orders")]
    public async Task<ActionResult<IReadOnlyList<CashCloseOrderResponse>>> CashCloseOrders(
        Guid restaurantId,
        CancellationToken ct,
        [FromQuery] int offset = 0
    )
    {
        if (!User.CanAccessRestaurant(restaurantId, RestaurantPermissions.CashRegisterManage))
        {
            return Forbid();
        }
        if (offset < 0)
        {
            return BadRequest("offset must be non-negative.");
        }
        return Ok(await settlements.ListAsync(restaurantId, offset, ct));
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<OrderResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> List(
        [FromQuery] Guid restaurantId,
        CancellationToken cancellationToken,
        [FromQuery] string? stationCode = null
    )
    {
        return restaurantId == Guid.Empty
                ? (ActionResult<IReadOnlyList<OrderResponse>>)
                    BadRequest("restaurantId is required.")
            : !User.CanAccessRestaurant(restaurantId, RestaurantPermissions.KdsUse)
            && !User.CanAccessRestaurant(restaurantId, RestaurantPermissions.OrdersManage)
                ? (ActionResult<IReadOnlyList<OrderResponse>>)Forbid()
            : (ActionResult<IReadOnlyList<OrderResponse>>)
                Ok(await store.ListAsync(restaurantId, stationCode, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        OrderResponse? order = await store.FindAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }
}
