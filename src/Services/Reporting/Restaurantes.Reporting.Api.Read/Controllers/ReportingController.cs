using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Reporting.Application.Dashboard;
using Restaurantes.Reporting.Application.Health;
using Restaurantes.Security;

namespace Restaurantes.Reporting.Api.Read.Controllers;

[Authorize, ApiController, Route("api/reporting")]
public sealed class ReportingController(
    DashboardQueryService dashboard,
    ReportingHealthService health
) : ControllerBase
{
    [HttpGet("dashboard"), HttpGet("dashboard/daily")]
    public async Task<ActionResult<DailyDashboard>> Dashboard(
        DateOnly? date,
        Guid? restaurantId,
        CancellationToken ct,
        int orderLimit = 10
    )
    {
        if (
            restaurantId.HasValue
            && !User.CanAccessRestaurant(restaurantId.Value, RestaurantPermissions.DashboardRead)
        )
        {
            return Forbid();
        }

        IReadOnlyCollection<Guid>? restaurantIds =
            restaurantId.HasValue ? [restaurantId.Value]
            : User.IsInRole("Admin") ? null
            : AuthorizedRestaurants(User);
        return restaurantIds is { Count: 0 }
            ? (ActionResult<DailyDashboard>)Forbid()
            : (ActionResult<DailyDashboard>)
                Ok(await dashboard.GetDailyAsync(date, restaurantIds, orderLimit, ct));
    }

    [HttpGet("health")]
    public async Task<ActionResult> Health(CancellationToken ct)
    {
        if (!User.HasAnyRestaurantPermission(RestaurantPermissions.DashboardRead))
        {
            return Forbid();
        }
        return Ok(await health.GetAsync(ct));
    }

    private static Guid[] AuthorizedRestaurants(ClaimsPrincipal user)
    {
        return user.FindAll(RestaurantClaimTypes.RestaurantId)
            .Select(x => Guid.TryParse(x.Value, out Guid id) ? id : Guid.Empty)
            .Where(id =>
                id != Guid.Empty
                && user.CanAccessRestaurant(id, RestaurantPermissions.DashboardRead)
            )
            .Distinct()
            .ToArray();
    }
}
