using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Identity.Application;
using Restaurantes.Security;

namespace Restaurantes.Identity.Api.Controllers;

public abstract class IdentityControllerBase(IConfiguration configuration) : ControllerBase
{
    protected IConfiguration Configuration => configuration;

    protected bool MicrosoftConfigured =>
        !string.IsNullOrWhiteSpace(configuration["ExternalAuth:PublicOrigin"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Microsoft:TenantId"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Microsoft:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Microsoft:ClientSecret"]);

    protected bool GoogleConfigured =>
        !string.IsNullOrWhiteSpace(configuration["ExternalAuth:PublicOrigin"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Google:WorkspaceDomain"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Google:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Google:ClientSecret"]);

    protected string ActiveProvider => ReadActiveProvider(configuration);

    protected (bool AllRestaurants, HashSet<Guid> RestaurantIds) IdentityManagementScope()
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

    protected async Task<IActionResult> Execute(Func<Task<IdentityResult>> operation)
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
            _ => throw new InvalidOperationException(
                $"Unsupported identity outcome: {result.Outcome}."
            ),
        };
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
}
