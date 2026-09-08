using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Restaurantes.Payments.Infrastructure.Persistence.Write;

namespace Restaurantes.Payments.Infrastructure.Persistence;

public sealed class PaymentWriteDbContextFactory
    : IDesignTimeDbContextFactory<PaymentWriteDbContext>
{
    public PaymentWriteDbContext CreateDbContext(string[] args)
    {
        return new(
            new DbContextOptionsBuilder<PaymentWriteDbContext>()
                .UseNpgsql(
                    "Host=localhost;Port=5432;Database=payments_write;Username=restaurants;Password=restaurants_dev"
                )
                .Options
        );
    }
}
