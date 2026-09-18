using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurantes.Identity.Application;
using Restaurantes.Security;

namespace Restaurantes.Identity.Api.Write.Controllers;

[ApiController, Route("api/identity")]
public sealed class IdentityController(IdentityService service, IConfiguration configuration)
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

    [HttpGet("auth/provider")]
    public Task<IActionResult> Provider(CancellationToken ct)
    {
        return Execute(() =>
            service.GetProviderSettings(MicrosoftConfigured, GoogleConfigured, ct)
        );
    }

    [Authorize(Roles = "Admin"), HttpPut("auth/provider")]
    public Task<IActionResult> ChangeProvider(ChangeProviderRequest request, CancellationToken ct)
    {
        return Execute(() =>
            service.ChangeProvider(request, MicrosoftConfigured, GoogleConfigured, ct)
        );
    }

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
            (await service.GetProviderSettings(MicrosoftConfigured, GoogleConfigured, ct)).Value!;
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
            return Redirect($"{returnPath}#login_error=authentication");
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
            return Redirect($"{returnPath}#login_error=identity");
        }

        string? accountType = principal.FindFirstValue("acct");
        if (
            provider == "Microsoft"
            && (
                !string.Equals(
                    tenant,
                    configuration["ExternalAuth:Microsoft:TenantId"],
                    StringComparison.OrdinalIgnoreCase
                ) || (accountType is not null && accountType != "0")
            )
        )
        {
            return Redirect($"{returnPath}#login_error=tenant");
        }

        if (
            provider == "Google"
            && (
                !string.Equals(
                    principal.FindFirstValue("hd"),
                    configuration["ExternalAuth:Google:WorkspaceDomain"],
                    StringComparison.OrdinalIgnoreCase
                )
                || !string.Equals(
                    principal.FindFirstValue("email_verified"),
                    "true",
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            return Redirect($"{returnPath}#login_error=workspace");
        }

        IdentityResult result = await service.AuthorizeExternalAsync(
            provider,
            email,
            subject,
            tenant,
            ct
        );
        return result.Outcome != IdentityOutcome.Ok
            ? Redirect($"{returnPath}#login_error=unauthorized")
            : Redirect($"{returnPath}#login_code={Uri.EscapeDataString((string)result.Value!)}");
    }

    [HttpPost("auth/exchange")]
    public Task<IActionResult> Exchange(ExchangeRequest request, CancellationToken ct)
    {
        return Execute(() => service.ExchangeAsync(request.Code, ct));
    }

    [Authorize, HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(
            new
            {
                id = User.FindFirstValue(ClaimTypes.NameIdentifier),
                name = User.Identity?.Name,
                globalRoles = User.FindAll(ClaimTypes.Role).Select(x => x.Value).Distinct(),
                restaurants = User.FindAll(RestaurantClaimTypes.RestaurantId)
                    .Select(x => x.Value)
                    .Distinct(),
            }
        );
    }

    [Authorize(Roles = "Admin"), HttpGet("users")]
    public Task<IActionResult> ListUsers(CancellationToken ct)
    {
        return Execute(() => service.ListUsers(ct));
    }

    [Authorize(Roles = "Admin"), HttpPost("users")]
    public Task<IActionResult> CreateUser(CreateAccountRequest request, CancellationToken ct)
    {
        return Execute(() => service.CreateUser(request, ct));
    }

    [Authorize(Roles = "Admin"), HttpPut("users/{id:guid}")]
    public Task<IActionResult> UpdateUser(
        Guid id,
        UpdateAccountRequest request,
        CancellationToken ct
    )
    {
        return Execute(() => service.UpdateUser(id, request, ct));
    }

    private static bool IsAllowedReturnPath(string path)
    {
        return path is "/backoffice/" or "/pos/" or "/commander/" or "/kds/" or "/dashboard/";
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

    public sealed record ExchangeRequest(string Code);
}
