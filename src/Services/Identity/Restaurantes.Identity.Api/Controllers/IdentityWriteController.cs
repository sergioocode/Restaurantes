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
    [HttpGet("auth/start/{provider}")]
    public async Task<IActionResult> Start(
        string provider,
        [FromQuery] string returnPath,
        CancellationToken ct
    )
    {
        if (provider is not ("Microsoft" or "Google") || !IsAllowedReturnPath(returnPath))
        {
            return BadRequest();
        }

        ProviderSettingsResponse settings = (ProviderSettingsResponse)
            (
                await service.GetProviderSettings(
                    ActiveProvider,
                    MicrosoftConfigured,
                    GoogleConfigured,
                    ct
                )
            ).Value!;
        if (
            settings.ActiveProvider != provider
            || (provider == "Microsoft" && !MicrosoftConfigured)
            || (provider == "Google" && !GoogleConfigured)
        )
        {
            return BadRequest(new { detail = "Proveedor no disponible." });
        }

        await HttpContext.SignOutAsync("External");
        AuthenticationProperties properties = new()
        {
            RedirectUri =
                $"/api/identity/auth/complete?returnPath={Uri.EscapeDataString(returnPath)}&provider={provider}",
        };
        properties.Items["provider"] = provider;
        properties.Items["returnPath"] = returnPath;
        return Challenge(properties, provider);
    }

    [HttpGet("auth/complete")]
    public async Task<IActionResult> Complete(
        string provider,
        string returnPath,
        CancellationToken ct
    )
    {
        if (provider is not ("Microsoft" or "Google") || !IsAllowedReturnPath(returnPath))
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
                "provider",
                out string? challengedProvider
            )
            || challengedProvider != provider
        )
        {
            return Redirect($"{safeReturnPath}#login_error=authentication");
        }

        ClaimsPrincipal principal = authentication.Principal;
        string? email =
            principal.FindFirstValue("email") ?? principal.FindFirstValue("preferred_username");
        string? subject =
            provider == "Microsoft"
                ? principal.FindFirstValue("oid")
                : principal.FindFirstValue("sub");
        string? tenant = provider == "Microsoft" ? principal.FindFirstValue("tid") : null;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(subject))
        {
            return Redirect($"{safeReturnPath}#login_error=identity");
        }

        string? accountType = principal.FindFirstValue("acct");
        if (provider == "Microsoft" && !IsAllowedMicrosoftAccount(tenant, accountType))
        {
            return Redirect($"{safeReturnPath}#login_error=tenant");
        }

        if (provider == "Google" && !IsAllowedGoogleAccount(principal))
        {
            return Redirect($"{safeReturnPath}#login_error=workspace");
        }

        IdentityResult result = await service.AuthorizeExternalAsync(
            ActiveProvider,
            provider,
            email,
            subject,
            tenant,
            ct
        );
        return result.Outcome != IdentityOutcome.Ok
            ? Redirect($"{safeReturnPath}#login_error=unauthorized")
            : Redirect(
                $"{safeReturnPath}#login_code={Uri.EscapeDataString((string)result.Value!)}"
            );
    }

    [HttpPost("auth/exchange")]
    public Task<IActionResult> Exchange(ExchangeRequest request, CancellationToken ct)
    {
        return Execute(() => service.ExchangeAsync(ActiveProvider, request.Code, ct));
    }

    [Authorize, HttpPost("users")]
    public Task<IActionResult> CreateUser(CreateAccountRequest request, CancellationToken ct)
    {
        (bool allRestaurants, HashSet<Guid> restaurantIds) = IdentityManagementScope();
        return !allRestaurants && restaurantIds.Count == 0
            ? Task.FromResult<IActionResult>(Forbid())
            : Execute(() =>
                service.CreateUser(ActiveProvider, allRestaurants, restaurantIds, request, ct)
            );
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

    private bool IsAllowedMicrosoftAccount(string? tenant, string? accountType)
    {
        return string.Equals(
                tenant,
                Configuration["ExternalAuth:Microsoft:TenantId"],
                StringComparison.OrdinalIgnoreCase
            ) && (accountType is null || accountType == "0");
    }

    private bool IsAllowedGoogleAccount(ClaimsPrincipal principal)
    {
        return string.Equals(
                principal.FindFirstValue("hd"),
                Configuration["ExternalAuth:Google:WorkspaceDomain"],
                StringComparison.OrdinalIgnoreCase
            )
            && string.Equals(
                principal.FindFirstValue("email_verified"),
                "true",
                StringComparison.OrdinalIgnoreCase
            );
    }

    public sealed record ExchangeRequest(string Code);
}
