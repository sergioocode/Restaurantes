using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Identity.Application;

namespace Restaurantes.Identity.Api.Controllers;

[ApiController, Route("api/identity")]
public sealed class IdentityWriteController(IdentityService service, IConfiguration configuration)
    : IdentityControllerBase(configuration)
{
    [HttpGet("auth/start")]
    public async Task<IActionResult> Start(
        [FromQuery] string returnPath,
        [FromQuery] bool reauthenticate = false
    )
    {
        if (!IsAllowedReturnPath(returnPath))
        {
            return BadRequest();
        }
        if (!KeycloakConfigured)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { detail = "El inicio de sesión no está configurado." }
            );
        }

        await HttpContext.SignOutAsync("External");
        AuthenticationProperties properties = new()
        {
            RedirectUri =
                $"/api/identity/auth/complete?returnPath={Uri.EscapeDataString(returnPath)}",
        };
        properties.Items["returnPath"] = returnPath;
        if (reauthenticate)
        {
            properties.Items["reauthenticate"] = "true";
        }
        return Challenge(properties, "Keycloak");
    }

    [HttpGet("auth/logout")]
    public IActionResult Logout([FromQuery] string returnPath)
    {
        if (!IsAllowedReturnPath(returnPath))
        {
            return BadRequest();
        }
        if (!KeycloakConfigured)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { detail = "El cierre de sesión no está configurado." }
            );
        }

        AuthenticationProperties properties = new() { RedirectUri = returnPath };
        return SignOut(properties, "External", "Keycloak");
    }

    [HttpGet("auth/complete")]
    public async Task<IActionResult> Complete(string returnPath, CancellationToken ct)
    {
        if (!IsAllowedReturnPath(returnPath))
        {
            return BadRequest();
        }

        string safeReturnPath = Url.IsLocalUrl(returnPath) ? returnPath : "/";

        AuthenticateResult authentication = await HttpContext.AuthenticateAsync("External");
        await HttpContext.SignOutAsync("External");
        if (
            !authentication.Succeeded
            || authentication.Principal is null
            || authentication.Properties is null
            || !authentication.Properties.Items.TryGetValue(
                "returnPath",
                out string? challengedReturnPath
            )
            || challengedReturnPath != returnPath
        )
        {
            return Redirect($"{safeReturnPath}#login_error=authentication");
        }

        ClaimsPrincipal principal = authentication.Principal;
        string? email = principal.FindFirstValue("email");
        string? subject = principal.FindFirstValue("sub");
        if (
            string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(subject)
            || !string.Equals(
                principal.FindFirstValue("email_verified"),
                "true",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return Redirect($"{safeReturnPath}#login_error=identity");
        }

        IdentityResult result = await service.AuthorizeExternalAsync(email, subject, ct);
        return result.Outcome != IdentityOutcome.Ok
            ? Redirect($"{safeReturnPath}#login_error=unauthorized")
            : Redirect(
                $"{safeReturnPath}#login_code={Uri.EscapeDataString((string)result.Value!)}"
            );
    }

    [HttpPost("auth/exchange")]
    public Task<IActionResult> Exchange(ExchangeRequest request, CancellationToken ct)
    {
        return Execute(() => service.ExchangeAsync(request.Code, ct));
    }

    [Authorize, HttpPost("users")]
    public Task<IActionResult> CreateUser(CreateAccountRequest request, CancellationToken ct)
    {
        (bool allRestaurants, HashSet<Guid> restaurantIds) = IdentityManagementScope();
        return !allRestaurants && restaurantIds.Count == 0
            ? Task.FromResult<IActionResult>(Forbid())
            : Execute(() => service.CreateUser(allRestaurants, restaurantIds, request, ct));
    }

    [Authorize, HttpPut("users/{id:guid}")]
    public Task<IActionResult> UpdateUser(
        Guid id,
        UpdateAccountRequest request,
        CancellationToken ct
    )
    {
        (bool allRestaurants, HashSet<Guid> restaurantIds) = IdentityManagementScope();
        return !allRestaurants && restaurantIds.Count == 0
            ? Task.FromResult<IActionResult>(Forbid())
            : Execute(() => service.UpdateUser(id, allRestaurants, restaurantIds, request, ct));
    }

    [Authorize, HttpDelete("users/{id:guid}")]
    public Task<IActionResult> DeleteUser(Guid id, CancellationToken ct)
    {
        if (
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid currentUserId)
            && currentUserId == id
        )
        {
            return Task.FromResult<IActionResult>(
                BadRequest(new { detail = "No puedes eliminar tu propia cuenta." })
            );
        }

        (bool allRestaurants, HashSet<Guid> restaurantIds) = IdentityManagementScope();
        return !allRestaurants && restaurantIds.Count == 0
            ? Task.FromResult<IActionResult>(Forbid())
            : Execute(() => service.DeleteUser(id, allRestaurants, restaurantIds, ct));
    }

    private static bool IsAllowedReturnPath(string path)
    {
        return path is "/backoffice/" or "/pos/" or "/commander/" or "/kds/" or "/dashboard/";
    }

    public sealed record ExchangeRequest(string Code);
}
