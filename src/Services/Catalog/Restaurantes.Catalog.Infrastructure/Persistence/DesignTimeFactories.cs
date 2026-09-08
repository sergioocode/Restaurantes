using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;

namespace Restaurantes.Catalog.Infrastructure.Persistence;

public sealed class CatalogWriteDbContextFactory
    : IDesignTimeDbContextFactory<CatalogWriteDbContext>
{
    public CatalogWriteDbContext CreateDbContext(string[] args)
    {
        return new(
            new DbContextOptionsBuilder<CatalogWriteDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5432;Database=catalog_write;Username=restaurants;Password=restaurants_dev"
                )
                .Options
        );
    }
}

public sealed class CatalogReadDbContextFactory : IDesignTimeDbContextFactory<CatalogReadDbContext>
{
    public CatalogReadDbContext CreateDbContext(string[] args)
    {
        return new(
            new DbContextOptionsBuilder<CatalogReadDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5432;Database=catalog_read;Username=restaurants;Password=restaurants_dev"
                )
                .Options
        );
    }
}
