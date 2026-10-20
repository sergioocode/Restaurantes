using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Sales.Application;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Sales.Domain;
using Restaurantes.Sales.Infrastructure.Persistence.Write;
using Restaurantes.Sales.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Sales.Application.Tests;

public sealed class SaleTicketQueryServiceTests
{
    [Fact]
    public async Task Ticket_is_read_from_completed_write_model_before_projection_runs()
    {
        Guid saleId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        DateTime completedAtUtc = DateTime.UtcNow;
        Sale sale = new()
        {
            Id = saleId,
            RestaurantId = restaurantId,
            Source = "Pos",
            PaymentMethod = "Mixed",
            Status = "Completed",
            Total = 25m,
            CreatedAtUtc = completedAtUtc,
            CompletedAtUtc = completedAtUtc,
            Orders =
            [
                new SaleOrder
                {
                    SaleId = saleId,
                    OrderId = orderId,
                    Amount = 25m,
                    IsPaid = true,
                    IsDelivered = true,
                    LinesJson = JsonSerializer.Serialize(
                        new[]
                        {
                            new SaleLineSnapshot(
                                Guid.NewGuid(),
                                "Tortilla",
                                25m,
                                1,
                                "Entrantes",
                                "KITCHEN"
                            ),
                        }
                    ),
                },
            ],
            Payments =
            [
                new SalePayment
                {
                    PaymentId = Guid.NewGuid(),
                    SaleId = saleId,
                    TransactionId = Guid.NewGuid(),
                    Method = "Cash",
                    Amount = 10m,
                    TipAmount = 1m,
                },
                new SalePayment
                {
                    PaymentId = Guid.NewGuid(),
                    SaleId = saleId,
                    TransactionId = Guid.NewGuid(),
                    Method = "Card",
                    Amount = 15m,
                },
            ],
        };
        DbContextOptions<SaleWriteDbContext> options =
            new DbContextOptionsBuilder<SaleWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        await using SaleWriteDbContext db = new(options);
        db.Sales.Add(sale);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        SaleTicketQueryService service = new(new SaleWriteStore(db));
        var ticket = await service.FindAsync(saleId, TestContext.Current.CancellationToken);

        Assert.NotNull(ticket);
        Assert.Equal(saleId, ticket.Id);
        Assert.Equal(restaurantId, ticket.RestaurantId);
        Assert.Equal("Completed", ticket.Status);
        Assert.Equal(25m, ticket.Total);
        Assert.Equal(completedAtUtc, ticket.CompletedAtUtc);
        Assert.Equal([orderId], ticket.OrderIds);
        Assert.Equal("Tortilla", Assert.Single(ticket.Lines).ProductName);
        Assert.Equal(1m, ticket.TipTotal);
        Assert.Equal(10m, Assert.Single(ticket.PaymentMethods!, x => x.Method == "Cash").Amount);
        Assert.Equal(15m, Assert.Single(ticket.PaymentMethods!, x => x.Method == "Card").Amount);
    }

    [Fact]
    public async Task Ticket_is_not_available_until_sale_is_completed()
    {
        DbContextOptions<SaleWriteDbContext> options =
            new DbContextOptionsBuilder<SaleWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        await using SaleWriteDbContext db = new(options);
        Guid saleId = Guid.NewGuid();
        db.Sales.Add(
            new Sale
            {
                Id = saleId,
                RestaurantId = Guid.NewGuid(),
                Source = "Pos",
                PaymentMethod = "Cash",
                Status = "Pending",
            }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        SaleTicketQueryService service = new(new SaleWriteStore(db));

        Assert.Null(await service.FindAsync(saleId, TestContext.Current.CancellationToken));
    }
}
