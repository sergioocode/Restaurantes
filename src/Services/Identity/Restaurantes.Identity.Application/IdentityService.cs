using Restaurantes.Identity.Domain;
using Restaurantes.Security;

namespace Restaurantes.Identity.Application;

public sealed class IdentityService(
    IIdentityStore store,
    IAccessTokenIssuer tokens,
    TimeProvider time
)
{
    private static readonly string[] Roles =
    [
        "Admin",
        "Gerente",
        "Contabilidad",
        "Marketing",
        "Manager",
        "Camarero",
        "Kds",
    ];

    public async Task<IdentityResult> GetProviderSettings(
        string activeProvider,
        bool microsoftConfigured,
        bool googleConfigured,
        CancellationToken ct
    )
    {
        await store.GetSettingsAsync(ct);
        return Ok(
            new ProviderSettingsResponse(activeProvider, microsoftConfigured, googleConfigured)
        );
    }

    public async Task<IdentityResult> AuthorizeExternalAsync(
        string activeProvider,
        string provider,
        string email,
        string subject,
        string? tenantId,
        CancellationToken ct
    )
    {
        if (!string.Equals(activeProvider, provider, StringComparison.Ordinal))
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

    public async Task<IdentityResult> ExchangeAsync(
        string activeProvider,
        string code,
        CancellationToken ct
    )
    {
        ApplicationUser? user = await store.ConsumeLoginTicketAsync(code, ct);
        if (user is null || !user.IsActive)
        {
            return new(IdentityOutcome.Unauthorized);
        }

        if (activeProvider != user.Provider)
        {
            return new(IdentityOutcome.Unauthorized);
        }

        IssuedAccessToken token = tokens.Issue(user);
        string[] allRestaurantsRoles = user.AllRestaurants ? [user.Role] : [];
        LoginRestaurantResponse[] restaurants =
            !user.AllRestaurants && user.RestaurantId is Guid restaurantId
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
                allRestaurantsRoles,
                restaurants
            )
        );
    }

    public async Task<IdentityResult> ListUsers(
        bool canManageAllRestaurants,
        IReadOnlySet<Guid> managedRestaurantIds,
        CancellationToken ct
    )
    {
        return Ok(
            (await store.ListUsersAsync(ct))
                .Where(user =>
                    CanManageScope(
                        canManageAllRestaurants,
                        managedRestaurantIds,
                        user.AllRestaurants,
                        user.RestaurantId
                    )
                )
                .Select(ToResponse)
                .ToArray()
        );
    }

    public async Task<IdentityResult> CreateUser(
        string activeProvider,
        bool canManageAllRestaurants,
        IReadOnlySet<Guid> managedRestaurantIds,
        CreateAccountRequest request,
        CancellationToken ct
    )
    {
        string? error = Validate(
            request.Email,
            request.DisplayName,
            request.Role,
            request.AllRestaurants,
            request.RestaurantId
        );
        if (error is not null)
        {
            return BadRequest(error);
        }

        if (
            !CanManageScope(
                canManageAllRestaurants,
                managedRestaurantIds,
                request.AllRestaurants,
                request.RestaurantId
            )
        )
        {
            return new(IdentityOutcome.Forbidden);
        }

        string email = request.Email.Trim().ToLowerInvariant();
        if (await store.FindByEmailAsync(activeProvider, email, ct) is not null)
        {
            return new(IdentityOutcome.Conflict, new { detail = "La cuenta ya está autorizada." });
        }

        ApplicationUser user = new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            Provider = activeProvider,
            DisplayName = request.DisplayName.Trim(),
            Role = request.Role,
            AllRestaurants = request.AllRestaurants,
            RestaurantId = request.RestaurantId,
            IsActive = true,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        await store.AddUserAsync(user, ct);
        return new(IdentityOutcome.Created, ToResponse(user), $"/api/identity/users/{user.Id}");
    }

    public async Task<IdentityResult> UpdateUser(
        Guid id,
        bool canManageAllRestaurants,
        IReadOnlySet<Guid> managedRestaurantIds,
        UpdateAccountRequest request,
        CancellationToken ct
    )
    {
        ApplicationUser? user = await store.FindByIdAsync(id, ct);
        if (user is null)
        {
            return new(IdentityOutcome.NotFound);
        }

        if (
            !CanManageScope(
                canManageAllRestaurants,
                managedRestaurantIds,
                user.AllRestaurants,
                user.RestaurantId
            )
            || !CanManageScope(
                canManageAllRestaurants,
                managedRestaurantIds,
                request.AllRestaurants,
                request.RestaurantId
            )
        )
        {
            return new(IdentityOutcome.Forbidden);
        }

        string? error = Validate(
            user.Email,
            request.DisplayName,
            request.Role,
            request.AllRestaurants,
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
        user.AllRestaurants = request.AllRestaurants;
        user.RestaurantId = request.RestaurantId;
        user.IsActive = request.IsActive;
        await store.SaveChangesAsync(ct);
        return Ok(ToResponse(user));
    }

    private static string? Validate(
        string email,
        string displayName,
        string role,
        bool allRestaurants,
        Guid? restaurantId
    )
    {
        return string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 320
                ? "Introduce un correo válido."
            : string.IsNullOrWhiteSpace(displayName) || displayName.Length > 120
                ? "Introduce un nombre visible."
            : !Roles.Contains(role, StringComparer.Ordinal) ? "Rol no válido."
            : allRestaurants && restaurantId is not null
                ? "Todos los Locales no admite un local concreto."
            : !allRestaurants && (restaurantId is null || restaurantId == Guid.Empty)
                ? "Selecciona un local o Todos los Locales."
            : null;
    }

    private static bool CanManageScope(
        bool canManageAllRestaurants,
        IReadOnlySet<Guid> managedRestaurantIds,
        bool allRestaurants,
        Guid? restaurantId
    )
    {
        return canManageAllRestaurants
            || (!allRestaurants && restaurantId is Guid id && managedRestaurantIds.Contains(id));
    }

    private static AccountResponse ToResponse(ApplicationUser user)
    {
        return new(
            user.Id,
            user.Email,
            user.Provider,
            user.DisplayName,
            user.Role,
            user.AllRestaurants,
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
