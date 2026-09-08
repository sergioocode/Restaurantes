using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Write;

namespace Restaurantes.RestaurantOperations.Infrastructure.Persistence;

public sealed class RestaurantWriteDbContextFactory
    : IDesignTimeDbContextFactory<RestaurantWriteDbContext>
{
    public RestaurantWriteDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<RestaurantWriteDbContext> options =
            new DbContextOptionsBuilder<RestaurantWriteDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5432;Database=restaurant_operations_write;Username=restaurants;Password=restaurants_dev"
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
                    "Host=localhost;Port=5432;Database=restaurant_operations_read;Username=restaurants;Password=restaurants_dev"
                )
                .Options;
        return new RestaurantReadDbContext(options);
    }
}
