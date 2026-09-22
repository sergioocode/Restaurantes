using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Identity.Application;
using Restaurantes.Identity.Domain;
using Restaurantes.Identity.Infrastructure.Persistence;

namespace Restaurantes.Identity.Infrastructure.Stores;

public sealed class IdentityStore(IdentityWriteDbContext db, TimeProvider time) : IIdentityStore
{
    public async Task<AuthenticationSettings> GetSettingsAsync(CancellationToken ct)
    {
        return await db.AuthenticationSettings.SingleAsync(x => x.Id == 1, ct);
    }

    public Task<ApplicationUser?> FindByEmailAsync(
        string provider,
        string email,
        CancellationToken ct
    )
    {
        return db.Users.SingleOrDefaultAsync(x => x.Provider == provider && x.Email == email, ct);
    }

    public Task<ApplicationUser?> FindBySubjectAsync(
        string provider,
        string? tenantId,
        string subject,
        CancellationToken ct
    )
    {
        return db.Users.SingleOrDefaultAsync(
            x => x.Provider == provider && x.TenantId == tenantId && x.ProviderSubject == subject,
            ct
        );
    }

    public Task<ApplicationUser?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<List<ApplicationUser>> ListUsersAsync(CancellationToken ct)
    {
        return db.Users.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(ct);
    }

    public Task<bool> AnyActiveAdminForProviderAsync(string provider, CancellationToken ct)
    {
        return db.Users.AnyAsync(
            x => x.Provider == provider && x.IsActive && x.Role == "Admin",
            ct
        );
    }

    public Task<bool> AnyActiveAdminExceptAsync(Guid id, string provider, CancellationToken ct)
    {
        return db.Users.AnyAsync(
            x => x.Id != id && x.Provider == provider && x.IsActive && x.Role == "Admin",
            ct
        );
    }

    public async Task AddUserAsync(ApplicationUser user, CancellationToken ct)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteUserAsync(ApplicationUser user, CancellationToken ct)
    {
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return db.SaveChangesAsync(ct);
    }

    public async Task<string> CreateLoginTicketAsync(Guid userId, CancellationToken ct)
    {
        string code = Convert
            .ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        db.LoginTickets.Add(
            new LoginTicket
            {
                CodeHash = Hash(code),
                UserId = userId,
                ExpiresAtUtc = time.GetUtcNow().UtcDateTime.AddMinutes(2),
            }
        );
        await db.SaveChangesAsync(ct);
        return code;
    }

    public async Task<ApplicationUser?> ConsumeLoginTicketAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 100)
        {
            return null;
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        string hash = Hash(code);
        int consumed = await db
            .LoginTickets.Where(x =>
                x.CodeHash == hash && x.ConsumedAtUtc == null && x.ExpiresAtUtc > now
            )
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ConsumedAtUtc, now), ct);
        if (consumed != 1)
        {
            return null;
        }

        Guid userId = await db
            .LoginTickets.Where(x => x.CodeHash == hash)
            .Select(x => x.UserId)
            .SingleAsync(ct);
        return await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
    }

    private static string Hash(string code)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
    }
}
