using Microsoft.EntityFrameworkCore;
using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Domain;
using Restaurantes.CashRegister.Infrastructure.Persistence;
using Restaurantes.CashRegister.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashRegisterStoreTests
{
    [Fact]
    public async Task Current_and_history_load_movements_and_reconciliations_in_date_order()
    {
        Guid restaurantId = Guid.NewGuid();
        DateOnly businessDate = new(2026, 10, 19);
        await using CashRegisterDbContext db = CreateDb();
        CashRegisterSession newer = Session(restaurantId, businessDate, openedAtUtc: 2);
        newer.Movements.Add(Movement(newer, CashMovementType.Income, "Cash", 5m));
        newer.Reconciliations.Add(Reconciliation(newer, "Cash", 15m));
        CashRegisterSession older = Session(restaurantId, businessDate, openedAtUtc: 1);
        older.Status = CashRegisterStatus.Closed;
        CashRegisterSession otherDate = Session(restaurantId, businessDate.AddDays(-1), 3);
        otherDate.Status = CashRegisterStatus.Closed;
        db.Sessions.AddRange(newer, older, otherDate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        CashRegisterStore store = new(db);
        CashRegisterSession current = (
            await store.GetCurrentAsync(restaurantId, TestContext.Current.CancellationToken)
        )!;
        List<CashRegisterSession> allHistory = await store.GetHistoryAsync(
            restaurantId,
            null,
            TestContext.Current.CancellationToken
        );
        List<CashRegisterSession> datedHistory = await store.GetHistoryAsync(
            restaurantId,
            businessDate,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(newer.Id, current.Id);
        Assert.Single(current.Movements);
        Assert.Single(current.Reconciliations);
        Assert.Equal([otherDate.Id, newer.Id, older.Id], allHistory.Select(x => x.Id));
        Assert.Equal([newer.Id, older.Id], datedHistory.Select(x => x.Id));
    }

    [Fact]
    public async Task Session_and_open_queries_are_scoped_to_the_restaurant_and_status()
    {
        Guid restaurantId = Guid.NewGuid();
        CashRegisterSession open = Session(restaurantId, new DateOnly(2026, 10, 19), 1);
        CashRegisterSession closed = Session(restaurantId, new DateOnly(2026, 10, 18), 2);
        closed.Status = CashRegisterStatus.Closed;
        await using CashRegisterDbContext db = CreateDb();
        db.Sessions.AddRange(open, closed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CashRegisterStore store = new(db);

        Assert.True(
            await store.HasOpenSessionAsync(restaurantId, TestContext.Current.CancellationToken)
        );
        Assert.Equal(
            open.Id,
            (
                await store.GetOpenSessionAsync(restaurantId, TestContext.Current.CancellationToken)
            )?.Id
        );
        Assert.Equal(
            closed.Id,
            (
                await store.GetSessionAsync(
                    restaurantId,
                    closed.Id,
                    TestContext.Current.CancellationToken
                )
            )?.Id
        );
        Assert.Null(
            await store.GetSessionAsync(
                Guid.NewGuid(),
                open.Id,
                TestContext.Current.CancellationToken
            )
        );
        Assert.Null(
            await store.GetCurrentAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Projection_lookup_uses_session_id_or_the_session_open_at_payment_time()
    {
        Guid restaurantId = Guid.NewGuid();
        DateTime opened = new(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);
        CashRegisterSession prior = Session(restaurantId, new DateOnly(2026, 10, 19), 1);
        prior.OpenedAtUtc = opened;
        prior.ClosedAtUtc = opened.AddHours(1);
        CashRegisterSession active = Session(restaurantId, new DateOnly(2026, 10, 19), 2);
        active.OpenedAtUtc = opened.AddHours(1);
        await using CashRegisterDbContext db = CreateDb();
        db.Sessions.AddRange(prior, active);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        CashRegisterStore store = new(db);
        PaymentProjection payment = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            restaurantId,
            12m,
            "Cash",
            "order",
            opened.AddHours(1).AddMinutes(5)
        );

        CashRegisterSession? byId = await store.FindProjectionSessionAsync(
            payment,
            prior.Id,
            TestContext.Current.CancellationToken
        );
        CashRegisterSession? byTime = await store.FindProjectionSessionAsync(
            payment,
            null,
            TestContext.Current.CancellationToken
        );
        CashRegisterSession? outsideInterval = await store.FindProjectionSessionAsync(
            payment with
            {
                OccurredAtUtc = opened.AddDays(-1),
            },
            null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(prior.Id, byId?.Id);
        Assert.Equal(active.Id, byTime?.Id);
        Assert.Null(outsideInterval);
    }

    [Fact]
    public async Task Save_changes_persists_added_entities_and_processed_message()
    {
        await using CashRegisterDbContext db = CreateDb();
        CashRegisterStore store = new(db);
        CashRegisterSession session = Session(Guid.NewGuid(), new DateOnly(2026, 10, 19), 1);
        CashMovement movement = Movement(session, CashMovementType.Income, "Cash", 5m);
        CashRegisterReconciliation reconciliation = Reconciliation(session, "Cash", 5m);
        Guid messageId = Guid.NewGuid();
        store.AddSession(session);
        store.AddMovement(movement);
        store.AddReconciliation(reconciliation);
        store.MarkMessageProcessed(messageId, DateTime.UtcNow);

        await store.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(
            await store.HasProcessedMessageAsync(messageId, TestContext.Current.CancellationToken)
        );
        Assert.Equal(1, await db.Movements.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.Reconciliations.CountAsync(TestContext.Current.CancellationToken));
    }

    private static CashRegisterDbContext CreateDb() =>
        new(
            new DbContextOptionsBuilder<CashRegisterDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static CashRegisterSession Session(
        Guid restaurantId,
        DateOnly businessDate,
        int openedAtUtc
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            BusinessDate = businessDate,
            Status = CashRegisterStatus.Open,
            OpeningFloat = 10m,
            OpenedAtUtc = new DateTime(2026, 10, 19, openedAtUtc, 0, 0, DateTimeKind.Utc),
            OpenedByName = "Cajera",
        };

    private static CashMovement Movement(
        CashRegisterSession session,
        CashMovementType type,
        string method,
        decimal amount
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CashRegisterSessionId = session.Id,
            Session = session,
            Type = type,
            Method = method,
            Amount = amount,
        };

    private static CashRegisterReconciliation Reconciliation(
        CashRegisterSession session,
        string method,
        decimal amount
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CashRegisterSessionId = session.Id,
            Session = session,
            Method = method,
            ExpectedAmount = amount,
            ReconciledAmount = amount,
            Difference = 0m,
        };
}
