using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Restaurantes.Dining.Application;
using Restaurantes.Dining.Infrastructure.Integrations;
using Restaurantes.Dining.Infrastructure.Persistence;
using Restaurantes.Dining.Infrastructure.Stores;

namespace Restaurantes.Dining.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDiningPersistence(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<DiningDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DiningWrite"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "dining")
            )
        );
        services.AddScoped<IDiningStore, DiningStore>();
        return services;
    }

    public static IServiceCollection AddDiningInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        Uri cashRegisterBaseAddress = new(
            configuration["CashRegister:BaseAddress"]
                ?? throw new InvalidOperationException("CashRegister:BaseAddress is required.")
        );
        Uri paymentsBaseAddress = new(
            configuration["Payments:BaseAddress"]
                ?? throw new InvalidOperationException("Payments:BaseAddress is required.")
        );
        services.AddDiningPersistence(configuration);
        services.AddScoped<IDiningPayments, DiningPayments>();
        services.AddHttpClient<IDiningCashRegister, DiningCashRegister>(client =>
            client.BaseAddress = cashRegisterBaseAddress
        );
        services.AddHttpClient("payments", client => client.BaseAddress = paymentsBaseAddress);
        return services;
    }

    public static async Task MigrateDiningDatabaseAsync(
        this IServiceProvider services,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        DiningDbContext db = scope.ServiceProvider.GetRequiredService<DiningDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
