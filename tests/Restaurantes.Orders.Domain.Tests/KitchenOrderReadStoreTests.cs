using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Infrastructure.Persistence.Read;
using Restaurantes.Orders.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class KitchenOrderReadStoreTests
{
    [Fact]
    public async Task Find_maps_the_order_and_returns_null_when_it_does_not_exist()
    {
        await using OrderReadDbContext db = CreateDatabase();
        KitchenOrderReadModel order = Order(Guid.NewGuid(), Guid.NewGuid(), "Submitted");
        db.KitchenOrders.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        KitchenOrderReadStore store = new(db);

        OrderResponse? found = await store.FindAsync(
            order.Id,
            TestContext.Current.CancellationToken
        );
        OrderResponse? missing = await store.FindAsync(
            Guid.NewGuid(),
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(found);
        Assert.Equal(order.Id, found.Id);
        Assert.Equal(order.RestaurantId, found.RestaurantId);
        Assert.Equal("Submitted", found.Status);
        Assert.Single(found.Lines);
        Assert.Single(found.Stations);
        Assert.Null(missing);
    }

    [Fact]
    public async Task List_returns_active_kitchen_statuses_for_the_restaurant_in_submission_order()
    {
        await using OrderReadDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        KitchenOrderReadModel later = Order(restaurantId, Guid.NewGuid(), "Ready");
        later.SubmittedAtUtc = new DateTime(2026, 10, 20, 11, 0, 0, DateTimeKind.Utc);
        KitchenOrderReadModel earlier = Order(restaurantId, Guid.NewGuid(), "InPreparation");
        earlier.SubmittedAtUtc = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
        KitchenOrderReadModel draft = Order(restaurantId, Guid.NewGuid(), "Draft");
        KitchenOrderReadModel cancelled = Order(restaurantId, Guid.NewGuid(), "Cancelled");
        KitchenOrderReadModel otherRestaurant = Order(Guid.NewGuid(), Guid.NewGuid(), "Ready");
        db.KitchenOrders.AddRange(later, earlier, draft, cancelled, otherRestaurant);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        KitchenOrderReadStore store = new(db);

        IReadOnlyList<OrderResponse> result = await store.ListAsync(
            restaurantId,
            null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal([earlier.Id, later.Id], result.Select(order => order.Id));
    }

    [Fact]
    public async Task List_filters_lines_by_station_and_ignores_cancelled_or_dispatched_stations()
    {
        await using OrderReadDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        KitchenOrderReadModel order = Order(restaurantId, Guid.NewGuid(), "InPreparation");
        OrderLineResponse grillLine = Line("GRILL");
        OrderLineResponse cancelledGrillLine = Line("GRILL", status: "Cancelled");
        OrderLineResponse barLine = Line("BAR");
        order.LinesJson = JsonSerializer.Serialize(
            new[] { grillLine, cancelledGrillLine, barLine }
        );
        order.StationsJson = JsonSerializer.Serialize(
            new[] { Station("GRILL", "Ready"), Station("BAR", "Dispatched") }
        );
        db.KitchenOrders.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        KitchenOrderReadStore store = new(db);

        IReadOnlyList<OrderResponse> grill = await store.ListAsync(
            restaurantId,
            "  grill ",
            TestContext.Current.CancellationToken
        );
        IReadOnlyList<OrderResponse> dispatched = await store.ListAsync(
            restaurantId,
            "BAR",
            TestContext.Current.CancellationToken
        );
        IReadOnlyList<OrderResponse> unknown = await store.ListAsync(
            restaurantId,
            "UNKNOWN",
            TestContext.Current.CancellationToken
        );

        OrderResponse selected = Assert.Single(grill);
        Assert.Equal(grillLine.Id, Assert.Single(selected.Lines).Id);
        Assert.Equal("GRILL", Assert.Single(selected.Stations).Code);
        Assert.Empty(dispatched);
        Assert.Empty(unknown);
    }

    [Fact]
    public async Task List_filters_the_same_station_by_zone_without_affecting_global_view()
    {
        await using OrderReadDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        Guid firstZoneId = Guid.NewGuid();
        Guid secondZoneId = Guid.NewGuid();
        KitchenOrderReadModel first = Order(restaurantId, Guid.NewGuid(), "Submitted");
        first.ZoneId = firstZoneId;
        KitchenOrderReadModel second = Order(restaurantId, Guid.NewGuid(), "Submitted");
        second.ZoneId = secondZoneId;
        KitchenOrderReadModel withoutZone = Order(restaurantId, Guid.NewGuid(), "Submitted");
        db.KitchenOrders.AddRange(first, second, withoutZone);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        KitchenOrderReadStore store = new(db);

        IReadOnlyList<OrderResponse> firstZone = await store.ListAsync(
            restaurantId,
            "GRILL",
            TestContext.Current.CancellationToken,
            firstZoneId
        );
        IReadOnlyList<OrderResponse> noZone = await store.ListAsync(
            restaurantId,
            "GRILL",
            TestContext.Current.CancellationToken,
            Guid.Empty
        );
        IReadOnlyList<OrderResponse> global = await store.ListAsync(
            restaurantId,
            "GRILL",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(first.Id, Assert.Single(firstZone).Id);
        Assert.Equal(firstZoneId, Assert.Single(firstZone).ZoneId);
        Assert.Equal(withoutZone.Id, Assert.Single(noZone).Id);
        Assert.Equal(3, global.Count);
    }

    [Fact]
    public async Task Chef_queue_hides_fully_dispatched_ready_dine_in_orders_but_keeps_takeaway()
    {
        await using OrderReadDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        KitchenOrderReadModel readyDineIn = Order(restaurantId, Guid.NewGuid(), "Ready");
        readyDineIn.StationsJson = JsonSerializer.Serialize(
            new[] { Station("GRILL", "Dispatched") }
        );
        KitchenOrderReadModel readyTakeaway = Order(restaurantId, Guid.NewGuid(), "Ready");
        readyTakeaway.ServiceMode = "Takeaway";
        readyTakeaway.StationsJson = JsonSerializer.Serialize(
            new[] { Station("GRILL", "Dispatched") }
        );
        KitchenOrderReadModel pending = Order(restaurantId, Guid.NewGuid(), "Ready");
        pending.StationsJson = JsonSerializer.Serialize(new[] { Station("GRILL", "Ready") });
        db.KitchenOrders.AddRange(readyDineIn, readyTakeaway, pending);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        KitchenOrderReadStore store = new(db);

        IReadOnlyList<OrderResponse> result = await store.ListAsync(
            restaurantId,
            "CHEF",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            new[] { readyTakeaway.Id, pending.Id }.OrderBy(id => id),
            result.Select(order => order.Id).OrderBy(id => id)
        );
        Assert.DoesNotContain(result, order => order.Id == readyDineIn.Id);
    }

    private static OrderReadDbContext CreateDatabase() =>
        new(
            new DbContextOptionsBuilder<OrderReadDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static KitchenOrderReadModel Order(Guid restaurantId, Guid id, string status) =>
        new()
        {
            Id = id,
            RestaurantId = restaurantId,
            TableLabel = "Mesa 1",
            CustomerName = "Cliente",
            ServiceMode = "DineIn",
            Source = "Pos",
            PaymentTiming = "OnAccount",
            Status = status,
            CreatedAtUtc = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc),
            SubmittedAtUtc = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc),
            LinesJson = JsonSerializer.Serialize(new[] { Line("GRILL") }),
            StationsJson = JsonSerializer.Serialize(new[] { Station("GRILL", "InPreparation") }),
        };

    private static OrderLineResponse Line(string station, string status = "Active") =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Producto",
            10m,
            1,
            10m,
            "",
            null,
            "",
            station,
            station,
            status
        );

    private static OrderKitchenStationResponse Station(string code, string status) =>
        new(code, code, status, false, 1, null, null, null);
}
