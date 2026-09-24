using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.CashRegister.Infrastructure.Persistence;

public sealed class CashRegisterDbContextFactory
    : IDesignTimeDbContextFactory<CashRegisterDbContext>
{
    public CashRegisterDbContext CreateDbContext(string[] args)
    {
        return new(
            new DbContextOptionsBuilder<CashRegisterDbContext>()
                .UseNpgsql(
                    VaultConfigurationExtensions.GetDesignTimeConnectionString("CashRegisterWrite")
                )
                .Options
        );
    }
}
