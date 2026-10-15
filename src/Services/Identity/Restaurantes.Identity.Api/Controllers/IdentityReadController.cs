using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Identity.Application;
using Restaurantes.Security;

namespace Restaurantes.Identity.Api.Controllers;

[ApiController, Route("api/identity")]
public sealed class IdentityReadController(IdentityService service, IConfiguration configuration)
    : ControllerBase
{
    private bool MicrosoftConfigured =>
        !string.IsNullOrWhiteSpace(configuration["ExternalAuth:PublicOrigin"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Microsoft:TenantId"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Microsoft:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Microsoft:ClientSecret"]);
    private bool GoogleConfigured =>
        !string.IsNullOrWhiteSpace(configuration["ExternalAuth:PublicOrigin"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Google:WorkspaceDomain"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Google:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Google:ClientSecret"]);
    private string ActiveProvider => ReadActiveProvider(configuration);

    [HttpGet("auth/provider")]
    public Task<IActionResult> Provider(CancellationToken ct)
    {
        return Execute(() =>
            service.GetProviderSettings(ActiveProvider, MicrosoftConfigured, GoogleConfigured, ct)
        );
    }

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

    private (bool AllRestaurants, HashSet<Guid> RestaurantIds) IdentityManagementScope()
    {
        bool allRestaurants = User.FindAll(ClaimTypes.Role)
            .Any(claim =>
                RestaurantPermissions
                    .ForRole(claim.Value)
                    .Contains(RestaurantPermissions.IdentityManage)
            );
        HashSet<Guid> restaurantIds = [];
        string suffix = $":{RestaurantPermissions.IdentityManage}";
        foreach (
            Claim claim in User.FindAll(RestaurantClaimTypes.Permission)
                .Where(claim => claim.Value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        )
        {
            if (Guid.TryParse(claim.Value[..^suffix.Length], out Guid restaurantId))
            {
                restaurantIds.Add(restaurantId);
            }
        }

        return (allRestaurants, restaurantIds);
    }

    private static string ReadActiveProvider(IConfiguration configuration)
    {
        string? provider = configuration["ExternalAuth:Provider"];
        return provider is "Microsoft" or "Google"
            ? provider
            : throw new InvalidOperationException(
                "ExternalAuth:Provider must be Microsoft or Google."
            );
    }

    private async Task<IActionResult> Execute(Func<Task<IdentityResult>> operation)
    {
        IdentityResult result = await operation();
        return result.Outcome switch
        {
            IdentityOutcome.Ok => Ok(result.Value),
            IdentityOutcome.Created => Created(result.Location!, result.Value),
            IdentityOutcome.NoContent => NoContent(),
            IdentityOutcome.BadRequest => BadRequest(result.Value),
            IdentityOutcome.Unauthorized => Unauthorized(),
            IdentityOutcome.Forbidden => Forbid(),
            IdentityOutcome.NotFound => NotFound(),
            IdentityOutcome.Conflict => Conflict(result.Value),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}
