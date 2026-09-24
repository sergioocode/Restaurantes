using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.Orders.Infrastructure.Persistence.Read;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.Orders.Infrastructure.Persistence;

public sealed class OrderWriteDbContextFactory : IDesignTimeDbContextFactory<OrderWriteDbContext>
{
    public OrderWriteDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<OrderWriteDbContext> options =
            new DbContextOptionsBuilder<OrderWriteDbContext>()
                .UseNpgsql(
                    VaultConfigurationExtensions.GetDesignTimeConnectionString("OrdersWrite")
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
                .UseNpgsql(VaultConfigurationExtensions.GetDesignTimeConnectionString("OrdersRead"))
                .Options;
        return new OrderReadDbContext(options);
    }
}
