using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.Sales.Infrastructure.Persistence.Read;
using Restaurantes.Sales.Infrastructure.Persistence.Write;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.Sales.Infrastructure.Persistence;

public sealed class SalesWriteDbContextFactory : IDesignTimeDbContextFactory<SaleWriteDbContext>
{
    public SaleWriteDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<SaleWriteDbContext> options =
            new DbContextOptionsBuilder<SaleWriteDbContext>()
                .UseNpgsql(
                    VaultConfigurationExtensions.GetDesignTimeConnectionString("SalesWrite"),
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "sales_write")
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
                    VaultConfigurationExtensions.GetDesignTimeConnectionString("SalesRead"),
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "sales_read")
                )
                .Options;
        return new SaleReadDbContext(options);
    }
}
