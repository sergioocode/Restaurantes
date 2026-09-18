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
        if (!await db.AuthenticationSettings.AnyAsync(ct))
        {
            db.AuthenticationSettings.Add(
                new AuthenticationSettings { Id = 1, ActiveProvider = "Microsoft" }
            );
            await db.SaveChangesAsync(ct);
        }
        string? bootstrapEmail = configuration["IdentityBootstrap:AdminEmail"];
        string? bootstrapProvider = configuration["IdentityBootstrap:Provider"];
        if (await db.Users.AnyAsync(ct) || string.IsNullOrWhiteSpace(bootstrapEmail))
        {
            return;
        }

        if (bootstrapProvider is not ("Microsoft" or "Google"))
        {
            throw new InvalidOperationException(
                "IdentityBootstrap:Provider must be Microsoft or Google."
            );
        }

        db.Users.Add(
            new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = bootstrapEmail.Trim().ToLowerInvariant(),
                Provider = bootstrapProvider,
                DisplayName = "Administrador",
                Role = "Admin",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
        AuthenticationSettings settings = await db.AuthenticationSettings.SingleAsync(ct);
        settings.ActiveProvider = bootstrapProvider;
        await db.SaveChangesAsync(ct);
    }
}
