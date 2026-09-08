using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.Orders.Infrastructure.Persistence.Read;
using Restaurantes.Orders.Infrastructure.Persistence.Write;

namespace Restaurantes.Orders.Infrastructure.Persistence;

public sealed class OrderWriteDbContextFactory : IDesignTimeDbContextFactory<OrderWriteDbContext>
{
    public OrderWriteDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<OrderWriteDbContext> options =
            new DbContextOptionsBuilder<OrderWriteDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5432;Database=orders_write;Username=restaurants;Password=restaurants_dev"
                )
                .Options;
        return new OrderWriteDbContext(options);
    }
}

public sealed class OrderReadDbContextFactory : IDesignTimeDbContextFactory<OrderReadDbContext>
{
    public OrderReadDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<OrderReadDbContext> options =
            new DbContextOptionsBuilder<OrderReadDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5432;Database=orders_read;Username=restaurants;Password=restaurants_dev"
                )
                .Options;
        return new OrderReadDbContext(options);
    }
}
