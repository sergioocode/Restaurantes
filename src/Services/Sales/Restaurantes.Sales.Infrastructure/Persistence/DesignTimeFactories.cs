using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.Sales.Infrastructure.Persistence.Read;
using Restaurantes.Sales.Infrastructure.Persistence.Write;

namespace Restaurantes.Sales.Infrastructure.Persistence;

public sealed class SalesWriteDbContextFactory : IDesignTimeDbContextFactory<SaleWriteDbContext>
{
    public SaleWriteDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<SaleWriteDbContext> options =
            new DbContextOptionsBuilder<SaleWriteDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5432;Database=sales_write;Username=restaurants;Password=restaurants_dev"
                )
                .Options;
        return new SaleWriteDbContext(options);
    }
}

public sealed class SalesReadDbContextFactory : IDesignTimeDbContextFactory<SaleReadDbContext>
{
    public SaleReadDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<SaleReadDbContext> options =
            new DbContextOptionsBuilder<SaleReadDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5432;Database=sales_read;Username=restaurants;Password=restaurants_dev"
                )
                .Options;
        return new SaleReadDbContext(options);
    }
}
