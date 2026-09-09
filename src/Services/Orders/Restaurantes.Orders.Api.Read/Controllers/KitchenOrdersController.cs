using Microsoft.AspNetCore.Mvc;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Security;

namespace Restaurantes.Orders.Api.Read.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class KitchenOrdersController(IKitchenOrderReadStore store) : ControllerBase
{
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
