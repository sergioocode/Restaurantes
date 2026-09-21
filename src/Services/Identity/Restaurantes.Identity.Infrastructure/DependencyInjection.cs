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
            options.UseNpgsql(configuration.GetConnectionString("IdentityWrite"))
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
        string? bootstrapEmail = configuration["IdentityBootstrap:AdminEmail"];
        if (await db.Users.AnyAsync(ct) || string.IsNullOrWhiteSpace(bootstrapEmail))
        {
            return;
        }

        db.Users.Add(
            new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = bootstrapEmail.Trim().ToLowerInvariant(),
                Provider = activeProvider,
                DisplayName = "Administrador",
                Role = "Admin",
                AllRestaurants = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
        settings.ActiveProvider = activeProvider;
        await db.SaveChangesAsync(ct);
    }

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
