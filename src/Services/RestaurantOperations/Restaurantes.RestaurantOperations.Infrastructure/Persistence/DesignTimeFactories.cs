using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Write;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.RestaurantOperations.Infrastructure.Persistence;

public sealed class RestaurantWriteDbContextFactory
    : IDesignTimeDbContextFactory<RestaurantWriteDbContext>
{
    public RestaurantWriteDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<RestaurantWriteDbContext> options =
            new DbContextOptionsBuilder<RestaurantWriteDbContext>()
                .UseNpgsql(
                    VaultConfigurationExtensions.GetDesignTimeConnectionString(
                        "RestaurantOperationsWrite"
                    )
                )
                .Options;
        return new RestaurantWriteDbContext(options);
    }
}

public sealed class RestaurantReadDbContextFactory
    : IDesignTimeDbContextFactory<RestaurantReadDbContext>
{
    public RestaurantReadDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<RestaurantReadDbContext> options =
            new DbContextOptionsBuilder<RestaurantReadDbContext>()
                .UseNpgsql(
                    VaultConfigurationExtensions.GetDesignTimeConnectionString(
                        "RestaurantOperationsRead"
                    )
                )
                .Options;
        return new RestaurantReadDbContext(options);
    }
}
