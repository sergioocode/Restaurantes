using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Infrastructure.Integrations;
using Restaurantes.CashRegister.Infrastructure.Persistence;
using Restaurantes.CashRegister.Infrastructure.Stores;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.CashRegister.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCashRegisterInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        Uri ordersBaseAddress = new(
            configuration["Orders:BaseAddress"]
                ?? throw new InvalidOperationException("Orders:BaseAddress is required.")
        );
        Uri paymentsBaseAddress = new(
            configuration["Payments:BaseAddress"]
                ?? throw new InvalidOperationException("Payments:BaseAddress is required.")
        );
        services.TryAddSingleton(TimeProvider.System);
        services.AddBusinessTimeZone(configuration);
        services.AddDbContext<CashRegisterDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("CashRegisterWrite"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "cash_register")
            )
        );
        services.AddScoped<ICashRegisterStore, CashRegisterStore>();
        services.AddHttpClient<IIncompleteOrdersChecker, OrdersIncompleteOrdersChecker>(client =>
        {
            client.BaseAddress = ordersBaseAddress;
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddHttpClient(
            "CashClosePayments",
            client =>
            {
                client.BaseAddress = paymentsBaseAddress;
                client.Timeout = TimeSpan.FromSeconds(5);
            }
        );
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
