using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Reporting.Infrastructure.Queries;
using Restaurantes.Security;

namespace Restaurantes.Reporting.Api.Read.Controllers;

[Authorize, ApiController, Route("api/reporting")]
public sealed class ReportingController(
    ReportingQueries queries,
    IHttpClientFactory clients,
    IConfiguration configuration
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
                Ok(await queries.GetDailyAsync(date, restaurantIds, orderLimit, ct));
    }

    [HttpGet("health")]
    public async Task<ActionResult> Health(CancellationToken ct)
    {
        if (!User.HasAnyRestaurantPermission(RestaurantPermissions.DashboardRead))
        {
            return Forbid();
        }
        Dictionary<string, string>? endpoints = configuration
            .GetSection("MonitoredEndpoints")
            .Get<Dictionary<string, string>>();
        var tasks = (endpoints ?? []).Select(async pair =>
        {
            try
            {
                using HttpResponseMessage response = await clients
                    .CreateClient()
                    .GetAsync(pair.Value, ct);
                return new
                {
                    service = pair.Key,
                    url = pair.Value,
                    healthy = response.IsSuccessStatusCode,
                    status = (int)response.StatusCode,
                };
            }
            catch
            {
                return new
                {
                    service = pair.Key,
                    url = pair.Value,
                    healthy = false,
                    status = 0,
                };
            }
        });
        return Ok(await Task.WhenAll(tasks));
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
