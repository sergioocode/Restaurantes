using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Sales.Application;
using Restaurantes.Sales.Infrastructure.Persistence.Read;
using Restaurantes.Sales.Infrastructure.Persistence.Write;
using Restaurantes.Sales.Infrastructure.Stores;

namespace Restaurantes.Sales.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSalesWriteInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddDbContext<SaleWriteDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("SalesWrite"))
        );
        services.AddScoped<ISaleWriteStore, SaleWriteStore>();
        return services;
    }

    public static IServiceCollection AddSalesReadInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddDbContext<SaleReadDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("SalesRead"))
        );
        services.AddScoped<ISaleReadStore, SaleReadStore>();
        return services;
    }
}
