using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts.Requests;
using Restaurantes.RestaurantOperations.Contracts.Responses;
using Restaurantes.Security;

namespace Restaurantes.RestaurantOperations.Api.Controllers;

[ApiController, Authorize]
[Route("api/restaurant-operations/restaurants")]
public sealed class RestaurantsWriteController(RestaurantCommandService commandService)
    : ControllerBase
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
                        Title = "Restaurant could not be created",
                        Detail =
                            $"The code '{request.Code}' may already exist, or the selected company is unavailable.",
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

    [HttpPut("{id:guid}/company")]
    [ProducesResponseType<RestaurantResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RestaurantResponse>> SetCompany(
        Guid id,
        SetRestaurantCompanyRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!User.IsInRole("Admin"))
        {
            return Forbid();
        }

        RestaurantResponse? restaurant = await commandService.SetCompanyAsync(
            id,
            request.CompanyId!.Value,
            cancellationToken
        );
        return restaurant is null
            ? NotFound()
            : Accepted($"/api/restaurant-operations/restaurants/{restaurant.Id}", restaurant);
    }

    [HttpPut("{id:guid}/active")]
    [ProducesResponseType<RestaurantResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RestaurantResponse>> SetActive(
        Guid id,
        SetRestaurantActiveRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!User.CanAccessRestaurant(id, RestaurantPermissions.TablesManage))
        {
            return Forbid();
        }

        RestaurantResponse? restaurant = await commandService.SetActiveAsync(
            id,
            request.IsActive,
            cancellationToken
        );
        return restaurant is null
            ? NotFound()
            : Accepted($"/api/restaurant-operations/restaurants/{restaurant.Id}", restaurant);
    }
}
