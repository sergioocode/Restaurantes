using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Identity.Application;
using Restaurantes.Security;

namespace Restaurantes.Identity.Api.Controllers;

[ApiController, Route("api/identity")]
public sealed class IdentityReadController(IdentityService service, IConfiguration configuration)
    : IdentityControllerBase(configuration)
{
    [Authorize, HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(
            new
            {
                id = User.FindFirstValue(ClaimTypes.NameIdentifier),
                name = User.Identity?.Name,
                allRestaurantsRoles = User.FindAll(ClaimTypes.Role).Select(x => x.Value).Distinct(),
                restaurants = User.FindAll(RestaurantClaimTypes.RestaurantId)
                    .Select(x => x.Value)
                    .Distinct(),
            }
        );
    }

    [Authorize, HttpGet("users")]
    public Task<IActionResult> ListUsers(CancellationToken ct)
    {
        (bool allRestaurants, HashSet<Guid> restaurantIds) = IdentityManagementScope();
        return !allRestaurants && restaurantIds.Count == 0
            ? Task.FromResult<IActionResult>(Forbid())
            : Execute(() => service.ListUsers(allRestaurants, restaurantIds, ct));
    }
}
