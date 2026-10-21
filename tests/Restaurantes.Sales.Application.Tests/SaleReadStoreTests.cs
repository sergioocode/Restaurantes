using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Sales.Infrastructure.Persistence.Read;
using Restaurantes.Sales.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Sales.Application.Tests;

public sealed class SaleReadStoreTests
{
    [Fact]
    public async Task Find_returns_null_for_missing_sale_and_maps_all_captured_details()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using SaleReadDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid saleId = Guid.NewGuid();
        db.Sales.Add(
            new SaleReadModel
            {
                Id = saleId,
                RestaurantId = restaurantId,
                Source = "Pos",
                PaymentMethod = "Mixed",
                Total = 24m,
                TipTotal = 2m,
                DiscountName = "Comida de personal",
                DiscountPercentage = 10m,
                DiscountAmount = 2m,
                Status = "Completed",
                OrderCount = 1,
                CompletedAtUtc = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc),
                OrderIdsJson = JsonSerializer.Serialize(new[] { orderId }),
                LinesJson = JsonSerializer.Serialize(
                    new[] { new SaleLineSnapshot(productId, "Pasta", 24m, 1, "Pastas", "CHEF") }
                ),
                PaymentMethodsJson = JsonSerializer.Serialize(
                    new[] { new SalePaymentBreakdown("Card", 1, 24m) }
                ),
            }
        );
        await db.SaveChangesAsync(ct);
        SaleReadStore store = new(db, TimeZoneInfo.Utc);

        Assert.Null(await store.FindAsync(Guid.NewGuid(), ct));
        var sale = await store.FindAsync(saleId, ct);

        Assert.NotNull(sale);
        Assert.Equal(restaurantId, sale.RestaurantId);
        Assert.Equal("Pos", sale.Source);
        Assert.Equal("Mixed", sale.PaymentMethod);
        Assert.Equal(24m, sale.Total);
        Assert.Equal(2m, sale.TipTotal);
        Assert.Equal("Comida de personal", sale.DiscountName);
        Assert.Equal(10m, sale.DiscountPercentage);
        Assert.Equal(2m, sale.DiscountAmount);
        Assert.Equal(new[] { orderId }, sale.OrderIds);
        Assert.Equal(productId, Assert.Single(sale.Lines).ProductId);
        SalePaymentBreakdown payment = Assert.Single(sale.PaymentMethods!);
        Assert.Equal("Card", payment.Method);
        Assert.Equal(24m, payment.Amount);
    }

    [Fact]
    public async Task List_filters_by_restaurant_and_local_date_orders_newest_first_and_maps_empty_json()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using SaleReadDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        DateOnly date = new(2026, 10, 21);
        SaleReadModel newest = Sale(
            restaurantId,
            new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc),
            date
        );
        SaleReadModel fallbackDate = Sale(
            restaurantId,
            new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc),
            null
        );
        db.Sales.AddRange(
            newest,
            fallbackDate,
            Sale(restaurantId, new DateTime(2026, 10, 20, 21, 59, 59, DateTimeKind.Utc), null),
            Sale(Guid.NewGuid(), new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc), date)
        );
        await db.SaveChangesAsync(ct);
        SaleReadStore store = new(db, TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time"));

        IReadOnlyList<Restaurantes.Sales.Contracts.Responses.SaleResponse> result =
            await store.ListAsync(restaurantId, date, -5, ct);

        Assert.Equal(new[] { newest.Id, fallbackDate.Id }, result.Select(item => item.Id));
        Assert.Empty(result[0].OrderIds);
        Assert.Empty(result[0].Lines);
        Assert.Empty(result[0].PaymentMethods!);

        IReadOnlyList<Restaurantes.Sales.Contracts.Responses.SaleResponse> all =
            await store.ListAsync(null, null, 1, ct);
        Assert.Equal(3, all.Count);
        Assert.Equal(fallbackDate.Id, all[1].Id);
    }

    private static SaleReadModel Sale(
        Guid restaurantId,
        DateTime completedAtUtc,
        DateOnly? businessDate
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Source = "Pos",
            PaymentMethod = "Cash",
            Total = 5m,
            Status = "Completed",
            OrderCount = 1,
            CompletedAtUtc = completedAtUtc,
            BusinessDate = businessDate,
        };

    private static SaleReadDbContext CreateDatabase() =>
        new(
            new DbContextOptionsBuilder<SaleReadDbContext>()
                .UseInMemoryDatabase("sale-read-store-" + Guid.NewGuid().ToString("N"))
                .Options
        );
}
