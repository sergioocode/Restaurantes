using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.Catalog.Infrastructure.Persistence;

public sealed class CatalogWriteDbContextFactory
    : IDesignTimeDbContextFactory<CatalogWriteDbContext>
{
    public CatalogWriteDbContext CreateDbContext(string[] args)
    {
        return new(
            new DbContextOptionsBuilder<CatalogWriteDbContext>()
                .UseNpgsql(
                    VaultConfigurationExtensions.GetDesignTimeConnectionString("CatalogWrite")
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
                    VaultConfigurationExtensions.GetDesignTimeConnectionString("CatalogRead")
                )
                .Options
        );
    }
}
