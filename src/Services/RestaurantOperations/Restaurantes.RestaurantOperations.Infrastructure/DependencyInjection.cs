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
            options.UseNpgsql(configuration.GetConnectionString("RestaurantOperationsWrite"))
        );
        services.AddScoped<IRestaurantWriteStore, RestaurantWriteStore>();
        return services;
    }

    public static IServiceCollection AddRestaurantOperationsReadInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddDbContext<RestaurantReadDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("RestaurantOperationsRead"))
        );
        services.AddScoped<IRestaurantReadStore, RestaurantReadStore>();
        return services;
    }
}
