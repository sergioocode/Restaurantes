using Restaurantes.Identity.Domain;
using Restaurantes.Security;

namespace Restaurantes.Identity.Application;

public sealed class IdentityService(
    IIdentityStore store,
    IAccessTokenIssuer tokens,
    TimeProvider time
)
{
    private static readonly string[] GlobalRoles = ["Admin", "Gerente", "Contabilidad", "Oficina"];
    private static readonly string[] LocalRoles = ["Manager", "PosComandero", "Kds"];

    public async Task<IdentityResult> GetProviderSettings(
        bool microsoftConfigured,
        bool googleConfigured,
        CancellationToken ct
    )
    {
        AuthenticationSettings settings = await store.GetSettingsAsync(ct);
        return Ok(
            new ProviderSettingsResponse(
                settings.ActiveProvider,
                microsoftConfigured,
                googleConfigured
            )
        );
    }

    public async Task<IdentityResult> ChangeProvider(
        ChangeProviderRequest request,
        bool microsoftConfigured,
        bool googleConfigured,
        CancellationToken ct
    )
    {
        if (!ValidProvider(request.ActiveProvider))
        {
            return BadRequest("Proveedor no válido.");
        }

        if (
            (request.ActiveProvider == "Microsoft" && !microsoftConfigured)
            || (request.ActiveProvider == "Google" && !googleConfigured)
        )
        {
            return BadRequest("El proveedor aún no está configurado.");
        }

        if (!await store.AnyActiveAdminForProviderAsync(request.ActiveProvider, ct))
        {
            return BadRequest("Autoriza primero un Admin del proveedor de destino.");
        }

        AuthenticationSettings settings = await store.GetSettingsAsync(ct);
        settings.ActiveProvider = request.ActiveProvider;
        await store.SaveChangesAsync(ct);
        return Ok(
            new ProviderSettingsResponse(
                settings.ActiveProvider,
                microsoftConfigured,
                googleConfigured
            )
        );
    }

    public async Task<IdentityResult> AuthorizeExternalAsync(
        string provider,
        string email,
        string subject,
        string? tenantId,
        CancellationToken ct
    )
    {
        AuthenticationSettings settings = await store.GetSettingsAsync(ct);
        if (!string.Equals(settings.ActiveProvider, provider, StringComparison.Ordinal))
        {
            return new(IdentityOutcome.Unauthorized);
        }

        string normalizedEmail = email.Trim().ToLowerInvariant();
        ApplicationUser? user = await store.FindByEmailAsync(provider, normalizedEmail, ct);
        if (user is null || !user.IsActive)
        {
            return new(IdentityOutcome.Unauthorized);
        }

        ApplicationUser? subjectOwner = await store.FindBySubjectAsync(
            provider,
            tenantId,
            subject,
            ct
        );
        if (subjectOwner is not null && subjectOwner.Id != user.Id)
        {
            return new(IdentityOutcome.Unauthorized);
        }

        if (user.ProviderSubject is null)
        {
            user.ProviderSubject = subject;
            user.TenantId = tenantId;
            await store.SaveChangesAsync(ct);
        }
        else if (user.ProviderSubject != subject || user.TenantId != tenantId)
        {
            return new(IdentityOutcome.Unauthorized);
        }

        string code = await store.CreateLoginTicketAsync(user.Id, ct);
        return Ok(code);
    }

    public async Task<IdentityResult> ExchangeAsync(string code, CancellationToken ct)
    {
        ApplicationUser? user = await store.ConsumeLoginTicketAsync(code, ct);
        if (user is null || !user.IsActive)
        {
            return new(IdentityOutcome.Unauthorized);
        }

        AuthenticationSettings settings = await store.GetSettingsAsync(ct);
        if (settings.ActiveProvider != user.Provider)
        {
            return new(IdentityOutcome.Unauthorized);
        }

        IssuedAccessToken token = tokens.Issue(user);
        string[] globalRoles = GlobalRoles.Contains(user.Role, StringComparer.Ordinal)
            ? [user.Role]
            : [];
        LoginRestaurantResponse[] restaurants =
            user.RestaurantId is Guid restaurantId
            && LocalRoles.Contains(user.Role, StringComparer.Ordinal)
                ?
                [
                    new(
                        restaurantId,
                        user.Role,
                        user.CreatedAtUtc,
                        null,
                        RestaurantPermissions.ForRole(user.Role)
                    ),
                ]
                : [];
        return Ok(
            new LoginResponse(
                token.Value,
                "Bearer",
                token.ExpiresAtUtc,
                new(user.Id, user.Email, user.DisplayName),
                globalRoles,
                restaurants
            )
        );
    }

    public async Task<IdentityResult> ListUsers(CancellationToken ct)
    {
        return Ok((await store.ListUsersAsync(ct)).Select(ToResponse).ToArray());
    }

    public async Task<IdentityResult> CreateUser(CreateAccountRequest request, CancellationToken ct)
    {
        string? error = Validate(
            request.Email,
            request.Provider,
            request.DisplayName,
            request.Role,
            request.RestaurantId
        );
        if (error is not null)
        {
            return BadRequest(error);
        }

        string email = request.Email.Trim().ToLowerInvariant();
        if (await store.FindByEmailAsync(request.Provider, email, ct) is not null)
        {
            return new(IdentityOutcome.Conflict, new { detail = "La cuenta ya está autorizada." });
        }

        ApplicationUser user = new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            Provider = request.Provider,
            DisplayName = request.DisplayName.Trim(),
            Role = request.Role,
            RestaurantId = request.RestaurantId,
            IsActive = true,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        await store.AddUserAsync(user, ct);
        return new(IdentityOutcome.Created, ToResponse(user), $"/api/identity/users/{user.Id}");
    }

    public async Task<IdentityResult> UpdateUser(
        Guid id,
        UpdateAccountRequest request,
        CancellationToken ct
    )
    {
        ApplicationUser? user = await store.FindByIdAsync(id, ct);
        if (user is null)
        {
            return new(IdentityOutcome.NotFound);
        }

        string? error = Validate(
            user.Email,
            user.Provider,
            request.DisplayName,
            request.Role,
            request.RestaurantId
        );
        if (error is not null)
        {
            return BadRequest(error);
        }

        if (
            user.Role == "Admin"
            && (!request.IsActive || request.Role != "Admin")
            && !await store.AnyActiveAdminExceptAsync(id, user.Provider, ct)
        )
        {
            return BadRequest("No se puede desactivar el último Admin.");
        }

        user.DisplayName = request.DisplayName.Trim();
        user.Role = request.Role;
        user.RestaurantId = request.RestaurantId;
        user.IsActive = request.IsActive;
        await store.SaveChangesAsync(ct);
        return Ok(ToResponse(user));
    }

    private static string? Validate(
        string email,
        string provider,
        string displayName,
        string role,
        Guid? restaurantId
    )
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 320)
        {
            return "Introduce un correo válido.";
        }

        return !ValidProvider(provider) ? "Proveedor no válido."
            : string.IsNullOrWhiteSpace(displayName) || displayName.Length > 120
                ? "Introduce un nombre visible."
            : GlobalRoles.Contains(role, StringComparer.Ordinal)
                ? restaurantId is null ? null
                    : "Los roles globales no tienen local."
            : LocalRoles.Contains(role, StringComparer.Ordinal)
                ? restaurantId is Guid id && id != Guid.Empty ? null
                    : "El rol local requiere un local."
            : "Rol no válido.";
    }

    private static bool ValidProvider(string provider)
    {
        return provider is "Microsoft" or "Google";
    }

    private static AccountResponse ToResponse(ApplicationUser user)
    {
        return new(
            user.Id,
            user.Email,
            user.Provider,
            user.DisplayName,
            user.Role,
            user.RestaurantId,
            user.IsActive,
            user.ProviderSubject is not null
        );
    }

    private static IdentityResult Ok(object value)
    {
        return new(IdentityOutcome.Ok, value);
    }

    private static IdentityResult BadRequest(string detail)
    {
        return new(IdentityOutcome.BadRequest, new { detail });
    }
}
