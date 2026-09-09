using Microsoft.AspNetCore.Identity;
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
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Password.RequiredLength = 10;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentityWriteDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.AddScoped<IIdentityStore, IdentityStore>();
        services.AddScoped<IAccessTokenIssuer, AccessTokenService>();
        services.AddScoped<IdentitySeed>();
        return services;
    }

    public static async Task InitializeIdentityDatabaseAsync(
        this IServiceProvider services,
        bool seed,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IdentityWriteDbContext db =
            scope.ServiceProvider.GetRequiredService<IdentityWriteDbContext>();
        await db.Database.MigrateAsync(ct);
        if (seed)
        {
            await scope.ServiceProvider.GetRequiredService<IdentitySeed>().SeedAsync(ct);
        }
    }
}
