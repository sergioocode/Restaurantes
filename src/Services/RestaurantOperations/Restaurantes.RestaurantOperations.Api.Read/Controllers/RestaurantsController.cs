using Microsoft.AspNetCore.Mvc;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts;

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
