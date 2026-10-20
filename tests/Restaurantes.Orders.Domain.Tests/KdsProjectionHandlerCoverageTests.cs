using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Infrastructure.Persistence.Read;
using Restaurantes.Orders.Infrastructure.Workers.Handlers;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class KdsProjectionHandlerCoverageTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] SubmittedStationCodes = ["CALIENTE", "BEBIDAS"];
    private static readonly string[] StartedStationCodes = ["CALIENTE"];

    [Fact]
    public async Task Created_event_persists_draft_once_without_notification()
    {
        await using OrderReadDbContext db = CreateDatabase();
        KdsProjectionHandler handler = new(db, TimeProvider.System);
        Guid messageId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        OrderCreated created = new(
            orderId,
            restaurantId,
            null,
            3,
            Guid.NewGuid(),
            "Mesa 4",
            "Ana",
            "DineIn",
            "Pos",
            "Immediate",
            1,
            CreatedAtUtc,
            [Line("Activa", 2), Line("Anulada", 8, status: "Cancelled")],
            [Station("COCINA", "Pending")]
        );

        KdsProjectionNotification? notification = await Project(handler, messageId, created);
        KitchenOrderReadModel projection = await db.KitchenOrders.SingleAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Null(notification);
        Assert.Equal(restaurantId, projection.RestaurantId);
        Assert.Equal("Draft", projection.Status);
        Assert.Equal(2, projection.Total);
        Assert.Equal(1, projection.Version);
        Assert.Equal(created.DiningSessionId, projection.DiningSessionId);
        Assert.Equal(
            2,
            JsonSerializer.Deserialize<OrderLineResponse[]>(projection.LinesJson)!.Length
        );
        Assert.Single(
            JsonSerializer.Deserialize<OrderKitchenStationResponse[]>(projection.StationsJson)!
        );
        Assert.Single(db.InboxMessages);

        Assert.Null(await Project(handler, messageId, created));
        Assert.Single(db.InboxMessages);
    }

    [Fact]
    public async Task Submitted_and_kitchen_events_map_status_and_affected_stations()
    {
        await using OrderReadDbContext db = CreateDatabase();
        KdsProjectionHandler handler = new(db, TimeProvider.System);
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        OrderSubmitted submitted = new(
            orderId,
            restaurantId,
            null,
            2,
            null,
            "Mesa 1",
            "",
            "DineIn",
            "Commander",
            "OnAccount",
            2,
            CreatedAtUtc,
            CreatedAtUtc.AddMinutes(1),
            [Line("Pasta", 10)],
            [Station("CALIENTE", "Pending"), Station("BEBIDAS", "Pending")]
        );

        KdsProjectionNotification submittedNotification = Assert.IsType<KdsProjectionNotification>(
            await Project(handler, Guid.NewGuid(), submitted)
        );
        Assert.Equal("Submitted", submittedNotification.Status);
        Assert.Equal(SubmittedStationCodes, submittedNotification.AffectedStationCodes);
        Assert.Equal(
            10,
            (await db.KitchenOrders.SingleAsync(TestContext.Current.CancellationToken)).Total
        );

        KitchenTicketPreparationStarted started = new(
            orderId,
            restaurantId,
            null,
            2,
            null,
            "Mesa 1",
            "",
            "DineIn",
            "Commander",
            "OnAccount",
            "CALIENTE",
            3,
            CreatedAtUtc,
            CreatedAtUtc.AddMinutes(1),
            CreatedAtUtc.AddMinutes(2),
            [Line("Pasta", 10)],
            [Station("CALIENTE", "InPreparation")]
        );
        KdsProjectionNotification startedNotification = Assert.IsType<KdsProjectionNotification>(
            await Project(handler, Guid.NewGuid(), started)
        );
        Assert.Equal("InPreparation", startedNotification.Status);
        Assert.Equal(StartedStationCodes, startedNotification.AffectedStationCodes);

        KitchenTicketReady readyWithoutTimestamp = TicketReady(orderId, restaurantId, 4, null);
        KdsProjectionNotification pendingReadyNotification =
            Assert.IsType<KdsProjectionNotification>(
                await Project(handler, Guid.NewGuid(), readyWithoutTimestamp)
            );
        Assert.Equal("InPreparation", pendingReadyNotification.Status);

        KitchenTicketReady ready = TicketReady(
            orderId,
            restaurantId,
            5,
            CreatedAtUtc.AddMinutes(4)
        );
        KdsProjectionNotification readyNotification = Assert.IsType<KdsProjectionNotification>(
            await Project(handler, Guid.NewGuid(), ready)
        );
        Assert.Equal("Ready", readyNotification.Status);
        Assert.Equal(
            CreatedAtUtc.AddMinutes(4),
            (await db.KitchenOrders.SingleAsync(TestContext.Current.CancellationToken)).ReadyAtUtc
        );
    }

    [Fact]
    public async Task Dispatch_and_order_lifecycle_events_project_terminal_states()
    {
        await using OrderReadDbContext db = CreateDatabase();
        KdsProjectionHandler handler = new(db, TimeProvider.System);
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();

        KitchenTicketDispatched partial = TicketDispatched(
            orderId,
            restaurantId,
            1,
            [Station("CALIENTE", "Dispatched"), Station("BEBIDAS", "Ready")]
        );
        KdsProjectionNotification partialNotification = Assert.IsType<KdsProjectionNotification>(
            await Project(handler, Guid.NewGuid(), partial)
        );
        Assert.Equal("Ready", partialNotification.Status);
        Assert.Null(
            (
                await db.KitchenOrders.SingleAsync(TestContext.Current.CancellationToken)
            ).DeliveredAtUtc
        );

        KitchenTicketDispatched complete = TicketDispatched(
            orderId,
            restaurantId,
            2,
            [Station("CALIENTE", "Dispatched"), Station("BEBIDAS", "Cancelled")]
        );
        KdsProjectionNotification completeNotification = Assert.IsType<KdsProjectionNotification>(
            await Project(handler, Guid.NewGuid(), complete)
        );
        Assert.Equal("Delivered", completeNotification.Status);
        Assert.Equal(
            CreatedAtUtc.AddMinutes(5),
            (
                await db.KitchenOrders.SingleAsync(TestContext.Current.CancellationToken)
            ).DeliveredAtUtc
        );

        OrderReady ready = new(
            orderId,
            restaurantId,
            null,
            2,
            null,
            "Mesa 2",
            "",
            "DineIn",
            "Pos",
            "Immediate",
            3,
            CreatedAtUtc,
            CreatedAtUtc.AddMinutes(1),
            CreatedAtUtc.AddMinutes(2),
            CreatedAtUtc.AddMinutes(3),
            [Line("Pasta", 10)],
            [Station("CALIENTE", "Ready")]
        );
        Assert.Equal("Ready", (await Project(handler, Guid.NewGuid(), ready))!.Status);

        OrderDelivered delivered = new(
            orderId,
            restaurantId,
            null,
            2,
            null,
            "Mesa 2",
            "",
            "DineIn",
            "Pos",
            "Immediate",
            4,
            CreatedAtUtc,
            CreatedAtUtc.AddMinutes(1),
            CreatedAtUtc.AddMinutes(2),
            CreatedAtUtc.AddMinutes(3),
            CreatedAtUtc.AddMinutes(4),
            [Line("Pasta", 10)],
            [Station("CALIENTE", "Dispatched")]
        );
        Assert.Equal("Delivered", (await Project(handler, Guid.NewGuid(), delivered))!.Status);

        OrderCancelled cancelled = new(
            orderId,
            restaurantId,
            null,
            2,
            null,
            "Mesa 2",
            "",
            "DineIn",
            "Pos",
            "Immediate",
            "Cliente",
            "encargado",
            5,
            CreatedAtUtc,
            CreatedAtUtc.AddMinutes(1),
            CreatedAtUtc.AddMinutes(5),
            [Line("Pasta", 10)],
            [
                Station(
                    "CALIENTE",
                    "Cancelled",
                    preparationStarted: CreatedAtUtc.AddMinutes(2),
                    ready: CreatedAtUtc.AddMinutes(3)
                ),
            ]
        );
        Assert.Equal("Cancelled", (await Project(handler, Guid.NewGuid(), cancelled))!.Status);
        KitchenOrderReadModel final = await db.KitchenOrders.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(CreatedAtUtc.AddMinutes(2), final.PreparationStartedAtUtc);
        Assert.Equal(CreatedAtUtc.AddMinutes(3), final.ReadyAtUtc);
        Assert.Null(final.DeliveredAtUtc);
    }

    [Theory]
    [InlineData("Submitted", "CALIENTE")]
    [InlineData("Cancelled", "CALIENTE,BEBIDAS")]
    [InlineData("Ready", "CALIENTE")]
    public async Task Line_cancellation_projects_status_and_changed_stations(
        string status,
        string expectedStations
    )
    {
        await using OrderReadDbContext db = CreateDatabase();
        KdsProjectionHandler handler = new(db, TimeProvider.System);
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        OrderLineCancelled cancelled = new(
            orderId,
            restaurantId,
            null,
            1,
            null,
            "Mesa 8",
            "",
            "DineIn",
            "Pos",
            "Immediate",
            Guid.NewGuid(),
            "CALIENTE",
            "Sin stock",
            status,
            5,
            1,
            CreatedAtUtc,
            CreatedAtUtc.AddMinutes(1),
            CreatedAtUtc.AddMinutes(2),
            [Line("Activa", 5), Line("Cancelada", 10, status: "Cancelled")],
            [
                Station(
                    "CALIENTE",
                    "InPreparation",
                    preparationStarted: CreatedAtUtc.AddSeconds(30)
                ),
                Station("BEBIDAS", "Pending"),
            ]
        );

        KdsProjectionNotification notification = Assert.IsType<KdsProjectionNotification>(
            await Project(handler, Guid.NewGuid(), cancelled)
        );

        Assert.Equal(status, notification.Status);
        Assert.Equal(expectedStations.Split(','), notification.AffectedStationCodes);
        KitchenOrderReadModel projection = await db.KitchenOrders.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(5, projection.Total);
        Assert.Equal(status == "Ready" ? CreatedAtUtc.AddMinutes(2) : null, projection.ReadyAtUtc);
        Assert.Equal(CreatedAtUtc.AddSeconds(30), projection.PreparationStartedAtUtc);
    }

    [Fact]
    public async Task Duplicate_non_draft_notifies_but_stale_new_message_does_not_overwrite_projection()
    {
        await using OrderReadDbContext db = CreateDatabase();
        KdsProjectionHandler handler = new(db, TimeProvider.System);
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        OrderSubmitted latest = Submitted(
            orderId,
            restaurantId,
            version: 2,
            tableLabel: "Mesa nueva"
        );
        Guid messageId = Guid.NewGuid();

        KdsProjectionNotification first = Assert.IsType<KdsProjectionNotification>(
            await Project(handler, messageId, latest)
        );
        KdsProjectionNotification duplicate = Assert.IsType<KdsProjectionNotification>(
            await Project(handler, messageId, latest)
        );
        Assert.Equal(first.OrderId, duplicate.OrderId);
        Assert.Equal(first.Status, duplicate.Status);
        Assert.Equal(first.Version, duplicate.Version);
        Assert.Equal(first.AffectedStationCodes, duplicate.AffectedStationCodes);

        OrderSubmitted stale = Submitted(
            orderId,
            restaurantId,
            version: 1,
            tableLabel: "Mesa vieja"
        );
        Assert.Null(await Project(handler, Guid.NewGuid(), stale));
        KitchenOrderReadModel projection = await db.KitchenOrders.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(2, projection.Version);
        Assert.Equal("Mesa nueva", projection.TableLabel);
        Assert.Equal(2, await db.InboxMessages.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unknown_event_and_empty_known_event_body_are_rejected()
    {
        await using OrderReadDbContext db = CreateDatabase();
        KdsProjectionHandler handler = new(db, TimeProvider.System);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            handler.ProjectAsync(
                Guid.NewGuid(),
                "unknown",
                "{}"u8.ToArray(),
                TestContext.Current.CancellationToken
            )
        );
        await Assert.ThrowsAsync<JsonException>(() =>
            handler.ProjectAsync(
                Guid.NewGuid(),
                typeof(OrderCreated).FullName,
                "null"u8.ToArray(),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Empty(db.InboxMessages);
    }

    private static OrderSubmitted Submitted(
        Guid orderId,
        Guid restaurantId,
        int version,
        string tableLabel
    ) =>
        new(
            orderId,
            restaurantId,
            null,
            2,
            null,
            tableLabel,
            "",
            "DineIn",
            "Pos",
            "Immediate",
            version,
            CreatedAtUtc,
            CreatedAtUtc.AddMinutes(1),
            [Line("Pasta", 10)],
            [Station("CALIENTE", "Pending")]
        );

    private static KitchenTicketReady TicketReady(
        Guid orderId,
        Guid restaurantId,
        int version,
        DateTime? readyAt
    ) =>
        new(
            orderId,
            restaurantId,
            null,
            2,
            null,
            "Mesa 1",
            "",
            "DineIn",
            "Pos",
            "Immediate",
            "CALIENTE",
            version,
            CreatedAtUtc,
            CreatedAtUtc.AddMinutes(1),
            CreatedAtUtc.AddMinutes(2),
            readyAt,
            [Line("Pasta", 10)],
            [Station("CALIENTE", "Ready", ready: readyAt)]
        );

    private static KitchenTicketDispatched TicketDispatched(
        Guid orderId,
        Guid restaurantId,
        int version,
        IReadOnlyList<OrderKitchenStationSnapshot> stations
    ) =>
        new(
            orderId,
            restaurantId,
            null,
            2,
            null,
            "Mesa 1",
            "",
            "DineIn",
            "Pos",
            "Immediate",
            stations[0].Code,
            version,
            CreatedAtUtc,
            CreatedAtUtc.AddMinutes(1),
            CreatedAtUtc.AddMinutes(2),
            CreatedAtUtc.AddMinutes(5),
            [Line("Pasta", 10)],
            stations
        );

    private static OrderLineSnapshot Line(string name, decimal price, string status = "Active") =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            price,
            1,
            "",
            null,
            "",
            "CALIENTE",
            "Caliente",
            Status: status
        );

    private static OrderKitchenStationSnapshot Station(
        string code,
        string status,
        DateTime? preparationStarted = null,
        DateTime? ready = null
    ) => new(code, code, status, false, 1, preparationStarted, ready, null);

    private static Task<KdsProjectionNotification?> Project(
        KdsProjectionHandler handler,
        Guid messageId,
        object value
    ) =>
        handler.ProjectAsync(
            messageId,
            value.GetType().FullName,
            JsonSerializer.SerializeToUtf8Bytes(value),
            TestContext.Current.CancellationToken
        );

    private static OrderReadDbContext CreateDatabase() =>
        new(
            new DbContextOptionsBuilder<OrderReadDbContext>()
                .UseInMemoryDatabase("kds-projection-" + Guid.NewGuid().ToString("N"))
                .Options
        );
}
