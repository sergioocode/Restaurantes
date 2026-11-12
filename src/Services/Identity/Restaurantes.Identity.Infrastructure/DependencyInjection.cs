using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Restaurantes.Identity.Application;
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
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IdentityWriteDbContext db =
            scope.ServiceProvider.GetRequiredService<IdentityWriteDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
