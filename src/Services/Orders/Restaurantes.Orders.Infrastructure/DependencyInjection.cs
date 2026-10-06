using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Infrastructure.Integrations;
using Restaurantes.Orders.Infrastructure.Persistence.Read;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.Orders.Infrastructure.Stores;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.Orders.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrdersWriteInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddBusinessTimeZone(configuration);
        Uri cashRegisterBaseAddress = new(
            configuration["CashRegister:BaseAddress"]
                ?? throw new InvalidOperationException("CashRegister:BaseAddress is required.")
        );
        services.AddDbContext<OrderWriteDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("OrdersWrite"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "orders_write")
            )
        );
        services.AddScoped<IOrderWriteStore, OrderWriteStore>();
        services.AddScoped<IKitchenHistoryQuery, Queries.KitchenHistoryQuery>();
        services.AddScoped<IManagementOrderQuery, Queries.ManagementOrderQuery>();
        services.AddScoped<IOrderSettlementQuery, Queries.OrderSettlementQuery>();
        services.AddScoped<IOrderCatalogStore, OrderCatalogStore>();
        services.AddScoped<IOrderPaymentStore, OrderPaymentStore>();
        services.AddHttpClient<IOrderCashRegister, OrderCashRegister>(client =>
            client.BaseAddress = cashRegisterBaseAddress
        );
        return services;
    }

    public static IServiceCollection AddDiningSessionClient(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        Uri diningBaseAddress = new(
            configuration["Dining:BaseAddress"]
                ?? throw new InvalidOperationException("Dining:BaseAddress is required.")
        );
        services.AddSingleton<IDiningSessionStore>(
            new DiningSessionHttpStore(new HttpClient { BaseAddress = diningBaseAddress })
        );
        return services;
    }

    public static IServiceCollection AddOrdersReadInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddDbContext<OrderReadDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("OrdersRead"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "orders_read")
            )
        );
        services.AddScoped<IKitchenOrderReadStore, KitchenOrderReadStore>();
        return services;
    }
}
