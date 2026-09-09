using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Restaurantes.CashRegister.Infrastructure.Persistence;

public sealed class CashRegisterDbContextFactory
    : IDesignTimeDbContextFactory<CashRegisterDbContext>
{
    public CashRegisterDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<CashRegisterDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5432;Database=cash_register_write;Username=restaurants;Password=restaurants_dev"
                )
                .Options
        );
}
