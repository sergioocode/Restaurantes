using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Infrastructure.Persistence;
using Restaurantes.CashRegister.Infrastructure.Stores;

namespace Restaurantes.CashRegister.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCashRegisterInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<CashRegisterDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("CashRegisterWrite"))
        );
        services.AddScoped<ICashRegisterStore, CashRegisterStore>();
        return services;
    }

    public static async Task MigrateCashRegisterDatabaseAsync(
        this IServiceProvider services,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        CashRegisterDbContext db =
            scope.ServiceProvider.GetRequiredService<CashRegisterDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
