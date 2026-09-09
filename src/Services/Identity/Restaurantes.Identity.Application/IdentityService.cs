using Restaurantes.Identity.Domain;
using Restaurantes.Security;

namespace Restaurantes.Identity.Application;

public sealed class IdentityService(
    IIdentityStore store,
    IAccessTokenIssuer tokens,
    TimeProvider time
)
{
    private static readonly string[] AllowedRoles =
    [
        "Admin",
        "Gerente",
        "Oficina",
        "Manager",
        "PosComandero",
        "Kds",
    ];

    private static readonly string[] GlobalRoles = ["Admin", "Gerente", "Oficina"];
    private static readonly string[] SingleRestaurantRoles = ["PosComandero", "Kds"];

    public async Task<IdentityResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return BadRequest("Username and password are required.");
        }

        ApplicationUser? user = await store.FindByNameAsync(request.Username.Trim());
        if (
            user is null
            || !user.IsActive
            || !await store.CheckPasswordAsync(user, request.Password)
        )
        {
            return new(IdentityOutcome.Unauthorized);
        }

        await store.LoadRestaurantAccessesAsync(user, ct);
        DateTime now = time.GetUtcNow().UtcDateTime;
        string[] globalRoles = await store.GetRolesAsync(user);
        IssuedAccessToken token = tokens.Issue(user, globalRoles);
        LoginRestaurantResponse[] restaurants = user
            .RestaurantAccesses.Where(x => x.IsCurrentlyActive(now))
            .Select(x => new LoginRestaurantResponse(
                x.RestaurantId,
                x.Role,
                x.ValidFromUtc,
                x.ValidUntilUtc,
                RestaurantPermissions.ForRole(x.Role)
            ))
            .ToArray();
        return Ok(
            new LoginResponse(
                token.Value,
                "Bearer",
                token.ExpiresAtUtc,
                new LoginUserResponse(user.Id, user.UserName, user.DisplayName),
                globalRoles,
                restaurants
            )
        );
    }

    public async Task<IdentityResult> ListUsers(IdentityActor actor, CancellationToken ct)
    {
        if (!actor.IsAdmin)
        {
            return new(IdentityOutcome.Forbidden);
        }

        IdentityUserResponse[] visible = (await store.ListUsersAsync(ct))
            .Select(user => new
            {
                User = user,
                Assignments = user
                    .RestaurantAccesses.Where(x =>
                        actor.ManageableRestaurantIds.Contains(x.RestaurantId)
                    )
                    .OrderBy(x => x.RestaurantId)
                    .ToArray(),
            })
            .Where(x => x.Assignments.Length > 0 || actor.IsAdmin)
            .Select(x => new IdentityUserResponse(
                x.User.Id,
                x.User.UserName,
                x.User.DisplayName,
                x.User.IsActive,
                x.User.CreatedAtUtc,
                x.Assignments.Select(ToRestaurantAccess).ToArray()
            ))
            .ToArray();
        return Ok(visible);
    }

    public async Task<IdentityResult> CreateUser(
        CreateStaffUserRequest request,
        IdentityActor actor,
        CancellationToken ct
    )
    {
        if (!actor.CanManage(request.RestaurantId))
        {
            return new(IdentityOutcome.Forbidden);
        }
        if (!ValidUserRequest(request, out string? error))
        {
            return BadRequest(error!);
        }
        if (!CanAssignRole(actor, request.Role))
        {
            return new(IdentityOutcome.Forbidden);
        }
        if (await store.FindByNameAsync(request.Username.Trim()) is not null)
        {
            return Conflict("Username already exists.");
        }

        await using IIdentityTransaction transaction = await store.BeginTransactionAsync(ct);
        DateTime now = time.GetUtcNow().UtcDateTime;
        ApplicationUser user = new()
        {
            Id = Guid.NewGuid(),
            UserName = request.Username.Trim(),
            DisplayName = request.DisplayName.Trim(),
            CreatedAtUtc = now,
            IsActive = true,
            LockoutEnabled = true,
        };
        IdentityOperation created = await store.CreateUserAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return IdentityFailure(created);
        }

        if (GlobalRoles.Contains(request.Role, StringComparer.Ordinal))
        {
            IdentityOperation roleResult = await EnsureGlobalRoleAsync(user, request.Role);
            if (!roleResult.Succeeded)
            {
                return IdentityFailure(roleResult);
            }
        }

        store.AddAssignment(
            new UserRestaurantAssignment
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                RestaurantId = request.RestaurantId,
                Role = request.Role,
                ValidFromUtc = now,
            }
        );
        await store.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(
            IdentityOutcome.Created,
            new CreatedIdentityUserResponse(
                user.Id,
                user.UserName,
                user.DisplayName,
                request.RestaurantId,
                request.Role
            ),
            $"/api/identity/users/{user.Id}"
        );
    }

    public Task<IdentityResult> AssignRestaurant(
        Guid userId,
        AssignRestaurantRequest request,
        IdentityActor actor,
        CancellationToken ct
    ) =>
        UpsertRestaurantAssignment(
            userId,
            request.RestaurantId,
            new UpdateRestaurantAssignmentRequest(
                request.Role,
                true,
                request.ValidFromUtc,
                request.ValidUntilUtc
            ),
            actor,
            ct
        );

    public Task<IdentityResult> UpdateRestaurantAssignment(
        Guid userId,
        Guid restaurantId,
        UpdateRestaurantAssignmentRequest request,
        IdentityActor actor,
        CancellationToken ct
    ) => UpsertRestaurantAssignment(userId, restaurantId, request, actor, ct);

    public async Task<IdentityResult> DisableRestaurantAssignment(
        Guid userId,
        Guid restaurantId,
        IdentityActor actor,
        CancellationToken ct
    )
    {
        if (!actor.CanManage(restaurantId))
        {
            return new(IdentityOutcome.Forbidden);
        }

        UserRestaurantAssignment? assignment = await store.FindAssignmentAsync(
            userId,
            restaurantId,
            ct
        );
        if (assignment is null)
        {
            return new(IdentityOutcome.NotFound);
        }
        assignment.IsActive = false;
        await store.SaveChangesAsync(ct);
        ApplicationUser? user = await store.FindByIdAsync(userId);
        if (user is not null)
        {
            await SynchronizeGlobalRolesAsync(user, ct);
            await store.UpdateSecurityStampAsync(user);
        }
        return new(IdentityOutcome.NoContent);
    }

    private async Task<IdentityResult> UpsertRestaurantAssignment(
        Guid userId,
        Guid restaurantId,
        UpdateRestaurantAssignmentRequest request,
        IdentityActor actor,
        CancellationToken ct
    )
    {
        if (!actor.CanManage(restaurantId))
        {
            return new(IdentityOutcome.Forbidden);
        }
        if (!AllowedRoles.Contains(request.Role, StringComparer.Ordinal))
        {
            return BadRequest("Role is invalid.");
        }
        if (!CanAssignRole(actor, request.Role))
        {
            return new(IdentityOutcome.Forbidden);
        }
        if (request.ValidUntilUtc <= request.ValidFromUtc)
        {
            return BadRequest("ValidUntilUtc must be later than ValidFromUtc.");
        }

        ApplicationUser? user = await store.FindByIdAsync(userId);
        if (user is null)
        {
            return new(IdentityOutcome.NotFound);
        }
        string[] assignedRoles = await store.GetOtherActiveRolesAsync(userId, restaurantId, ct);
        if (assignedRoles.Any(role => !string.Equals(role, request.Role, StringComparison.Ordinal)))
        {
            return Conflict("A user can only have one role.");
        }
        if (
            request.IsActive
            && SingleRestaurantRoles.Contains(request.Role, StringComparer.Ordinal)
            && assignedRoles.Length > 0
        )
        {
            return Conflict($"Role {request.Role} can only be assigned to one restaurant.");
        }
        if (GlobalRoles.Contains(request.Role, StringComparer.Ordinal))
        {
            IdentityOperation roleResult = await EnsureGlobalRoleAsync(user, request.Role);
            if (!roleResult.Succeeded)
            {
                return IdentityFailure(roleResult);
            }
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        UserRestaurantAssignment? assignment = await store.FindAssignmentAsync(
            userId,
            restaurantId,
            ct
        );
        if (assignment is null)
        {
            assignment = new UserRestaurantAssignment
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RestaurantId = restaurantId,
            };
            store.AddAssignment(assignment);
        }
        assignment.Role = request.Role;
        assignment.IsActive = request.IsActive;
        assignment.ValidFromUtc = request.ValidFromUtc?.ToUniversalTime() ?? now;
        assignment.ValidUntilUtc = request.ValidUntilUtc?.ToUniversalTime();
        await store.SaveChangesAsync(ct);
        await SynchronizeGlobalRolesAsync(user, ct);
        await store.UpdateSecurityStampAsync(user);
        return Ok(
            new UpdatedRestaurantAssignmentResponse(
                user.Id,
                assignment.RestaurantId,
                assignment.Role,
                assignment.IsActive,
                assignment.ValidFromUtc,
                assignment.ValidUntilUtc
            )
        );
    }

    private async Task<IdentityOperation> EnsureGlobalRoleAsync(ApplicationUser user, string role)
    {
        if (!await store.RoleExistsAsync(role))
        {
            IdentityOperation created = await store.CreateRoleAsync(role);
            if (!created.Succeeded)
            {
                return created;
            }
        }
        return await store.IsInRoleAsync(user, role)
            ? IdentityOperation.Success
            : await store.AddToRoleAsync(user, role);
    }

    private async Task SynchronizeGlobalRolesAsync(ApplicationUser user, CancellationToken ct)
    {
        string[] desired = await store.GetDesiredGlobalRolesAsync(user.Id, GlobalRoles, ct);
        string[] current = (await store.GetRolesAsync(user))
            .Where(x => GlobalRoles.Contains(x, StringComparer.Ordinal))
            .ToArray();
        string[] removed = current.Except(desired, StringComparer.Ordinal).ToArray();
        if (removed.Length > 0)
        {
            IdentityOperation result = await store.RemoveFromRolesAsync(user, removed);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", result.Errors));
            }
        }
        foreach (string role in desired.Except(current, StringComparer.Ordinal))
        {
            IdentityOperation result = await EnsureGlobalRoleAsync(user, role);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Could not synchronize global role {role}.");
            }
        }
    }

    private static bool ValidUserRequest(CreateStaffUserRequest request, out string? error)
    {
        error =
            request.Username.Trim().Length is < 3 or > 80
                ? "Username must contain 3 to 80 characters."
            : request.DisplayName.Trim().Length is < 1 or > 120
                ? "DisplayName must contain 1 to 120 characters."
            : request.Password.Length < 10 ? "Password must contain at least 10 characters."
            : !AllowedRoles.Contains(request.Role, StringComparer.Ordinal) ? "Role is invalid."
            : null;
        return error is null;
    }

    private static bool CanAssignRole(IdentityActor actor, string role) =>
        actor.IsAdmin && AllowedRoles.Contains(role, StringComparer.Ordinal);

    private static RestaurantAssignmentResponse ToRestaurantAccess(
        UserRestaurantAssignment access
    ) =>
        new(
            access.RestaurantId,
            access.Role,
            access.IsActive,
            access.ValidFromUtc,
            access.ValidUntilUtc
        );

    private static IdentityResult Ok(object value) => new(IdentityOutcome.Ok, value);

    private static IdentityResult BadRequest(string detail) =>
        new(IdentityOutcome.BadRequest, new { detail });

    private static IdentityResult Conflict(string detail) =>
        new(IdentityOutcome.Conflict, new { detail });

    private static IdentityResult IdentityFailure(IdentityOperation result) =>
        BadRequest(string.Join("; ", result.Errors));
}
