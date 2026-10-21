using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.Orders.Infrastructure.Queries;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class KitchenHistoryQueryTests
{
    private static readonly DateOnly BusinessDate = new(2026, 10, 21);
    private static readonly DateTime UtcMorning = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task List_returns_null_when_station_is_missing_or_inactive()
    {
        await using OrderWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        KitchenHistoryQuery query = new(db, TimeZoneInfo.Utc);
        db.KitchenStationConfigurations.Add(Configuration(restaurantId, "GRILL", active: false));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<KitchenHistoryResponse>? missing = await query.ListAsync(
            restaurantId,
            BusinessDate,
            "BAR",
            TestContext.Current.CancellationToken
        );
        IReadOnlyList<KitchenHistoryResponse>? inactive = await query.ListAsync(
            restaurantId,
            BusinessDate,
            "GRILL",
            TestContext.Current.CancellationToken
        );

        Assert.Null(missing);
        Assert.Null(inactive);
    }

    [Fact]
    public async Task List_returns_dispatched_orders_for_station_and_business_date_newest_first()
    {
        await using OrderWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        db.KitchenStationConfigurations.Add(Configuration(restaurantId, "GRILL"));
        Order newest = CreateDispatchedOrder(restaurantId, "GRILL", UtcMorning.AddHours(1));
        Order older = CreateDispatchedOrder(restaurantId, "GRILL", UtcMorning);
        Order explicitlyDated = CreateDispatchedOrder(
            restaurantId,
            "GRILL",
            UtcMorning.AddMinutes(30),
            BusinessDate
        );
        Order otherRestaurant = CreateDispatchedOrder(Guid.NewGuid(), "GRILL", UtcMorning);
        Order otherStation = CreateDispatchedOrder(restaurantId, "BAR", UtcMorning);
        Order otherBusinessDate = CreateDispatchedOrder(
            restaurantId,
            "GRILL",
            UtcMorning,
            BusinessDate.AddDays(-1)
        );
        Order undispatched = CreateOrder(restaurantId, "GRILL", UtcMorning);
        db.Orders.AddRange(
            newest,
            explicitlyDated,
            older,
            otherRestaurant,
            otherStation,
            otherBusinessDate,
            undispatched
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        KitchenHistoryQuery query = new(db, TimeZoneInfo.Utc);

        IReadOnlyList<KitchenHistoryResponse>? result = await query.ListAsync(
            restaurantId,
            BusinessDate,
            "GRILL",
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(result);
        Assert.Equal(
            new[] { newest.Id, explicitlyDated.Id, older.Id },
            result.Select(item => item.OrderId)
        );
        KitchenHistoryResponse response = Assert.Single(result, item => item.OrderId == newest.Id);
        Assert.Equal("Mesa 1", response.TableLabel);
        Assert.Equal("Ready", response.Status);
        Assert.Equal(UtcMorning.AddHours(1), response.CreatedAtUtc);
        Assert.NotNull(response.CashRegisterSessionId);
        Assert.Equal(UtcMorning.AddHours(1).AddMinutes(2), response.ProcessedAtUtc);
        KitchenHistoryLineResponse line = Assert.Single(response.Lines);
        Assert.Equal("Producto", line.ProductName);
        Assert.Equal("Active", line.Status);
        Assert.Equal("Ración", line.Format);
    }

    [Fact]
    public async Task Chef_history_includes_only_stations_requiring_primary_dispatch()
    {
        await using OrderWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        db.KitchenStationConfigurations.Add(
            Configuration(restaurantId, "MAIN-KITCHEN", isPrimary: true)
        );
        Order primary = CreateDispatchedOrder(
            restaurantId,
            "GRILL",
            UtcMorning,
            requiresPrimaryDispatch: true
        );
        Order secondary = CreateDispatchedOrder(restaurantId, "BAR", UtcMorning.AddMinutes(1));
        db.Orders.AddRange(primary, secondary);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        KitchenHistoryQuery query = new(db, TimeZoneInfo.Utc);

        IReadOnlyList<KitchenHistoryResponse>? result = await query.ListAsync(
            restaurantId,
            BusinessDate,
            "CHEF",
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(result);
        KitchenHistoryResponse response = Assert.Single(result);
        Assert.Equal(primary.Id, response.OrderId);
        KitchenHistoryLineResponse line = Assert.Single(response.Lines);
        Assert.Equal(1, line.Quantity);
        Assert.Equal("Ración", line.Format);
    }

    [Fact]
    public async Task Station_history_includes_only_lines_prepared_by_that_station()
    {
        await using OrderWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        db.KitchenStationConfigurations.Add(Configuration(restaurantId, "GRILL"));
        Order order = Order.Create(
            restaurantId,
            Guid.NewGuid(),
            2,
            Guid.NewGuid(),
            "Mesa 1",
            "",
            ServiceMode.DineIn,
            OrderSource.Pos,
            PaymentTiming.OnAccount,
            [
                new OrderLineDraft(
                    Guid.NewGuid(),
                    "Carne",
                    18m,
                    1,
                    "",
                    null,
                    "Carnes",
                    "GRILL",
                    "Parrilla",
                    false,
                    1
                ),
                new OrderLineDraft(
                    Guid.NewGuid(),
                    "Agua",
                    3m,
                    2,
                    "",
                    null,
                    "Bebidas",
                    "BAR",
                    "Barra",
                    false,
                    1
                ),
            ],
            UtcMorning,
            businessDate: BusinessDate
        );
        order.Submit(UtcMorning.AddMinutes(-1));
        foreach (OrderLine orderLine in order.Lines)
        {
            order.StartLinePreparation(orderLine.Id, UtcMorning.AddMinutes(1));
            order.MarkLineReady(orderLine.Id, UtcMorning.AddMinutes(2));
        }
        db.Orders.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        KitchenHistoryQuery query = new(db, TimeZoneInfo.Utc);

        IReadOnlyList<KitchenHistoryResponse>? result = await query.ListAsync(
            restaurantId,
            BusinessDate,
            "GRILL",
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(result);
        KitchenHistoryResponse response = Assert.Single(result);
        Assert.Equal(order.Id, response.OrderId);
        KitchenHistoryLineResponse line = Assert.Single(response.Lines);
        Assert.Equal("Carne", line.ProductName);
        Assert.Equal(1, line.Quantity);
    }

    [Fact]
    public async Task List_uses_local_business_day_and_filters_zones_including_orders_without_zone()
    {
        await using OrderWriteDbContext db = CreateDatabase();
        Guid restaurantId = Guid.NewGuid();
        Guid terraceId = Guid.NewGuid();
        db.KitchenStationConfigurations.Add(Configuration(restaurantId, "GRILL"));
        TimeZoneInfo timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "KitchenHistoryTestZone",
            TimeSpan.FromHours(2),
            "Kitchen history test zone",
            "Kitchen history test zone"
        );
        DateTime utcStart = TimeZoneInfo.ConvertTimeToUtc(
            BusinessDate.ToDateTime(TimeOnly.MinValue),
            timeZone
        );
        Order terrace = CreateDispatchedOrder(
            restaurantId,
            "GRILL",
            utcStart.AddHours(1),
            businessDate: null,
            zoneId: terraceId
        );
        Order withoutZone = CreateDispatchedOrder(
            restaurantId,
            "GRILL",
            utcStart.AddHours(2),
            businessDate: null
        );
        Order nextDay = CreateDispatchedOrder(
            restaurantId,
            "GRILL",
            utcStart.AddDays(1),
            businessDate: null,
            zoneId: terraceId
        );
        db.Orders.AddRange(terrace, withoutZone, nextDay);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        KitchenHistoryQuery query = new(db, timeZone);

        IReadOnlyList<KitchenHistoryResponse>? terraceOrders = await query.ListAsync(
            restaurantId,
            BusinessDate,
            "GRILL",
            TestContext.Current.CancellationToken,
            terraceId
        );
        IReadOnlyList<KitchenHistoryResponse>? unzonedOrders = await query.ListAsync(
            restaurantId,
            BusinessDate,
            "GRILL",
            TestContext.Current.CancellationToken,
            Guid.Empty
        );

        Assert.NotNull(terraceOrders);
        Assert.Equal(terrace.Id, Assert.Single(terraceOrders).OrderId);
        Assert.NotNull(unzonedOrders);
        Assert.Equal(withoutZone.Id, Assert.Single(unzonedOrders).OrderId);
        IReadOnlyList<KitchenHistoryResponse>? allZones = await query.ListAsync(
            restaurantId,
            BusinessDate,
            "GRILL",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(allZones);
        Assert.Equal(new[] { withoutZone.Id, terrace.Id }, allZones.Select(order => order.OrderId));
    }

    private static OrderWriteDbContext CreateDatabase()
    {
        DbContextOptions<OrderWriteDbContext> options =
            new DbContextOptionsBuilder<OrderWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        return new OrderWriteDbContext(options);
    }

    private static OrderKitchenStationConfiguration Configuration(
        Guid restaurantId,
        string code,
        bool active = true,
        bool isPrimary = false
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Code = code,
            Name = code,
            IsPrimary = isPrimary,
            IsActive = active,
        };

    private static Order CreateOrder(
        Guid restaurantId,
        string stationCode,
        DateTime createdAtUtc,
        DateOnly? businessDate = null,
        Guid? zoneId = null,
        bool requiresPrimaryDispatch = false
    ) =>
        Order.Create(
            restaurantId,
            Guid.NewGuid(),
            2,
            Guid.NewGuid(),
            "Mesa 1",
            "",
            ServiceMode.DineIn,
            OrderSource.Pos,
            PaymentTiming.OnAccount,
            [
                new OrderLineDraft(
                    Guid.NewGuid(),
                    "Producto",
                    10m,
                    1,
                    "",
                    null,
                    "Comida",
                    stationCode,
                    stationCode,
                    requiresPrimaryDispatch,
                    1,
                    "Ración"
                ),
            ],
            createdAtUtc,
            Guid.NewGuid(),
            businessDate,
            zoneId
        );

    private static Order CreateDispatchedOrder(
        Guid restaurantId,
        string stationCode,
        DateTime createdAtUtc,
        DateOnly? businessDate = null,
        Guid? zoneId = null,
        bool requiresPrimaryDispatch = false
    )
    {
        Order order = CreateOrder(
            restaurantId,
            stationCode,
            createdAtUtc,
            businessDate,
            zoneId,
            requiresPrimaryDispatch
        );
        Guid lineId = Assert.Single(order.Lines).Id;
        order.Submit(createdAtUtc.AddMinutes(-1));
        order.StartLinePreparation(lineId, createdAtUtc.AddMinutes(1));
        order.MarkLineReady(lineId, createdAtUtc.AddMinutes(2));
        if (requiresPrimaryDispatch)
        {
            order.DispatchLine(lineId, createdAtUtc.AddMinutes(3));
        }
        return order;
    }
}
