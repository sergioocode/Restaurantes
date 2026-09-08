using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Restaurantes.Security;

namespace Restaurantes.Identity.Api.Write;

public sealed record LoginRequest(string Username, string Password);

public sealed record CreateStaffUserRequest(
    string Username,
    string DisplayName,
    string Password,
    Guid RestaurantId,
    string Role
);

public sealed record AssignRestaurantRequest(
    Guid RestaurantId,
    string Role,
    DateTime? ValidFromUtc = null,
    DateTime? ValidUntilUtc = null
);

public sealed record UpdateRestaurantAssignmentRequest(
    string Role,
    bool IsActive,
    DateTime? ValidFromUtc = null,
    DateTime? ValidUntilUtc = null
);

public static class IdentityEndpoints
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

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/identity");
        group.MapPost("/login", Login);
        group.MapGet("/me", Me).RequireAuthorization();
        group.MapGet("/users", ListUsers).RequireAuthorization();
        group.MapPost("/users", CreateUser).RequireAuthorization();
        group.MapPost("/users/{userId:guid}/restaurants", AssignRestaurant).RequireAuthorization();
        group
            .MapPut(
                "/users/{userId:guid}/restaurants/{restaurantId:guid}",
                UpdateRestaurantAssignment
            )
            .RequireAuthorization();
        group
            .MapDelete(
                "/users/{userId:guid}/restaurants/{restaurantId:guid}",
                DisableRestaurantAssignment
            )
            .RequireAuthorization();
        return app;
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IdentityWriteDbContext db,
        AccessTokenService tokens,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Results.BadRequest(new { detail = "Username and password are required." });
        }

        ApplicationUser? user = await users.FindByNameAsync(request.Username.Trim());
        if (user is null || !user.IsActive)
        {
            return Results.Unauthorized();
        }

        SignInResult verification = await signIn.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true
        );
        if (!verification.Succeeded)
        {
            return Results.Unauthorized();
        }

        await db.Entry(user).Collection(x => x.RestaurantAccesses).LoadAsync(ct);
        DateTime now = time.GetUtcNow().UtcDateTime;
        string[] globalRoles = (await users.GetRolesAsync(user)).ToArray();
        IssuedAccessToken token = tokens.Issue(user, globalRoles);
        return Results.Ok(
            new
            {
                accessToken = token.Value,
                tokenType = "Bearer",
                token.ExpiresAtUtc,
                user = new
                {
                    user.Id,
                    username = user.UserName,
                    user.DisplayName,
                },
                globalRoles,
                restaurants = user
                    .RestaurantAccesses.Where(x => x.IsCurrentlyActive(now))
                    .Select(x => new
                    {
                        x.RestaurantId,
                        x.Role,
                        x.ValidFromUtc,
                        x.ValidUntilUtc,
                        permissions = RestaurantPermissions.ForRole(x.Role),
                    }),
            }
        );
    }

    private static IResult Me(ClaimsPrincipal principal)
    {
        return Results.Ok(
            new
            {
                id = principal.FindFirstValue(ClaimTypes.NameIdentifier),
                name = principal.Identity?.Name,
                globalRoles = principal.FindAll(ClaimTypes.Role).Select(x => x.Value).Distinct(),
                restaurants = principal
                    .FindAll(RestaurantClaimTypes.RestaurantId)
                    .Select(x => x.Value)
                    .Distinct(),
            }
        );
    }

    private static async Task<IResult> ListUsers(
        ClaimsPrincipal principal,
        IdentityWriteDbContext db,
        CancellationToken ct
    )
    {
        if (!CanManageUsers(principal))
        {
            return Results.Forbid();
        }

        List<ApplicationUser> users = await db
            .Users.AsNoTracking()
            .Include(x => x.RestaurantAccesses)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(ct);
        object[] visible = users
            .Select(user => new
            {
                User = user,
                Assignments = user
                    .RestaurantAccesses.Where(x =>
                        principal.CanAccessRestaurant(
                            x.RestaurantId,
                            RestaurantPermissions.IdentityManage
                        )
                    )
                    .OrderBy(x => x.RestaurantId)
                    .ToArray(),
            })
            .Where(x => x.Assignments.Length > 0 || principal.IsInRole("Admin"))
            .Select(x =>
                (object)
                    new
                    {
                        x.User.Id,
                        username = x.User.UserName,
                        x.User.DisplayName,
                        x.User.IsActive,
                        x.User.CreatedAtUtc,
                        restaurants = x.Assignments.Select(a => new
                        {
                            a.RestaurantId,
                            a.Role,
                            a.IsActive,
                            a.ValidFromUtc,
                            a.ValidUntilUtc,
                        }),
                    }
            )
            .ToArray();
        return Results.Ok(visible);
    }

    private static async Task<IResult> CreateUser(
        CreateStaffUserRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        IdentityWriteDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!CanManageRestaurantUsers(principal, request.RestaurantId))
        {
            return Results.Forbid();
        }
        if (
            !ValidUserRequest(
                request.Username,
                request.DisplayName,
                request.Password,
                request.Role,
                out string? error
            )
        )
        {
            return Results.BadRequest(new { detail = error });
        }
        if (!CanAssignRole(principal, request.Role))
        {
            return Results.Forbid();
        }
        if (await users.FindByNameAsync(request.Username.Trim()) is not null)
        {
            return Results.Conflict(new { detail = "Username already exists." });
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
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
        IdentityResult created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return IdentityFailure(created);
        }

        if (GlobalRoles.Contains(request.Role, StringComparer.Ordinal))
        {
            IResult? roleFailure = await EnsureGlobalRoleAsync(user, request.Role, users, roles);
            if (roleFailure is not null)
            {
                return roleFailure;
            }
        }

        db.RestaurantAccesses.Add(
            new UserRestaurantAssignment
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                RestaurantId = request.RestaurantId,
                Role = request.Role,
                ValidFromUtc = now,
            }
        );
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created(
            $"/api/identity/users/{user.Id}",
            new
            {
                user.Id,
                username = user.UserName,
                user.DisplayName,
                request.RestaurantId,
                request.Role,
            }
        );
    }

    private static Task<IResult> AssignRestaurant(
        Guid userId,
        AssignRestaurantRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        IdentityWriteDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        return UpsertRestaurantAssignment(
            userId,
            request.RestaurantId,
            new UpdateRestaurantAssignmentRequest(
                request.Role,
                true,
                request.ValidFromUtc,
                request.ValidUntilUtc
            ),
            principal,
            users,
            roles,
            db,
            time,
            ct
        );
    }

    private static Task<IResult> UpdateRestaurantAssignment(
        Guid userId,
        Guid restaurantId,
        UpdateRestaurantAssignmentRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        IdentityWriteDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        return UpsertRestaurantAssignment(
            userId,
            restaurantId,
            request,
            principal,
            users,
            roles,
            db,
            time,
            ct
        );
    }

    private static async Task<IResult> UpsertRestaurantAssignment(
        Guid userId,
        Guid restaurantId,
        UpdateRestaurantAssignmentRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        IdentityWriteDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!CanManageRestaurantUsers(principal, restaurantId))
        {
            return Results.Forbid();
        }
        if (!AllowedRoles.Contains(request.Role, StringComparer.Ordinal))
        {
            return Results.BadRequest(new { detail = "Role is invalid." });
        }
        if (!CanAssignRole(principal, request.Role))
        {
            return Results.Forbid();
        }
        if (request.ValidUntilUtc <= request.ValidFromUtc)
        {
            return Results.BadRequest(
                new { detail = "ValidUntilUtc must be later than ValidFromUtc." }
            );
        }

        ApplicationUser? user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Results.NotFound();
        }
        string[] assignedRoles = await db
            .RestaurantAccesses.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive && x.RestaurantId != restaurantId)
            .Select(x => x.Role)
            .Distinct()
            .ToArrayAsync(ct);
        if (assignedRoles.Any(role => !string.Equals(role, request.Role, StringComparison.Ordinal)))
        {
            return Results.Conflict(new { detail = "A user can only have one role." });
        }
        if (
            request.IsActive
            && SingleRestaurantRoles.Contains(request.Role, StringComparer.Ordinal)
            && assignedRoles.Length > 0
        )
        {
            return Results.Conflict(
                new { detail = $"Role {request.Role} can only be assigned to one restaurant." }
            );
        }
        if (GlobalRoles.Contains(request.Role, StringComparer.Ordinal))
        {
            IResult? roleFailure = await EnsureGlobalRoleAsync(user, request.Role, users, roles);
            if (roleFailure is not null)
            {
                return roleFailure;
            }
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        UserRestaurantAssignment? assignment = await db.RestaurantAccesses.SingleOrDefaultAsync(
            x => x.UserId == userId && x.RestaurantId == restaurantId,
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
            db.RestaurantAccesses.Add(assignment);
        }
        assignment.Role = request.Role;
        assignment.IsActive = request.IsActive;
        assignment.ValidFromUtc = request.ValidFromUtc?.ToUniversalTime() ?? now;
        assignment.ValidUntilUtc = request.ValidUntilUtc?.ToUniversalTime();
        await db.SaveChangesAsync(ct);
        await SynchronizeGlobalRolesAsync(user, users, roles, db, ct);
        await users.UpdateSecurityStampAsync(user);
        return Results.Ok(
            new
            {
                user.Id,
                assignment.RestaurantId,
                assignment.Role,
                assignment.IsActive,
                assignment.ValidFromUtc,
                assignment.ValidUntilUtc,
            }
        );
    }

    private static async Task<IResult> DisableRestaurantAssignment(
        Guid userId,
        Guid restaurantId,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        IdentityWriteDbContext db,
        CancellationToken ct
    )
    {
        if (!CanManageRestaurantUsers(principal, restaurantId))
        {
            return Results.Forbid();
        }

        UserRestaurantAssignment? assignment = await db.RestaurantAccesses.SingleOrDefaultAsync(
            x => x.UserId == userId && x.RestaurantId == restaurantId,
            ct
        );
        if (assignment is null)
        {
            return Results.NotFound();
        }
        assignment.IsActive = false;
        await db.SaveChangesAsync(ct);
        ApplicationUser? user = await users.FindByIdAsync(userId.ToString());
        if (user is not null)
        {
            await SynchronizeGlobalRolesAsync(user, users, roles, db, ct);
            await users.UpdateSecurityStampAsync(user);
        }
        return Results.NoContent();
    }

    private static bool ValidUserRequest(
        string username,
        string displayName,
        string password,
        string role,
        out string? error
    )
    {
        error =
            username.Trim().Length is < 3 or > 80 ? "Username must contain 3 to 80 characters."
            : displayName.Trim().Length is < 1 or > 120
                ? "DisplayName must contain 1 to 120 characters."
            : password.Length < 10 ? "Password must contain at least 10 characters."
            : !AllowedRoles.Contains(role, StringComparer.Ordinal) ? "Role is invalid."
            : null;
        return error is null;
    }

    private static bool CanManageUsers(ClaimsPrincipal principal)
    {
        return principal.IsInRole("Admin");
    }

    private static bool CanManageRestaurantUsers(ClaimsPrincipal principal, Guid restaurantId)
    {
        return CanManageUsers(principal)
            && principal.CanAccessRestaurant(restaurantId, RestaurantPermissions.IdentityManage);
    }

    private static bool CanAssignRole(ClaimsPrincipal principal, string role)
    {
        return principal.IsInRole("Admin") && AllowedRoles.Contains(role, StringComparer.Ordinal);
    }

    private static async Task<IResult?> EnsureGlobalRoleAsync(
        ApplicationUser user,
        string role,
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole<Guid>> roles
    )
    {
        if (!await roles.RoleExistsAsync(role))
        {
            IdentityResult roleCreated = await roles.CreateAsync(new IdentityRole<Guid>(role));
            if (!roleCreated.Succeeded)
            {
                return IdentityFailure(roleCreated);
            }
        }
        if (!await users.IsInRoleAsync(user, role))
        {
            IdentityResult assigned = await users.AddToRoleAsync(user, role);
            if (!assigned.Succeeded)
            {
                return IdentityFailure(assigned);
            }
        }
        return null;
    }

    private static async Task SynchronizeGlobalRolesAsync(
        ApplicationUser user,
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        IdentityWriteDbContext db,
        CancellationToken ct
    )
    {
        string[] desired = await db
            .RestaurantAccesses.AsNoTracking()
            .Where(x => x.UserId == user.Id && x.IsActive && GlobalRoles.Contains(x.Role))
            .Select(x => x.Role)
            .Distinct()
            .ToArrayAsync(ct);
        string[] current = (await users.GetRolesAsync(user))
            .Where(x => GlobalRoles.Contains(x, StringComparer.Ordinal))
            .ToArray();
        string[] removed = current.Except(desired, StringComparer.Ordinal).ToArray();
        if (removed.Length > 0)
        {
            IdentityResult result = await users.RemoveFromRolesAsync(user, removed);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Join("; ", result.Errors.Select(x => x.Description))
                );
            }
        }
        foreach (string role in desired.Except(current, StringComparer.Ordinal))
        {
            IResult? failure = await EnsureGlobalRoleAsync(user, role, users, roles);
            if (failure is not null)
            {
                throw new InvalidOperationException($"Could not synchronize global role {role}.");
            }
        }
    }

    private static IResult IdentityFailure(IdentityResult result)
    {
        return Results.BadRequest(
            new { detail = string.Join("; ", result.Errors.Select(x => x.Description)) }
        );
    }
}
