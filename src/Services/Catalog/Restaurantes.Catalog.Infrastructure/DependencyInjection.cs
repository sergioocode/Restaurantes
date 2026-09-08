using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Catalog.Infrastructure.Stores;

namespace Restaurantes.Catalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogWriteInfrastructure(
        this IServiceCollection s,
        IConfiguration c
    )
    {
        s.AddDbContext<CatalogWriteDbContext>(o =>
            o.UseNpgsql(c.GetConnectionString("CatalogWrite"))
        );
        s.AddScoped<ICatalogWriteStore, CatalogWriteStore>();
        return s;
    }

    public static IServiceCollection AddCatalogReadInfrastructure(
        this IServiceCollection s,
        IConfiguration c
    )
    {
        s.AddDbContext<CatalogReadDbContext>(o =>
            o.UseNpgsql(c.GetConnectionString("CatalogRead"))
        );
        s.AddScoped<ICatalogReadStore, CatalogReadStore>();
        return s;
    }
}
