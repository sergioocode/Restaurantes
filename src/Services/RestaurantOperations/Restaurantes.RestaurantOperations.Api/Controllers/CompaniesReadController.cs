using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Contracts.Responses;
using Restaurantes.Security;

namespace Restaurantes.RestaurantOperations.Api.Controllers;

[ApiController, Authorize]
[Route("api/restaurant-operations/companies")]
public sealed class CompaniesReadController(
    ICompanyReadStore companyStore,
    IRestaurantReadStore restaurantStore
) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CompanyResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CompanyResponse>>> List(
        CancellationToken cancellationToken
    )
    {
        if (!User.HasAnyRestaurantPermission(RestaurantPermissions.BackofficeAccess))
        {
            return Forbid();
        }

        IReadOnlyList<CompanyResponse> companies = await companyStore.ListAsync(cancellationToken);
        if (User.HasGlobalRestaurantPermission(RestaurantPermissions.BackofficeAccess))
        {
            return Ok(companies);
        }

        IReadOnlyList<RestaurantResponse> restaurants = await restaurantStore.ListAsync(
            cancellationToken
        );
        HashSet<Guid> accessibleCompanyIds = restaurants
            .Where(restaurant =>
                restaurant.CompanyId.HasValue
                && User.CanAccessRestaurant(restaurant.Id, RestaurantPermissions.BackofficeAccess)
            )
            .Select(restaurant => restaurant.CompanyId!.Value)
            .ToHashSet();

        return Ok(companies.Where(company => accessibleCompanyIds.Contains(company.Id)).ToArray());
    }
}
