using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Reporting.Application.Dashboard;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;
using Restaurantes.Reporting.Infrastructure.Queries;
using Restaurantes.Reporting.Infrastructure.Workers;
using Restaurantes.Sales.Contracts.Events;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class ReportingProjectionHandlerTests
{
    [Fact]
    public async Task Kitchen_ticket_ready_uses_ready_line_timestamp_while_station_is_incomplete()
    {
        DbContextOptions<ReportingReadDbContext> options =
            new DbContextOptionsBuilder<ReportingReadDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        await using ReportingReadDbContext db = new(options);
        ReportingProjectionHandler handler = new(db, TimeProvider.System);
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid readyLineId = Guid.NewGuid();
        Guid pendingLineId = Guid.NewGuid();
        DateTime createdAtUtc = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        DateTime readyAtUtc = createdAtUtc.AddMinutes(2);

        await ProjectAsync(
            handler,
            Guid.NewGuid(),
            new KitchenTicketReady(
                orderId,
                restaurantId,
                null,
                2,
                null,
                "Mesa 01",
                "",
                "DineIn",
                "Pos",
                "Immediate",
                "CARNES",
                4,
                createdAtUtc,
                createdAtUtc,
                createdAtUtc.AddSeconds(30),
                null,
                [
                    new OrderLineSnapshot(
                        readyLineId,
                        Guid.NewGuid(),
                        "Hamburguesa",
                        12m,
                        1,
                        "",
                        null,
                        "",
                        "CARNES",
                        "Carnes",
                        KitchenStatus: "Ready",
                        PreparationStartedAtUtc: createdAtUtc.AddSeconds(30),
                        ReadyAtUtc: readyAtUtc
                    ),
                    new OrderLineSnapshot(
                        pendingLineId,
                        Guid.NewGuid(),
                        "Entrecot",
                        18m,
                        1,
                        "",
                        null,
                        "",
                        "CARNES",
                        "Carnes"
                    ),
                ],
                [
                    new OrderKitchenStationSnapshot(
                        "CARNES",
                        "Carnes",
                        "InPreparation",
                        false,
                        1,
                        createdAtUtc.AddSeconds(30),
                        null,
                        null
                    ),
                ]
            )
        );

        OrderReportingFact order = await db.Orders.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal("InPreparation", order.OrderStatus);
        Assert.Equal(readyAtUtc, order.UpdatedAtUtc);
        Assert.Equal(
            readyLineId,
            JsonSerializer.Deserialize<OrderLineSnapshot[]>(order.LinesJson)![0].Id
        );
        Assert.Null(order.DeliveredAtUtc);
    }

    [Fact]
    public async Task Partial_payment_does_not_notify_dashboard_until_sale_is_completed()
    {
        DbContextOptions<ReportingReadDbContext> options =
            new DbContextOptionsBuilder<ReportingReadDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        await using ReportingReadDbContext db = new(options);
        ReportingProjectionHandler handler = new(db, TimeProvider.System);
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid accountId = Guid.NewGuid();
        DateTime now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        await ProjectAsync(
            handler,
            Guid.NewGuid(),
            new PaymentCaptured(
                Guid.NewGuid(),
                orderId,
                restaurantId,
                4m,
                "Cash",
                "DineIn",
                "Pos",
                "",
                now,
                AmountPaid: 4m,
                IsFullyPaid: false,
                SaleGroupId: accountId
            )
        );

        OrderReportingFact order = await db.Orders.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal("PartiallyPaid", order.PaymentStatus);
        Assert.Null(order.SaleRecognizedAtUtc);
        Assert.Empty(db.OutboxMessages);

        await ProjectAsync(
            handler,
            Guid.NewGuid(),
            new PaymentCaptured(
                Guid.NewGuid(),
                orderId,
                restaurantId,
                6m,
                "Card",
                "DineIn",
                "Pos",
                "",
                now.AddMinutes(1),
                AmountPaid: 10m,
                IsFullyPaid: true,
                SaleGroupId: accountId
            )
        );
        Assert.Equal("Paid", order.PaymentStatus);
        Assert.Empty(db.OutboxMessages);

        await ProjectAsync(
            handler,
            Guid.NewGuid(),
            new SaleCompleted(
                accountId,
                restaurantId,
                "Pos",
                "Mixed",
                10m,
                now.AddMinutes(2),
                [orderId],
                [],
                PaymentMethods:
                [
                    new SalePaymentBreakdown("Cash", 1, 4m),
                    new SalePaymentBreakdown("Card", 1, 6m),
                ]
            )
        );
        Assert.Single(db.OutboxMessages);
        Assert.Single(db.Sales);

        DashboardQueryService query = new(
            new ReportingReadStore(db),
            TimeProvider.System,
            TimeZoneInfo.Utc
        );
        DailyDashboard dashboard = await query.GetDailyAsync(
            DateOnly.FromDateTime(now),
            [restaurantId],
            10,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(1, dashboard.CompletedSales);
        Assert.Equal(10m, dashboard.TotalSales);
        Assert.Contains(
            dashboard.PaymentMethods,
            x => x.Method == "Cash" && x.Tickets == 1 && x.Sales == 4m
        );
        Assert.Contains(
            dashboard.PaymentMethods,
            x => x.Method == "Card" && x.Tickets == 1 && x.Sales == 6m
        );
        OperationalOrder operationalOrder = Assert.Single(dashboard.OperationalOrders);
        OperationalLine operationalLine = Assert.Single(operationalOrder.Lines);
        Assert.Equal("Dispatched", operationalLine.KitchenStatus);
    }

    [Fact]
    public async Task Sale_after_midnight_uses_cash_register_business_date_in_daily_totals()
    {
        DbContextOptions<ReportingReadDbContext> options =
            new DbContextOptionsBuilder<ReportingReadDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        await using ReportingReadDbContext db = new(options);
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        DateTime completedAtUtc = new(2026, 10, 4, 22, 10, 0, DateTimeKind.Utc);
        db.Orders.Add(new OrderReportingFact { OrderId = orderId, RestaurantId = restaurantId });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        ReportingProjectionHandler handler = new(db, TimeProvider.System);
        await ProjectAsync(
            handler,
            Guid.NewGuid(),
            new SaleCompleted(
                Guid.NewGuid(),
                restaurantId,
                "Comandero",
                "Card",
                2.20m,
                completedAtUtc,
                [orderId],
                [],
                new DateOnly(2026, 10, 4)
            )
        );

        ReportingReadStore store = new(db);
        DailyReportingSnapshot fourth = await store.GetDailySnapshotAsync(
            new DateOnly(2026, 10, 4),
            new DateTime(2026, 10, 3, 22, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 4, 22, 0, 0, DateTimeKind.Utc),
            [restaurantId],
            10,
            TestContext.Current.CancellationToken
        );
        DailyReportingSnapshot fifth = await store.GetDailySnapshotAsync(
            new DateOnly(2026, 10, 5),
            new DateTime(2026, 10, 4, 22, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 5, 22, 0, 0, DateTimeKind.Utc),
            [restaurantId],
            10,
            TestContext.Current.CancellationToken
        );

        Assert.Single(fourth.Sales);
        Assert.Equal(2.20m, fourth.Sales[0].Total);
        Assert.Empty(fifth.Sales);
    }

    [Fact]
    public async Task Daily_snapshot_limits_operational_orders_to_the_selected_day()
    {
        DbContextOptions<ReportingReadDbContext> options =
            new DbContextOptionsBuilder<ReportingReadDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        await using ReportingReadDbContext db = new(options);
        Guid restaurantId = Guid.NewGuid();
        Guid selectedDayOrderId = Guid.NewGuid();
        DateTime startUtc = new(2026, 10, 4, 22, 0, 0, DateTimeKind.Utc);
        DateTime endUtc = new(2026, 10, 5, 22, 0, 0, DateTimeKind.Utc);
        db.Orders.AddRange(
            new OrderReportingFact
            {
                OrderId = Guid.NewGuid(),
                RestaurantId = restaurantId,
                CreatedAtUtc = startUtc.AddDays(-1),
                UpdatedAtUtc = endUtc.AddMinutes(1),
            },
            new OrderReportingFact
            {
                OrderId = selectedDayOrderId,
                RestaurantId = restaurantId,
                CreatedAtUtc = startUtc.AddHours(1),
                UpdatedAtUtc = startUtc.AddHours(2),
            }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        ReportingReadStore store = new(db);
        DailyReportingSnapshot snapshot = await store.GetDailySnapshotAsync(
            new DateOnly(2026, 10, 5),
            startUtc,
            endUtc,
            [restaurantId],
            10,
            TestContext.Current.CancellationToken
        );

        OperationalOrder order = Assert.Single(snapshot.OperationalOrders);
        Assert.Equal(selectedDayOrderId, order.OrderId);
    }

    [Fact]
    public async Task Cancelled_projection_does_not_record_an_inbox_message()
    {
        DbContextOptions<ReportingReadDbContext> options =
            new DbContextOptionsBuilder<ReportingReadDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        await using ReportingReadDbContext db = new(options);
        ReportingProjectionHandler handler = new(db, TimeProvider.System);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.ProjectAsync(
                Guid.NewGuid(),
                null,
                ReadOnlyMemory<byte>.Empty,
                cancellation.Token
            )
        );

        Assert.Empty(await db.InboxMessages.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Projects_order_payment_refund_and_sale_once()
    {
        DbContextOptions<ReportingReadDbContext> options =
            new DbContextOptionsBuilder<ReportingReadDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        await using ReportingReadDbContext db = new(options);
        ReportingProjectionHandler handler = new(db, TimeProvider.System);
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid paymentId = Guid.NewGuid();
        Guid transactionId = Guid.NewGuid();
        Guid saleId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        OrderCreated order = new(
            orderId,
            restaurantId,
            null,
            null,
            null,
            "Mesa 1",
            "Cliente",
            "DineIn",
            "Pos",
            "AfterService",
            1,
            now,
            [
                new OrderLineSnapshot(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Hamburguesa",
                    10m,
                    1,
                    "",
                    null,
                    "Carnes",
                    "CARNES",
                    "Carnes",
                    KitchenStatus: "Dispatched"
                ),
            ],
            []
        );
        Guid orderMessageId = Guid.NewGuid();
        await ProjectAsync(handler, orderMessageId, order);
        await ProjectAsync(handler, orderMessageId, order);

        OrderReportingFact projectedOrder = await db.Orders.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(orderId, projectedOrder.OrderId);
        Assert.Equal("Mesa 1", projectedOrder.TableLabel);
        Assert.Equal("Draft", projectedOrder.OrderStatus);
        Assert.Single(await db.InboxMessages.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await db.OutboxMessages.ToListAsync(TestContext.Current.CancellationToken));

        PaymentCaptured payment = new(
            paymentId,
            orderId,
            restaurantId,
            12m,
            "Card",
            "DineIn",
            "Pos",
            "",
            now.AddMinutes(1),
            transactionId
        );
        await ProjectAsync(handler, Guid.NewGuid(), payment);
        Assert.Equal("Paid", projectedOrder.PaymentStatus);
        Assert.Equal(transactionId, projectedOrder.PaymentTransactionId);
        Assert.Equal(now.AddMinutes(1), projectedOrder.SaleRecognizedAtUtc);

        PaymentRefunded refund = new(
            paymentId,
            orderId,
            restaurantId,
            12m,
            "Card",
            "DineIn",
            "Pos",
            "Requested by customer",
            now.AddMinutes(2)
        );
        await ProjectAsync(handler, Guid.NewGuid(), refund);
        Assert.Equal("Refunded", projectedOrder.PaymentStatus);
        Assert.Null(projectedOrder.SaleRecognizedAtUtc);

        SaleCompleted sale = new(
            saleId,
            restaurantId,
            "Pos",
            "Card",
            12m,
            now.AddMinutes(3),
            [orderId],
            []
        );
        await ProjectAsync(handler, Guid.NewGuid(), sale);

        CompletedSaleFact projectedSale = await db.Sales.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(saleId, projectedSale.SaleId);
        Assert.Equal(JsonSerializer.Serialize(new[] { orderId }), projectedSale.OrderIdsJson);
        Assert.Equal("[]", projectedSale.LinesJson);
        Assert.Equal(4, await db.InboxMessages.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, await db.OutboxMessages.CountAsync(TestContext.Current.CancellationToken));
    }

    private static Task ProjectAsync<T>(
        ReportingProjectionHandler handler,
        Guid messageId,
        T @event
    )
        where T : notnull
    {
        return handler.ProjectAsync(
            messageId,
            typeof(T).FullName,
            JsonSerializer.SerializeToUtf8Bytes(@event),
            TestContext.Current.CancellationToken
        );
    }
}
