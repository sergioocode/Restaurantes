using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Domain;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashRegisterServiceTests
{
    [Fact]
    public async Task Card_overpayment_records_card_tender_and_cash_change_without_inflating_sales()
    {
        Guid restaurantId = Guid.NewGuid();
        CashRegisterSession session = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Status = CashRegisterStatus.Open,
            OpeningFloat = 20m,
        };
        TestStore store = new();
        store.AddSession(session);
        CashPaymentProjectionService projection = new(store, TimeProvider.System);
        PaymentProjection payment = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            restaurantId,
            11.90m,
            "Card",
            "CARD-1",
            DateTime.UtcNow,
            session.Id,
            20m,
            8.10m
        );

        await projection.Project(payment, CancellationToken.None);
        await projection.Project(payment, CancellationToken.None);

        Assert.Equal(3, session.Movements.Count);
        Assert.Equal(11.90m, session.Movements.Single(x => x.Type == CashMovementType.Sale).Amount);
        Assert.Equal(
            8.10m,
            session.Movements.Single(x => x.Type == CashMovementType.CardTender).Amount
        );
        Assert.Equal(
            -8.10m,
            session.Movements.Single(x => x.Type == CashMovementType.CashChange).Amount
        );
        CashRegisterService service = new(
            store,
            new TestIncompleteOrdersChecker(),
            TimeProvider.System,
            TimeZoneInfo.Utc
        );
        CashRegisterResponse current = Assert.IsType<CashRegisterResponse>(
            (await service.Current(restaurantId, CancellationToken.None)).Value
        );
        Assert.Equal(11.90m, current.TotalSales);
        Assert.Equal(11.90m, current.ExpectedTotal - current.OpeningFloat);
        Assert.Equal(11.90m, current.ExpectedCash);
    }

    [Fact]
    public async Task Open_session_keeps_its_business_date_after_midnight_until_closed()
    {
        Guid restaurantId = Guid.NewGuid();
        TestTimeProvider time = new(new DateTimeOffset(2026, 10, 4, 21, 30, 0, TimeSpan.Zero));
        TestStore store = new();
        CashRegisterService service = new(
            store,
            new TestIncompleteOrdersChecker(),
            time,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid")
        );
        CashRegisterUser user = new(Guid.NewGuid(), "Manager");

        CashRegisterResult opened = await service.Open(
            restaurantId,
            new OpenCashRegisterRequest(10),
            user,
            CancellationToken.None
        );
        CashRegisterResponse first = Assert.IsType<CashRegisterResponse>(opened.Value);
        Assert.Equal(new DateOnly(2026, 10, 4), first.BusinessDate);

        time.UtcNow = new DateTimeOffset(2026, 10, 4, 22, 30, 0, TimeSpan.Zero);
        CashRegisterResult availability = await service.IsOpen(
            restaurantId,
            CancellationToken.None
        );
        CashRegisterAvailabilityResponse available =
            Assert.IsType<CashRegisterAvailabilityResponse>(availability.Value);
        Assert.True(available.IsOpen);
        Assert.Equal(first.Id, available.SessionId);
        Assert.Equal(first.BusinessDate, available.BusinessDate);

        CashRegisterResult current = await service.Current(restaurantId, CancellationToken.None);
        Assert.Equal(first.Id, Assert.IsType<CashRegisterResponse>(current.Value).Id);
        Assert.Equal(
            CashRegisterOutcome.Conflict,
            (
                await service.Open(
                    restaurantId,
                    new OpenCashRegisterRequest(0),
                    user,
                    CancellationToken.None
                )
            ).Outcome
        );

        CashRegisterResult closed = await service.Close(
            restaurantId,
            first.Id,
            new CloseCashRegisterRequest(new Dictionary<string, decimal> { ["Cash"] = 10 }),
            user,
            "Bearer test-token",
            CancellationToken.None
        );
        CashRegisterResponse completed = Assert.IsType<CashRegisterResponse>(closed.Value);
        Assert.Equal("Closed", completed.Status);
        Assert.Equal(first.BusinessDate, completed.BusinessDate);

        CashRegisterResult next = await service.Open(
            restaurantId,
            new OpenCashRegisterRequest(0),
            user,
            CancellationToken.None
        );
        Assert.Equal(
            new DateOnly(2026, 10, 5),
            Assert.IsType<CashRegisterResponse>(next.Value).BusinessDate
        );
    }

    [Fact]
    public async Task Close_keeps_session_open_when_incomplete_orders_exist()
    {
        Guid restaurantId = Guid.NewGuid();
        TestStore store = new();
        TestIncompleteOrdersChecker incompleteOrders = new(2);
        CashRegisterService service = new(
            store,
            incompleteOrders,
            new TestTimeProvider(new DateTimeOffset(2026, 10, 4, 21, 30, 0, TimeSpan.Zero)),
            TimeZoneInfo.Utc
        );
        CashRegisterUser user = new(Guid.NewGuid(), "Manager");
        CashRegisterResponse opened = Assert.IsType<CashRegisterResponse>(
            (
                await service.Open(
                    restaurantId,
                    new OpenCashRegisterRequest(0),
                    user,
                    CancellationToken.None
                )
            ).Value
        );

        CashRegisterResult result = await service.Close(
            restaurantId,
            opened.Id,
            new CloseCashRegisterRequest(new Dictionary<string, decimal> { ["Cash"] = 0 }),
            user,
            "Bearer test-token",
            CancellationToken.None
        );

        Assert.Equal(CashRegisterOutcome.Conflict, result.Outcome);
        Assert.Contains("2 pedidos incompletos", result.Value!.ToString());
        Assert.Equal(CashRegisterStatus.Open, store.Sessions.Single().Status);
        Assert.Null(store.Sessions.Single().ClosedAtUtc);
        Assert.Empty(store.Sessions.Single().Reconciliations);
        Assert.Equal(restaurantId, incompleteOrders.LastRestaurantId);
        Assert.Equal("Bearer test-token", incompleteOrders.LastAuthorization);
    }

    [Fact]
    public async Task Close_fails_closed_when_orders_cannot_be_checked()
    {
        Guid restaurantId = Guid.NewGuid();
        TestStore store = new();
        TestIncompleteOrdersChecker incompleteOrders = new(
            new IncompleteOrdersUnavailableException(new HttpRequestException())
        );
        CashRegisterService service = new(
            store,
            incompleteOrders,
            new TestTimeProvider(new DateTimeOffset(2026, 10, 4, 21, 30, 0, TimeSpan.Zero)),
            TimeZoneInfo.Utc
        );
        CashRegisterUser user = new(Guid.NewGuid(), "Manager");
        CashRegisterResponse opened = Assert.IsType<CashRegisterResponse>(
            (
                await service.Open(
                    restaurantId,
                    new OpenCashRegisterRequest(0),
                    user,
                    CancellationToken.None
                )
            ).Value
        );

        CashRegisterResult result = await service.Close(
            restaurantId,
            opened.Id,
            new CloseCashRegisterRequest(new Dictionary<string, decimal> { ["Cash"] = 0 }),
            user,
            "Bearer test-token",
            CancellationToken.None
        );

        Assert.Equal(CashRegisterOutcome.ServiceUnavailable, result.Outcome);
        Assert.Equal(CashRegisterStatus.Open, store.Sessions.Single().Status);
        Assert.Empty(store.Sessions.Single().Reconciliations);
    }

    [Fact]
    public async Task New_session_uses_the_configured_time_zone()
    {
        TestTimeProvider time = new(new DateTimeOffset(2026, 10, 4, 22, 30, 0, TimeSpan.Zero));
        CashRegisterService service = new(
            new TestStore(),
            new TestIncompleteOrdersChecker(),
            time,
            TimeZoneInfo.Utc
        );

        CashRegisterResult result = await service.Open(
            Guid.NewGuid(),
            new OpenCashRegisterRequest(0),
            new CashRegisterUser(Guid.NewGuid(), "Manager"),
            CancellationToken.None
        );

        Assert.Equal(
            new DateOnly(2026, 10, 4),
            Assert.IsType<CashRegisterResponse>(result.Value).BusinessDate
        );
    }

    [Fact]
    public async Task Close_preserves_reconciliation_for_cash_card_and_online_payments()
    {
        Guid restaurantId = Guid.NewGuid();
        TestStore store = new();
        CashRegisterService service = new(
            store,
            new TestIncompleteOrdersChecker(),
            TimeProvider.System,
            TimeZoneInfo.Utc
        );
        CashRegisterUser user = new(Guid.NewGuid(), "Manager");
        await service.Open(
            restaurantId,
            new OpenCashRegisterRequest(10),
            user,
            CancellationToken.None
        );
        CashRegisterSession session = store.Sessions.Single();
        foreach (
            (string method, decimal amount) in new[] { ("Cash", 5m), ("Card", 8m), ("Online", 9m) }
        )
        {
            session.Movements.Add(
                new CashMovement
                {
                    Id = Guid.NewGuid(),
                    CashRegisterSessionId = session.Id,
                    Type = CashMovementType.Sale,
                    Method = method,
                    Amount = amount,
                }
            );
        }
        CashRegisterResult result = await service.Close(
            restaurantId,
            session.Id,
            new CloseCashRegisterRequest(
                new()
                {
                    ["Cash"] = 15,
                    ["Card"] = 8,
                    ["Online"] = 9,
                }
            ),
            user,
            "Bearer test-token",
            CancellationToken.None
        );

        CashRegisterResponse closed = Assert.IsType<CashRegisterResponse>(result.Value);
        Assert.Equal("Closed", closed.Status);
        Assert.Equal(22, closed.TotalSales);
        Assert.Equal(5, closed.CashSales);
        Assert.Equal(15, closed.ExpectedCash);
        Assert.Equal(32, closed.ExpectedTotal);
        Assert.Equal(0m, closed.Difference);
        Assert.Equal(3, closed.PaymentMethods.Count);
        Assert.Equal(8, closed.PaymentMethods.Single(method => method.Method == "Card").Sales);
        Assert.Equal(9, closed.PaymentMethods.Single(method => method.Method == "Online").Sales);
        Assert.All(closed.Reconciliations, method => Assert.Equal(0, method.Difference));
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return UtcNow;
        }
    }

    private sealed class TestStore : ICashRegisterStore
    {
        private readonly List<CashRegisterSession> sessions = [];
        private readonly HashSet<Guid> processedMessages = [];

        public IReadOnlyList<CashRegisterSession> Sessions => sessions;

        public Task<CashRegisterSession?> GetOpenSessionAsync(
            Guid restaurantId,
            CancellationToken ct
        )
        {
            return Task.FromResult(
                sessions.SingleOrDefault(x =>
                    x.RestaurantId == restaurantId && x.Status == CashRegisterStatus.Open
                )
            );
        }

        public Task<CashRegisterSession?> GetCurrentAsync(Guid restaurantId, CancellationToken ct)
        {
            return GetOpenSessionAsync(restaurantId, ct);
        }

        public Task<List<CashRegisterSession>> GetHistoryAsync(
            Guid restaurantId,
            int take,
            CancellationToken ct
        )
        {
            return Task.FromResult(
                sessions.Where(x => x.RestaurantId == restaurantId).Take(take).ToList()
            );
        }

        public async Task<bool> HasOpenSessionAsync(Guid restaurantId, CancellationToken ct)
        {
            return await GetOpenSessionAsync(restaurantId, ct) is not null;
        }

        public Task<CashRegisterSession?> GetSessionAsync(
            Guid restaurantId,
            Guid sessionId,
            CancellationToken ct
        )
        {
            return Task.FromResult(
                sessions.SingleOrDefault(x => x.RestaurantId == restaurantId && x.Id == sessionId)
            );
        }

        public Task<bool> HasProcessedMessageAsync(Guid messageId, CancellationToken ct)
        {
            return Task.FromResult(processedMessages.Contains(messageId));
        }

        public Task<CashRegisterSession?> FindProjectionSessionAsync(
            PaymentProjection payment,
            Guid? sessionId,
            CancellationToken ct
        )
        {
            return Task.FromResult(sessions.SingleOrDefault(x => x.Id == sessionId));
        }

        public void AddSession(CashRegisterSession session)
        {
            sessions.Add(session);
        }

        public void AddReconciliation(CashRegisterReconciliation reconciliation)
        {
            sessions
                .Single(x => x.Id == reconciliation.CashRegisterSessionId)
                .Reconciliations.Add(reconciliation);
        }

        public void AddMovement(CashMovement movement)
        {
            sessions.Single(x => x.Id == movement.CashRegisterSessionId).Movements.Add(movement);
        }

        public void MarkMessageProcessed(Guid messageId, DateTime processedAtUtc)
        {
            processedMessages.Add(messageId);
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestIncompleteOrdersChecker(
        int count = 0,
        IncompleteOrdersUnavailableException? exception = null
    ) : IIncompleteOrdersChecker
    {
        public Guid LastRestaurantId { get; private set; }
        public string LastAuthorization { get; private set; } = string.Empty;

        public Task<int> CountAsync(Guid restaurantId, string authorization, CancellationToken ct)
        {
            LastRestaurantId = restaurantId;
            LastAuthorization = authorization;
            return exception is not null ? throw exception : Task.FromResult(count);
        }

        public TestIncompleteOrdersChecker(IncompleteOrdersUnavailableException exception)
            : this(0, exception) { }
    }
}
