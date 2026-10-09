using Microsoft.EntityFrameworkCore;
using Restaurantes.Sales.Domain;
using Restaurantes.Sales.Infrastructure.Persistence.Write;
using Xunit;

namespace Restaurantes.Sales.Application.Tests;

public sealed class SaleWriteDbContextTests
{
    [Fact]
    public async Task New_external_payment_added_to_tracked_sale_is_inserted()
    {
        DbContextOptions<SaleWriteDbContext> options =
            new DbContextOptionsBuilder<SaleWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        await using SaleWriteDbContext db = new(options);
        Sale sale = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            Source = "Pos",
            PaymentMethod = "Cash",
            CreatedAtUtc = DateTime.UtcNow,
        };
        sale.Payments.Add(
            new SalePayment
            {
                PaymentId = Guid.NewGuid(),
                SaleId = sale.Id,
                TransactionId = Guid.NewGuid(),
                Method = "Cash",
                Amount = 1m,
            }
        );
        db.Sales.Add(sale);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Sale persistedSale = await db
            .Sales.Include(item => item.Payments)
            .SingleAsync(TestContext.Current.CancellationToken);
        SalePayment nextPayment = new()
        {
            PaymentId = Guid.NewGuid(),
            SaleId = persistedSale.Id,
            TransactionId = Guid.NewGuid(),
            Method = "Card",
            Amount = 2m,
        };
        persistedSale.Payments.Add(nextPayment);
        db.ChangeTracker.DetectChanges();

        Assert.Equal(EntityState.Added, db.Entry(nextPayment).State);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, await db.SalePayments.CountAsync(TestContext.Current.CancellationToken));
    }
}
