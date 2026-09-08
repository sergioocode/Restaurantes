using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts;
using Restaurantes.Security;

namespace Restaurantes.RestaurantOperations.Api.Write.Controllers;

[ApiController, Authorize]
[Route("api/restaurant-operations/restaurants")]
public sealed class RestaurantsController(RestaurantCommandService commandService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RestaurantResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RestaurantResponse>> Create(
        CreateRestaurantRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!User.IsInRole("Admin"))
        {
            return Forbid();
        }

        RestaurantResponse? restaurant = await commandService.CreateAsync(
            request,
            cancellationToken
        );
        return restaurant is null
            ? (ActionResult<RestaurantResponse>)
                Conflict(
                    new ProblemDetails
                    {
                        Title = "Restaurant code already exists",
                        Detail = $"A restaurant with code '{request.Code}' already exists.",
                        Status = StatusCodes.Status409Conflict,
                    }
                )
            : (ActionResult<RestaurantResponse>)
                Accepted($"/api/restaurant-operations/restaurants/{restaurant.Id}", restaurant);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<RestaurantResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RestaurantResponse>> Update(
        Guid id,
        UpdateRestaurantRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!User.CanAccessRestaurant(id, RestaurantPermissions.TablesManage))
        {
            return Forbid();
        }

        RestaurantResponse? restaurant = await commandService.UpdateAsync(
            id,
            request,
            cancellationToken
        );
        return restaurant is null
            ? NotFound()
            : Accepted($"/api/restaurant-operations/restaurants/{restaurant.Id}", restaurant);
    }
}
