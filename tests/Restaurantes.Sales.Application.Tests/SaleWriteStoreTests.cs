using Microsoft.EntityFrameworkCore;
using Restaurantes.Sales.Domain;
using Restaurantes.Sales.Infrastructure.Persistence.Write;
using Restaurantes.Sales.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Sales.Application.Tests;

public sealed class SaleWriteStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Finds_the_sale_with_all_orders_and_payments(bool byOrder)
    {
        DbContextOptions<SaleWriteDbContext> options =
            new DbContextOptionsBuilder<SaleWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        Guid saleId = Guid.NewGuid();
        Guid firstOrder = Guid.NewGuid();
        Guid secondOrder = Guid.NewGuid();
        await using (SaleWriteDbContext writer = new(options))
        {
            writer.Sales.Add(
                new Sale
                {
                    Id = saleId,
                    Orders =
                    [
                        new() { OrderId = firstOrder, Amount = 10 },
                        new() { OrderId = secondOrder, Amount = 20 },
                    ],
                    Payments =
                    [
                        new()
                        {
                            PaymentId = Guid.NewGuid(),
                            Method = "Cash",
                            Amount = 5,
                        },
                        new()
                        {
                            PaymentId = Guid.NewGuid(),
                            Method = "Card",
                            Amount = 25,
                        },
                    ],
                }
            );
            writer.Sales.Add(new Sale { Id = Guid.NewGuid() });
            await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using SaleWriteDbContext reader = new(options);
        SaleWriteStore store = new(reader);

        Sale? result = byOrder
            ? await store.FindByOrderIdAsync(firstOrder, TestContext.Current.CancellationToken)
            : await store.FindAsync(saleId, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(saleId, result.Id);
        Assert.Equal(2, result.Orders.Count);
        Assert.Contains(result.Orders, order => order.OrderId == secondOrder);
        Assert.Equal(30m, result.Orders.Sum(order => order.Amount));
        Assert.Equal(2, result.Payments.Count);
        Assert.Equal(30m, result.Payments.Sum(payment => payment.Amount));
        Assert.Null(await store.FindAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
        Assert.Null(
            await store.FindByOrderIdAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)
        );
    }
}
