using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.Payments.Infrastructure.Persistence.Write;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.Payments.Infrastructure.Persistence;

public sealed class PaymentWriteDbContextFactory
    : IDesignTimeDbContextFactory<PaymentWriteDbContext>
{
    public PaymentWriteDbContext CreateDbContext(string[] args)
    {
        return new(
            new DbContextOptionsBuilder<PaymentWriteDbContext>()
                .UseNpgsql(
                    VaultConfigurationExtensions.GetDesignTimeConnectionString("PaymentsWrite")
                )
                .Options
        );
    }
}
