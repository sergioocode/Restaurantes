using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Identity.Application;
using Restaurantes.Security;

namespace Restaurantes.Identity.Api.Controllers;

public abstract class IdentityControllerBase(IConfiguration configuration) : ControllerBase
{
    protected bool KeycloakConfigured =>
        Uri.TryCreate(
            configuration["ExternalAuth:Keycloak:Authority"],
            UriKind.Absolute,
            out Uri? authority
        )
        && authority.Scheme is "http" or "https"
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:PublicOrigin"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Keycloak:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Keycloak:ClientSecret"]);

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
}
