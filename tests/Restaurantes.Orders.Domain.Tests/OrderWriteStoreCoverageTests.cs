using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Domain;
using Restaurantes.Orders.Infrastructure.Persistence;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.Orders.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrderWriteStoreCoverageTests
{
    [Fact]
    public async Task Save_with_event_persists_each_lifecycle_event_at_its_business_timestamp()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using OrderWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        DateTime createdAt = DateTime.UtcNow.AddMinutes(-5);
        DateTime submittedAt = createdAt.AddMinutes(1);
        DateTime preparedAt = submittedAt.AddMinutes(1);
        DateTime readyAt = preparedAt.AddMinutes(1);
        DateTime dispatchedAt = readyAt.AddMinutes(1);
        DateTime deliveredAt = dispatchedAt.AddMinutes(1);
        DateTime cancelledAt = deliveredAt.AddMinutes(1);
        Order order = CreateOrder(restaurantId, createdAt);
        Guid orderId = order.Id;
        OrderWriteStore store = new(db);
        (object Event, DateTime OccurredAtUtc)[] events =
        [
            (
                new OrderCreated(
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
                    1,
                    createdAt,
                    [],
                    []
                ),
                createdAt
            ),
            (
                new OrderSubmitted(
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
                    2,
                    createdAt,
                    submittedAt,
                    [],
                    []
                ),
                submittedAt
            ),
            (
                new OrderPreparationStarted(
                    orderId,
                    restaurantId,
                    null,
                    2,
                    "Mesa 2",
                    "",
                    "DineIn",
                    "Pos",
                    "Immediate",
                    3,
                    createdAt,
                    submittedAt,
                    preparedAt,
                    [],
                    []
                ),
                preparedAt
            ),
            (
                new OrderReady(
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
                    createdAt,
                    submittedAt,
                    preparedAt,
                    readyAt,
                    [],
                    []
                ),
                readyAt
            ),
            (
                new KitchenTicketPreparationStarted(
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
                    "PASTAS",
                    5,
                    createdAt,
                    submittedAt,
                    preparedAt,
                    [LineSnapshot(preparedAt)],
                    []
                ),
                preparedAt
            ),
            (
                new KitchenTicketReady(
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
                    "PASTAS",
                    6,
                    createdAt,
                    submittedAt,
                    preparedAt,
                    readyAt,
                    [],
                    []
                ),
                readyAt
            ),
            (
                new KitchenTicketDispatched(
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
                    "PASTAS",
                    7,
                    createdAt,
                    submittedAt,
                    preparedAt,
                    dispatchedAt,
                    [],
                    []
                ),
                dispatchedAt
            ),
            (
                new OrderDelivered(
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
                    8,
                    createdAt,
                    submittedAt,
                    preparedAt,
                    readyAt,
                    deliveredAt,
                    [],
                    []
                ),
                deliveredAt
            ),
            (
                new OrderLineCancelled(
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
                    Guid.NewGuid(),
                    "PASTAS",
                    "Sin producto",
                    "Cancelled",
                    0m,
                    9,
                    createdAt,
                    submittedAt,
                    cancelledAt,
                    [],
                    []
                ),
                cancelledAt
            ),
            (
                new OrderCancelled(
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
                    "Cancelación solicitada",
                    "Sergio",
                    10,
                    createdAt,
                    submittedAt,
                    cancelledAt,
                    [],
                    []
                ),
                cancelledAt
            ),
        ];

        foreach ((object integrationEvent, _) in events)
        {
            await store.SaveWithEventAsync(order, integrationEvent, ct);
        }

        Assert.Equal(events.Length, db.OutboxMessages.Count());
        Dictionary<string, DateTime> expectedTimes = events.ToDictionary(
            item => item.Event.GetType().FullName!,
            item => item.OccurredAtUtc
        );
        foreach (OutboxMessage message in db.OutboxMessages)
        {
            Assert.Equal(expectedTimes[message.Type], message.OccurredAtUtc);
            Assert.False(string.IsNullOrWhiteSpace(message.Payload));
        }

        db.ChangeTracker.Clear();
        Order? loaded = await store.FindAsync(order.Id, ct);
        Assert.NotNull(loaded);
        Assert.Single(loaded.Lines);
        Assert.Null(await store.FindAsync(Guid.NewGuid(), ct));
    }

    [Fact]
    public async Task Kitchen_ticket_ready_uses_line_timestamp_then_station_timestamp_as_fallback()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using OrderWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        Order order = CreateOrder(restaurantId, DateTime.UtcNow);
        OrderWriteStore store = new(db);
        DateTime createdAt = DateTime.UtcNow.AddMinutes(-3);
        DateTime submittedAt = createdAt.AddMinutes(1);
        DateTime preparedAt = submittedAt.AddMinutes(1);
        DateTime lineReadyAt = preparedAt.AddSeconds(20);
        DateTime stationReadyAt = preparedAt.AddSeconds(30);
        KitchenTicketReady[] events =
        [
            new(
                order.Id,
                restaurantId,
                null,
                2,
                null,
                "Mesa 2",
                "",
                "DineIn",
                "Pos",
                "Immediate",
                "PASTAS",
                11,
                createdAt,
                submittedAt,
                preparedAt,
                null,
                [LineSnapshot(null, lineReadyAt)],
                []
            ),
            new(
                order.Id,
                restaurantId,
                null,
                2,
                null,
                "Mesa 2",
                "",
                "DineIn",
                "Pos",
                "Immediate",
                "PASTAS",
                12,
                createdAt,
                submittedAt,
                preparedAt,
                null,
                [LineSnapshot(null)],
                [new("PASTAS", "Pastas", "Ready", false, 1, preparedAt, stationReadyAt, null)]
            ),
        ];

        foreach (KitchenTicketReady integrationEvent in events)
        {
            await store.SaveWithEventAsync(order, integrationEvent, ct);
        }

        Assert.Equal(
            [lineReadyAt, stationReadyAt],
            db.OutboxMessages.Select(item => item.OccurredAtUtc).Order().ToArray()
        );
    }

    [Fact]
    public async Task Save_with_event_rejects_unknown_integration_event()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using OrderWriteDbContext db = CreateDatabase();
        OrderWriteStore store = new(db);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            store.SaveWithEventAsync(CreateOrder(Guid.NewGuid(), DateTime.UtcNow), new object(), ct)
        );

        Assert.Empty(db.OutboxMessages);
    }

    private static Order CreateOrder(Guid restaurantId, DateTime occurredAtUtc) =>
        Order.Create(
            restaurantId,
            Guid.NewGuid(),
            2,
            Guid.NewGuid(),
            "Mesa 2",
            "",
            ServiceMode.DineIn,
            OrderSource.Pos,
            PaymentTiming.OnAccount,
            [
                new OrderLineDraft(
                    Guid.NewGuid(),
                    "Pasta",
                    12m,
                    1,
                    "",
                    Guid.NewGuid(),
                    "Pastas",
                    "PASTAS",
                    "Pastas",
                    false,
                    1
                ),
            ],
            occurredAtUtc
        );

    private static OrderLineSnapshot LineSnapshot(DateTime? preparedAt, DateTime? readyAt = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Pasta",
            12m,
            1,
            "",
            null,
            "",
            "PASTAS",
            "Pastas",
            PreparationStartedAtUtc: preparedAt,
            ReadyAtUtc: readyAt
        );

    private static OrderWriteDbContext CreateDatabase() =>
        new(
            new DbContextOptionsBuilder<OrderWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
}
