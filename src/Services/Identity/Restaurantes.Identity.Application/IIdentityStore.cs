using Restaurantes.Identity.Domain;

namespace Restaurantes.Identity.Application;

public interface IIdentityStore
{
    Task<AuthenticationSettings> GetSettingsAsync(CancellationToken ct);
    Task<ApplicationUser?> FindByEmailAsync(string provider, string email, CancellationToken ct);
    Task<ApplicationUser?> FindBySubjectAsync(
        string provider,
        string? tenantId,
        string subject,
        CancellationToken ct
    );
    Task<ApplicationUser?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<List<ApplicationUser>> ListUsersAsync(CancellationToken ct);
    Task<bool> AnyActiveAdminForProviderAsync(string provider, CancellationToken ct);
    Task<bool> AnyActiveAdminExceptAsync(Guid id, string provider, CancellationToken ct);
    Task AddUserAsync(ApplicationUser user, CancellationToken ct);
    Task DeleteUserAsync(ApplicationUser user, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
    Task<string> CreateLoginTicketAsync(Guid userId, CancellationToken ct);
    Task<ApplicationUser?> ConsumeLoginTicketAsync(string code, CancellationToken ct);
}

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(ApplicationUser user);
}
