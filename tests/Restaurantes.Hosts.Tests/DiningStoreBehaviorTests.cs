using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Restaurantes.Dining.Application;
using Restaurantes.Dining.Domain;
using Restaurantes.Dining.Infrastructure.Persistence;
using Restaurantes.Dining.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class DiningStoreBehaviorTests
{
    [Fact]
    public void Dining_model_preserves_relational_keys_indexes_and_constraints()
    {
        DbContextOptions<DiningDbContext> options = new DbContextOptionsBuilder<DiningDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using DiningDbContext db = new(options);

        Microsoft.EntityFrameworkCore.Metadata.IEntityType zone = db.Model.FindEntityType(
            typeof(DiningZone)
        )!;
        Microsoft.EntityFrameworkCore.Metadata.IEntityType table = db.Model.FindEntityType(
            typeof(RestaurantTable)
        )!;
        Microsoft.EntityFrameworkCore.Metadata.IEntityType session = db.Model.FindEntityType(
            typeof(DiningSession)
        )!;
        Microsoft.EntityFrameworkCore.Metadata.IEntityType order = db.Model.FindEntityType(
            typeof(DiningSessionOrder)
        )!;

        Assert.Equal("dining_zones", zone.GetTableName());
        Assert.Equal("restaurant_tables", table.GetTableName());
        Assert.Equal("dining_sessions", session.GetTableName());
        Assert.Equal("dining_session_orders", order.GetTableName());
        Assert.Equal(80, zone.FindProperty(nameof(DiningZone.Name))!.GetMaxLength());
        Assert.Equal(40, table.FindProperty(nameof(RestaurantTable.Code))!.GetMaxLength());
        Assert.Equal(80, table.FindProperty(nameof(RestaurantTable.Label))!.GetMaxLength());
        Assert.Equal(64, table.FindProperty(nameof(RestaurantTable.QrCode))!.GetMaxLength());
        Assert.Equal(
            5,
            session.FindProperty(nameof(DiningSession.DiscountPercentage))!.GetPrecision()
        );
        Assert.Contains(
            zone.GetIndexes(),
            index => index.IsUnique && index.GetFilter() is not null
        );
        Assert.Contains(
            session.GetIndexes(),
            index => index.IsUnique && index.GetFilter() == "\"Status\" = 'Open'"
        );
        Assert.Equal(DeleteBehavior.Restrict, table.GetForeignKeys().Single().DeleteBehavior);
        Assert.Equal(DeleteBehavior.Cascade, order.GetForeignKeys().Single().DeleteBehavior);
    }

    [Fact]
    public async Task Table_and_zone_reads_filter_deleted_rows_and_keep_zone_ordering()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using DiningDbContext db = CreateContext();
        Guid restaurantId = Guid.NewGuid();
        DiningZone terrace = Zone(restaurantId, "Terraza", 2);
        DiningZone main = Zone(restaurantId, "Salón", 1);
        DiningZone deletedZone = Zone(restaurantId, "Eliminada", 0);
        deletedZone.DeletedAtUtc = DateTime.UtcNow;
        RestaurantTable second = Table(restaurantId, terrace, "T02", "QR-T02");
        RestaurantTable first = Table(restaurantId, main, "T01", "QR-T01");
        RestaurantTable inactive = Table(restaurantId, main, "T03", "QR-T03");
        inactive.IsActive = false;
        RestaurantTable deleted = Table(restaurantId, main, "T04", "QR-T04");
        deleted.DeletedAtUtc = DateTime.UtcNow;
        RestaurantTable anotherRestaurant = Table(
            Guid.NewGuid(),
            Zone(Guid.NewGuid(), "Otra", 0),
            "T00",
            "QR-OTHER"
        );
        db.AddRange(
            terrace,
            main,
            deletedZone,
            second,
            first,
            inactive,
            deleted,
            anotherRestaurant
        );
        await db.SaveChangesAsync(cancellationToken);
        DiningStore store = new(db);

        RestaurantTable? qrTable = await store.ReadQrTableAsync("QR-T02", cancellationToken);
        Assert.NotNull(qrTable);
        Assert.Equal("Terraza", qrTable.Zone.Name);
        Assert.Null(await store.ReadQrTableAsync("QR-T03", cancellationToken));
        Assert.Null(await store.ReadQrTableAsync("QR-T04", cancellationToken));

        List<RestaurantTable> tables = await store.ListTablesAsync(restaurantId, cancellationToken);
        Assert.Equal(["T01", "T02", "T03"], tables.Select(table => table.Code));
        Assert.Equal("Salón", tables[0].Zone.Name);
        Assert.Equal(
            terrace.Id,
            await store.ReadTableZoneIdAsync(restaurantId, second.Id, cancellationToken)
        );
        Assert.Null(
            await store.ReadTableZoneIdAsync(restaurantId, anotherRestaurant.Id, cancellationToken)
        );

        List<DiningZone> zones = await store.ListZonesAsync(restaurantId, cancellationToken);
        Assert.Equal(["Salón", "Terraza"], zones.Select(zone => zone.Name));
        Assert.Equal(main, await store.FindZoneAsync(restaurantId, main.Id, cancellationToken));
        Assert.Null(await store.FindZoneAsync(restaurantId, deletedZone.Id, cancellationToken));
        Assert.Null(await store.FindZoneAsync(Guid.NewGuid(), main.Id, cancellationToken));
    }

    [Fact]
    public async Task Session_reads_respect_table_status_and_tenant_and_return_orders()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using DiningDbContext db = CreateContext();
        Guid restaurantId = Guid.NewGuid();
        DiningZone zone = Zone(restaurantId, "Sala", 0);
        RestaurantTable table = Table(restaurantId, zone, "M01", "QR-M01");
        DiningSession open = Session(restaurantId, table.Id);
        DiningSessionOrder openOrder = Order(open.Id, 12m);
        open.Orders.Add(openOrder);
        DiningSession paid = Session(restaurantId, table.Id);
        paid.Status = "Closed";
        paid.PaidAtUtc = DateTime.UtcNow;
        DiningSessionOrder paidOrder = Order(paid.Id, 15m);
        paidOrder.AmountPaid = 5m;
        paid.Orders.Add(paidOrder);
        DiningSession foreign = Session(Guid.NewGuid(), Guid.NewGuid());
        db.AddRange(zone, table, open, paid, foreign);
        await db.SaveChangesAsync(cancellationToken);
        DiningStore store = new(db);

        Assert.True(await store.HasOpenSessionAsync(table.Id, cancellationToken));
        Assert.True(await store.HasSalesForTableAsync(table.Id, cancellationToken));
        Assert.Equal(
            open.Id,
            (await store.ReadActiveSessionWithOrdersAsync(table.Id, cancellationToken))!.Id
        );
        Assert.Equal(
            openOrder.OrderId,
            (await store.ReadActiveSessionWithOrdersAsync(table.Id, cancellationToken))!
                .Orders.Single()
                .OrderId
        );
        Assert.Equal(
            open.Id,
            (await store.FindActiveSessionWithOrdersAsync(table.Id, cancellationToken))!.Id
        );
        Assert.Equal(
            open.Id,
            (await store.ReadRequiredActiveSessionAsync(table.Id, cancellationToken)).Id
        );
        Assert.Equal(
            open.Id,
            (await store.ReadSessionWithOrdersAsync(open.Id, cancellationToken))!.Id
        );
        Assert.Equal(
            open.Id,
            (await store.FindSessionWithOrdersAsync(open.Id, cancellationToken))!.Id
        );
        Assert.Null(
            await store.ReadValidatedSessionAsync(
                open.Id,
                Guid.NewGuid(),
                table.Id,
                cancellationToken
            )
        );
        Assert.Equal(
            open.Id,
            (
                await store.ReadValidatedSessionAsync(
                    open.Id,
                    restaurantId,
                    table.Id,
                    cancellationToken
                )
            )!.Id
        );

        Dictionary<Guid, DiningSession> active = await store.ReadActiveSessionsAsync(
            restaurantId,
            cancellationToken
        );
        Assert.Equal(open.Id, active[table.Id].Id);
        Assert.Single(active);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ReadRequiredActiveSessionAsync(Guid.NewGuid(), cancellationToken)
        );
        Assert.False(await store.HasOpenSessionAsync(Guid.NewGuid(), cancellationToken));
        Assert.False(await store.HasSalesForTableAsync(Guid.NewGuid(), cancellationToken));
    }

    [Fact]
    public async Task Store_persists_inbox_and_pending_payment_changes_and_loads_orders()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using DiningDbContext db = CreateContext();
        Guid restaurantId = Guid.NewGuid();
        DiningZone zone = Zone(restaurantId, "Sala", 0);
        RestaurantTable table = Table(restaurantId, zone, "M01", "QR-M01");
        DiningSession session = Session(restaurantId, table.Id);
        DiningSessionOrder order = Order(session.Id, 8m);
        DiningPendingPayment pending = new()
        {
            OrderId = order.OrderId,
            AmountPaid = 3m,
            TipPaid = 1m,
            Status = "Partial",
            UpdatedAtUtc = DateTime.UtcNow,
        };
        db.AddRange(zone, table, session, order, pending);
        await db.SaveChangesAsync(cancellationToken);
        DiningStore store = new(db);

        Assert.Equal(order, await store.FindOrderAsync(order.OrderId, cancellationToken));
        DiningSession loaded = await db.Sessions.SingleAsync(
            item => item.Id == session.Id,
            cancellationToken
        );
        await store.LoadOrdersAsync(loaded, cancellationToken);
        Assert.Equal(order.OrderId, Assert.Single(loaded.Orders).OrderId);
        Assert.False(await store.HasProcessedMessageAsync(Guid.NewGuid(), cancellationToken));

        Guid messageId = Guid.NewGuid();
        store.MarkMessageProcessed(messageId, DateTime.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        Assert.True(await store.HasProcessedMessageAsync(messageId, cancellationToken));
        Assert.Equal(
            pending,
            await store.FindPendingPaymentAsync(order.OrderId, cancellationToken)
        );
        store.RemovePendingPayment(pending);
        await store.SaveChangesAsync(cancellationToken);
        Assert.Null(await store.FindPendingPaymentAsync(order.OrderId, cancellationToken));

        DiningZone addedZone = Zone(restaurantId, "Anexo", 3);
        store.Add(addedZone);
        await store.SaveChangesAsync(cancellationToken);
        Assert.Equal(
            addedZone.Id,
            (await store.FindZoneAsync(restaurantId, addedZone.Id, cancellationToken))!.Id
        );

        store.Detach(loaded);
        Assert.Equal(EntityState.Detached, db.Entry(loaded).State);
        await using IDiningTransaction committed = await store.BeginTransactionAsync(
            cancellationToken
        );
        await committed.CommitAsync(cancellationToken);
        await using IDiningTransaction rolledBack = await store.BeginTransactionAsync(
            cancellationToken
        );
        await rolledBack.RollbackAsync(cancellationToken);
    }

    [Fact]
    public void Dining_migration_history_generates_a_script_with_session_safety_schema()
    {
        DbContextOptions<DiningDbContext> options = new DbContextOptionsBuilder<DiningDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=unused;Username=unused;Password=unused",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "dining")
            )
            .Options;
        using DiningDbContext db = new(options);
        AssertSnapshotMatchesCurrentModel(db);

        IMigrator migrator = db.GetService<IMigrator>();
        string script = migrator.GenerateScript();
        string rollback = migrator.GenerateScript(db.Database.GetMigrations().Last(), "0");

        Assert.Contains("CREATE TABLE dining_zones", script);
        Assert.Contains("CREATE TABLE dining_sessions", script);
        Assert.Contains("CREATE TABLE pending_payments", script);
        Assert.Contains("IX_dining_sessions_TableId_Status", script);
        Assert.Contains("WHERE \"Status\" = 'Open'", script);
        Assert.Contains("DROP TABLE dining_zones", rollback);
    }

    private static void AssertSnapshotMatchesCurrentModel(DbContext context)
    {
        ModelSnapshot snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!;
        var snapshotModel = context
            .GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot.Model, designTime: true);
        IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();

        Assert.False(
            differ.HasDifferences(
                snapshotModel.GetRelationalModel(),
                context.GetService<IDesignTimeModel>().Model.GetRelationalModel()
            )
        );
    }

    private static DiningDbContext CreateContext()
    {
        DbContextOptions<DiningDbContext> options = new DbContextOptionsBuilder<DiningDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings =>
                warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)
            )
            .Options;
        return new DiningDbContext(options);
    }

    private static DiningZone Zone(Guid restaurantId, string name, int sortOrder) =>
        new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Name = name,
            SortOrder = sortOrder,
        };

    private static RestaurantTable Table(
        Guid restaurantId,
        DiningZone zone,
        string code,
        string qrCode
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            ZoneId = zone.Id,
            Zone = zone,
            Code = code,
            Label = code,
            QrCode = qrCode,
            IsActive = true,
        };

    private static DiningSession Session(Guid restaurantId, Guid tableId) =>
        new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            TableId = tableId,
            Source = "Pos",
            CustomerAccessToken = Guid.NewGuid().ToString("N"),
            OpenedAtUtc = DateTime.UtcNow,
        };

    private static DiningSessionOrder Order(Guid sessionId, decimal amount) =>
        new()
        {
            OrderId = Guid.NewGuid(),
            DiningSessionId = sessionId,
            Source = "Pos",
            OrderStatus = "Submitted",
            Amount = amount,
            AddedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
}
