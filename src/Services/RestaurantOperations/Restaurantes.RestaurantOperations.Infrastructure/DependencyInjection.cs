using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Write;
using Restaurantes.RestaurantOperations.Infrastructure.Stores;

namespace Restaurantes.RestaurantOperations.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRestaurantOperationsWriteInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddDbContext<RestaurantWriteDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("RestaurantOperationsWrite"),
                npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "restaurant_operations")
            )
        );
        services.AddScoped<IRestaurantWriteStore, RestaurantWriteStore>();
        services.AddScoped<ICompanyWriteStore, CompanyWriteStore>();
        return services;
    }

    public static IServiceCollection AddRestaurantOperationsReadInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddDbContext<RestaurantReadDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("RestaurantOperationsRead"),
                npgsql =>
                    npgsql.MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                        "restaurant_operations_read"
                    )
            )
        );
        services.AddScoped<IRestaurantReadStore, RestaurantReadStore>();
        services.AddScoped<ICompanyReadStore, CompanyReadStore>();
        return services;
    }
}
