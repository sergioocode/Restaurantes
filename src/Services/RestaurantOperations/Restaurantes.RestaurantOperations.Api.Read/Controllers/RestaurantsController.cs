using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts.Responses;
using Restaurantes.Security;

namespace Restaurantes.RestaurantOperations.Api.Read.Controllers;

[ApiController]
[Route("api/restaurant-operations/restaurants")]
public sealed class RestaurantsController(IRestaurantReadStore store) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RestaurantResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RestaurantResponse>>> List(
        CancellationToken cancellationToken
    )
    {
        return Ok(await store.ListAsync(cancellationToken));
    }

    [Authorize]
    [HttpGet("accessible/kds")]
    [ProducesResponseType<IReadOnlyList<RestaurantResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RestaurantResponse>>> ListKdsAccessible(
        CancellationToken cancellationToken
    )
    {
        return await ListAccessible(RestaurantPermissions.KdsUse, cancellationToken);
    }

    [Authorize]
    [HttpGet("accessible/commander")]
    [ProducesResponseType<IReadOnlyList<RestaurantResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RestaurantResponse>>> ListCommanderAccessible(
        CancellationToken cancellationToken
    )
    {
        return await ListAccessible(RestaurantPermissions.CommanderUse, cancellationToken);
    }

    [Authorize]
    [HttpGet("accessible/pos")]
    [ProducesResponseType<IReadOnlyList<RestaurantResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RestaurantResponse>>> ListPosAccessible(
        CancellationToken cancellationToken
    )
    {
        return await ListAccessible(RestaurantPermissions.PosUse, cancellationToken);
    }

    private async Task<ActionResult<IReadOnlyList<RestaurantResponse>>> ListAccessible(
        string permission,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<RestaurantResponse> restaurants = await store.ListAsync(cancellationToken);
        return Ok(
            restaurants
                .Where(restaurant =>
                    restaurant.IsActive && User.CanAccessRestaurant(restaurant.Id, permission)
                )
                .OrderBy(restaurant => restaurant.Name)
                .ToArray()
        );
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<RestaurantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RestaurantResponse>> Get(
        Guid id,
        CancellationToken cancellationToken
    )
    {
        RestaurantResponse? restaurant = await store.FindAsync(id, cancellationToken);
        return restaurant is null ? NotFound() : Ok(restaurant);
    }
}
