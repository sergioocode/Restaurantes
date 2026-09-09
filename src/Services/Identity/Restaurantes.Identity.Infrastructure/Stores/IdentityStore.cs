using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Restaurantes.Identity.Application;
using Restaurantes.Identity.Domain;
using Restaurantes.Identity.Infrastructure.Persistence;

namespace Restaurantes.Identity.Infrastructure.Stores;

public sealed class IdentityStore(
    IdentityWriteDbContext db,
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    RoleManager<IdentityRole<Guid>> roles
) : IIdentityStore
{
    public Task<ApplicationUser?> FindByNameAsync(string username) =>
        users.FindByNameAsync(username);

    public Task<ApplicationUser?> FindByIdAsync(Guid userId) =>
        users.FindByIdAsync(userId.ToString());

    public async Task<bool> CheckPasswordAsync(ApplicationUser user, string password) =>
        (await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)).Succeeded;

    public Task LoadRestaurantAccessesAsync(ApplicationUser user, CancellationToken ct) =>
        db.Entry(user).Collection(x => x.RestaurantAccesses).LoadAsync(ct);

    public async Task<string[]> GetRolesAsync(ApplicationUser user) =>
        (await users.GetRolesAsync(user)).ToArray();

    public Task<List<ApplicationUser>> ListUsersAsync(CancellationToken ct) =>
        db
            .Users.AsNoTracking()
            .Include(x => x.RestaurantAccesses)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(ct);

    public async Task<IdentityOperation> CreateUserAsync(ApplicationUser user, string password) =>
        ToOperation(await users.CreateAsync(user, password));

    public Task<bool> RoleExistsAsync(string role) => roles.RoleExistsAsync(role);

    public async Task<IdentityOperation> CreateRoleAsync(string role) =>
        ToOperation(await roles.CreateAsync(new IdentityRole<Guid>(role)));

    public Task<bool> IsInRoleAsync(ApplicationUser user, string role) =>
        users.IsInRoleAsync(user, role);

    public async Task<IdentityOperation> AddToRoleAsync(ApplicationUser user, string role) =>
        ToOperation(await users.AddToRoleAsync(user, role));

    public async Task<IdentityOperation> RemoveFromRolesAsync(
        ApplicationUser user,
        IEnumerable<string> rolesToRemove
    ) => ToOperation(await users.RemoveFromRolesAsync(user, rolesToRemove));

    public async Task<IdentityOperation> UpdateSecurityStampAsync(ApplicationUser user) =>
        ToOperation(await users.UpdateSecurityStampAsync(user));

    public Task<string[]> GetOtherActiveRolesAsync(
        Guid userId,
        Guid restaurantId,
        CancellationToken ct
    ) =>
        db
            .RestaurantAccesses.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive && x.RestaurantId != restaurantId)
            .Select(x => x.Role)
            .Distinct()
            .ToArrayAsync(ct);

    public Task<UserRestaurantAssignment?> FindAssignmentAsync(
        Guid userId,
        Guid restaurantId,
        CancellationToken ct
    ) =>
        db.RestaurantAccesses.SingleOrDefaultAsync(
            x => x.UserId == userId && x.RestaurantId == restaurantId,
            ct
        );

    public Task<string[]> GetDesiredGlobalRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> globalRoles,
        CancellationToken ct
    ) =>
        db
            .RestaurantAccesses.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive && globalRoles.Contains(x.Role))
            .Select(x => x.Role)
            .Distinct()
            .ToArrayAsync(ct);

    public void AddAssignment(UserRestaurantAssignment assignment) =>
        db.RestaurantAccesses.Add(assignment);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<IIdentityTransaction> BeginTransactionAsync(CancellationToken ct) =>
        new IdentityTransaction(await db.Database.BeginTransactionAsync(ct));

    private static IdentityOperation ToOperation(
        Microsoft.AspNetCore.Identity.IdentityResult result
    ) => new(result.Succeeded, result.Errors.Select(x => x.Description).ToArray());

    private sealed class IdentityTransaction(IDbContextTransaction transaction)
        : IIdentityTransaction
    {
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
