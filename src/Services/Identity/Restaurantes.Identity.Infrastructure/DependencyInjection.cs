using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Restaurantes.Identity.Application;
using Restaurantes.Identity.Domain;
using Restaurantes.Identity.Infrastructure.Persistence;
using Restaurantes.Identity.Infrastructure.Security;
using Restaurantes.Identity.Infrastructure.Stores;

namespace Restaurantes.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<IdentityWriteDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("IdentityWrite"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity")
            )
        );
        services.AddScoped<IIdentityStore, IdentityStore>();
        services.AddScoped<IAccessTokenIssuer, AccessTokenService>();
        return services;
    }

    public static async Task InitializeIdentityDatabaseAsync(
        this IServiceProvider services,
        IConfiguration configuration,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IdentityWriteDbContext db =
            scope.ServiceProvider.GetRequiredService<IdentityWriteDbContext>();
        await db.Database.MigrateAsync(ct);
        string activeProvider = ReadActiveProvider(configuration);
        AuthenticationSettings settings;
        if (await db.AuthenticationSettings.SingleOrDefaultAsync(x => x.Id == 1, ct) is null)
        {
            settings = new AuthenticationSettings { Id = 1, ActiveProvider = activeProvider };
            db.AuthenticationSettings.Add(settings);
        }
        else
        {
            settings = await db.AuthenticationSettings.SingleAsync(x => x.Id == 1, ct);
            settings.ActiveProvider = activeProvider;
        }
        await db.SaveChangesAsync(ct);
        List<BootstrapAccount> bootstrapAccounts = GetBootstrapAccounts(configuration);
        foreach (BootstrapAccount account in bootstrapAccounts)
        {
            ApplicationUser? user = await db.Users.SingleOrDefaultAsync(
                x => x.Provider == activeProvider && x.Email == account.Email,
                ct
            );
            if (user is null)
            {
                db.Users.Add(
                    new ApplicationUser
                    {
                        Id = Guid.NewGuid(),
                        Email = account.Email,
                        Provider = activeProvider,
                        DisplayName = account.DisplayName,
                        Role = account.Role,
                        AllRestaurants = true,
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow,
                    }
                );
                continue;
            }

            user.DisplayName = account.DisplayName;
            user.Role = account.Role;
            user.AllRestaurants = true;
            user.RestaurantId = null;
            user.IsActive = true;
        }

        await db.SaveChangesAsync(ct);
    }

    private static List<BootstrapAccount> GetBootstrapAccounts(IConfiguration configuration)
    {
        (string Key, string Role, string DisplayName)[] configuredRoles =
        [
            ("AdminEmail", "Admin", "Administrador"),
            ("GerenteEmail", "Gerente", "Gerencia"),
            ("ContabilidadEmail", "Contabilidad", "Contabilidad"),
            ("MarketingEmail", "Marketing", "Marketing"),
        ];
        List<BootstrapAccount> accounts = [];
        foreach ((string key, string role, string displayName) in configuredRoles)
        {
            string[] emails = (configuration[$"IdentityBootstrap:{key}"] ?? string.Empty)
                .Split(
                    [',', ';'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .Select(email => email.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            accounts.AddRange(
                emails.Select(email => new BootstrapAccount(email, role, displayName))
            );
        }

        BootstrapAccount? duplicate = accounts
            .GroupBy(account => account.Email, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(account => account.Role).Distinct().Skip(1).Any())
            .Select(group => group.First())
            .FirstOrDefault();
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"IdentityBootstrap asigna más de un rol a {duplicate.Email}."
            );
        }

        return accounts
            .DistinctBy(account => account.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record BootstrapAccount(string Email, string Role, string DisplayName);

    private static string ReadActiveProvider(IConfiguration configuration)
    {
        string? provider = configuration["ExternalAuth:Provider"];
        return provider is "Microsoft" or "Google"
            ? provider
            : throw new InvalidOperationException(
                "ExternalAuth:Provider must be Microsoft or Google."
            );
    }
}
