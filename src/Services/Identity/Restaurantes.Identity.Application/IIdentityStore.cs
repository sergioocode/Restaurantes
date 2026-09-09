using Restaurantes.Identity.Domain;

namespace Restaurantes.Identity.Application;

public interface IIdentityStore
{
    Task<ApplicationUser?> FindByNameAsync(string username);
    Task<ApplicationUser?> FindByIdAsync(Guid userId);
    Task<bool> CheckPasswordAsync(ApplicationUser user, string password);
    Task LoadRestaurantAccessesAsync(ApplicationUser user, CancellationToken ct);
    Task<string[]> GetRolesAsync(ApplicationUser user);
    Task<List<ApplicationUser>> ListUsersAsync(CancellationToken ct);
    Task<IdentityOperation> CreateUserAsync(ApplicationUser user, string password);
    Task<bool> RoleExistsAsync(string role);
    Task<IdentityOperation> CreateRoleAsync(string role);
    Task<bool> IsInRoleAsync(ApplicationUser user, string role);
    Task<IdentityOperation> AddToRoleAsync(ApplicationUser user, string role);
    Task<IdentityOperation> RemoveFromRolesAsync(ApplicationUser user, IEnumerable<string> roles);
    Task<IdentityOperation> UpdateSecurityStampAsync(ApplicationUser user);
    Task<string[]> GetOtherActiveRolesAsync(Guid userId, Guid restaurantId, CancellationToken ct);
    Task<UserRestaurantAssignment?> FindAssignmentAsync(
        Guid userId,
        Guid restaurantId,
        CancellationToken ct
    );
    Task<string[]> GetDesiredGlobalRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> globalRoles,
        CancellationToken ct
    );
    void AddAssignment(UserRestaurantAssignment assignment);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<IIdentityTransaction> BeginTransactionAsync(CancellationToken ct);
}

public sealed record IdentityOperation(bool Succeeded, IReadOnlyCollection<string> Errors)
{
    public static IdentityOperation Success { get; } = new(true, []);
}

public interface IIdentityTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(ApplicationUser user, IReadOnlyCollection<string> globalRoles);
}
