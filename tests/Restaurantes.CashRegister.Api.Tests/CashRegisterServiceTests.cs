using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Domain;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashRegisterServiceTests
{
    [Fact]
    public async Task IsOpen_returns_false_when_restaurant_has_no_open_session()
    {
        Guid restaurantId = Guid.NewGuid();
        CashRegisterService service = new(
            new TestStore(),
            new TestIncompleteOrdersChecker(),
            TimeProvider.System,
            TimeZoneInfo.Utc
        );

        CashRegisterResult result = await service.IsOpen(restaurantId, CancellationToken.None);

        CashRegisterAvailabilityResponse availability =
            Assert.IsType<CashRegisterAvailabilityResponse>(result.Value);
        Assert.Equal(CashRegisterOutcome.Ok, result.Outcome);
        Assert.False(availability.IsOpen);
        Assert.Null(availability.SessionId);
    }

    [Fact]
    public async Task IsOpen_returns_open_session_and_its_business_date()
    {
        Guid restaurantId = Guid.NewGuid();
        DateOnly businessDate = new(2026, 10, 19);
        Guid sessionId = Guid.NewGuid();
        TestStore store = new();
        store.AddSession(
            new CashRegisterSession
            {
                Id = sessionId,
                RestaurantId = restaurantId,
                BusinessDate = businessDate,
                Status = CashRegisterStatus.Open,
            }
        );
        CashRegisterService service = new(
            store,
            new TestIncompleteOrdersChecker(),
            TimeProvider.System,
            TimeZoneInfo.Utc
        );

        CashRegisterResult result = await service.IsOpen(restaurantId, CancellationToken.None);

        CashRegisterAvailabilityResponse availability =
            Assert.IsType<CashRegisterAvailabilityResponse>(result.Value);
        Assert.True(availability.IsOpen);
        Assert.Equal(sessionId, availability.SessionId);
        Assert.Equal(businessDate, availability.BusinessDate);
    }

    [Fact]
    public async Task History_filters_cash_sessions_by_business_date()
    {
        Guid restaurantId = Guid.NewGuid();
        DateOnly selectedDate = new(2026, 10, 19);
        TestStore store = new();
        store.AddSession(
            new CashRegisterSession
            {
                Id = Guid.NewGuid(),
                RestaurantId = restaurantId,
                BusinessDate = selectedDate,
                Status = CashRegisterStatus.Closed,
            }
        );
        store.AddSession(
            new CashRegisterSession
            {
                Id = Guid.NewGuid(),
                RestaurantId = restaurantId,
                BusinessDate = selectedDate.AddDays(-1),
                Status = CashRegisterStatus.Closed,
            }
        );
        CashRegisterService service = new(
            store,
            new TestIncompleteOrdersChecker(),
            TimeProvider.System,
            TimeZoneInfo.Utc
        );

        CashRegisterResult result = await service.History(
            restaurantId,
            selectedDate,
            CancellationToken.None
        );

        IEnumerable<CashRegisterResponse> sessions = Assert.IsType<
            IEnumerable<CashRegisterResponse>
        >(result.Value, exactMatch: false);
        Assert.Equal(selectedDate, Assert.Single(sessions).BusinessDate);
    }

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
    public async Task Projection_resolves_session_from_reference_when_session_id_is_missing()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        CashRegisterSession session = new()
        {
            Id = sessionId,
            RestaurantId = restaurantId,
            Status = CashRegisterStatus.Open,
        };
        TestStore store = new();
        store.AddSession(session);
        CashPaymentProjectionService projection = new(store, TimeProvider.System);
        PaymentProjection payment = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            restaurantId,
            12m,
            "Cash",
            $"cashregister:{sessionId};source=pos",
            DateTime.UtcNow
        );

        await projection.Project(payment, CancellationToken.None);

        Assert.Equal(sessionId, Assert.Single(session.Movements).CashRegisterSessionId);
    }

    [Fact]
    public async Task Projection_rejects_payment_when_no_session_matches_reference()
    {
        Guid restaurantId = Guid.NewGuid();
        TestStore store = new();
        CashPaymentProjectionService projection = new(store, TimeProvider.System);
        PaymentProjection payment = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            restaurantId,
            12m,
            "Cash",
            "cashregister:not-a-guid",
            DateTime.UtcNow
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                projection.Project(payment, CancellationToken.None)
        );

        Assert.Contains(payment.PaymentId.ToString(), exception.Message);
        Assert.Empty(store.Sessions);
    }

    [Fact]
    public async Task Late_projection_updates_closed_session_reconciliation()
    {
        Guid restaurantId = Guid.NewGuid();
        CashRegisterSession session = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Status = CashRegisterStatus.Closed,
            OpeningFloat = 30m,
            ExpectedTotalAtClose = 40m,
            ReconciledTotalAtClose = 42m,
            ExpectedCashAtClose = 40m,
            DifferenceAtClose = 2m,
            Version = 3,
        };
        session.Reconciliations.Add(
            new CashRegisterReconciliation
            {
                Id = Guid.NewGuid(),
                CashRegisterSessionId = session.Id,
                Method = "Cash",
                ExpectedAmount = 40m,
                ReconciledAmount = 41m,
                Difference = 1m,
            }
        );
        TestStore store = new();
        store.AddSession(session);
        CashPaymentProjectionService projection = new(store, TimeProvider.System);
        PaymentProjection payment = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            restaurantId,
            5m,
            "Cash",
            $"CASHREGISTER:{session.Id}",
            DateTime.UtcNow
        );

        await projection.Project(payment, CancellationToken.None);

        CashRegisterReconciliation reconciliation = Assert.Single(session.Reconciliations);
        Assert.Equal(45m, reconciliation.ExpectedAmount);
        Assert.Equal(-4m, reconciliation.Difference);
        Assert.Equal(45m, session.ExpectedTotalAtClose);
        Assert.Equal(-3m, session.DifferenceAtClose);
        Assert.Equal(45m, session.ExpectedCashAtClose);
        Assert.Equal(4, session.Version);
    }

    [Fact]
    public async Task Manual_income_and_expense_update_the_session_balance_and_keep_audit_details()
    {
        Guid restaurantId = Guid.NewGuid();
        CashRegisterSession session = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Status = CashRegisterStatus.Open,
            OpeningFloat = 20m,
        };
        session.Movements.Add(
            new CashMovement
            {
                Id = Guid.NewGuid(),
                CashRegisterSessionId = session.Id,
                Type = CashMovementType.Sale,
                Method = "Cash",
                Amount = 10m,
            }
        );
        TestStore store = new();
        store.AddSession(session);
        TestTimeProvider time = new(new DateTimeOffset(2026, 10, 19, 17, 45, 12, TimeSpan.Zero));
        CashRegisterService service = new(
            store,
            new TestIncompleteOrdersChecker(),
            time,
            TimeZoneInfo.Utc
        );
        CashRegisterUser user = new(Guid.NewGuid(), "Cajera");

        CashRegisterResult expense = await service.CreateMovement(
            restaurantId,
            session.Id,
            new CreateCashMovementRequest(
                "Expense",
                "Cash",
                25m,
                "Pago a proveedor",
                "Proveedor A"
            ),
            user,
            CancellationToken.None
        );
        CashRegisterResult income = await service.CreateMovement(
            restaurantId,
            session.Id,
            new CreateCashMovementRequest(
                "Income",
                "Cash",
                2m,
                "Aportación de caja",
                "Cambio inicial"
            ),
            user,
            CancellationToken.None
        );

        Assert.Equal(CashRegisterOutcome.Ok, expense.Outcome);
        CashRegisterResponse current = Assert.IsType<CashRegisterResponse>(income.Value);
        Assert.Equal(10m, current.TotalSales);
        Assert.Equal(2m, current.OtherIncomeTotal);
        Assert.Equal(25m, current.OtherExpenseTotal);
        Assert.Equal(7m, current.ExpectedCash);
        Assert.Equal(7m, current.ExpectedTotal);
        CashMovementResponse savedExpense = Assert.Single(
            current.Movements,
            movement => movement.Type == "Expense"
        );
        Assert.Equal("Proveedor A", savedExpense.Reference);
        Assert.Equal("Pago a proveedor", savedExpense.Category);
        Assert.Equal("Cajera", savedExpense.RecordedByName);
        Assert.Equal(time.UtcNow.UtcDateTime, savedExpense.OccurredAtUtc);
    }

    [Fact]
    public async Task Manual_expense_cannot_exceed_the_expected_method_balance()
    {
        Guid restaurantId = Guid.NewGuid();
        CashRegisterSession session = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Status = CashRegisterStatus.Open,
            OpeningFloat = 10m,
        };
        TestStore store = new();
        store.AddSession(session);
        CashRegisterService service = new(
            store,
            new TestIncompleteOrdersChecker(),
            TimeProvider.System,
            TimeZoneInfo.Utc
        );

        CashRegisterResult result = await service.CreateMovement(
            restaurantId,
            session.Id,
            new CreateCashMovementRequest("Expense", "Cash", 11m, "Gastos varios", "Compra"),
            new CashRegisterUser(Guid.NewGuid(), "Cajera"),
            CancellationToken.None
        );

        Assert.Equal(CashRegisterOutcome.Validation, result.Outcome);
        Assert.Empty(session.Movements);
    }

    [Fact]
    public async Task Transfer_expense_is_reconciled_as_a_negative_net_movement()
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

        CashRegisterResult expense = await service.CreateMovement(
            restaurantId,
            opened.Id,
            new CreateCashMovementRequest("Expense", "Transfer", 30m, "Reparación", "Técnico"),
            user,
            CancellationToken.None
        );
        CashRegisterResponse withExpense = Assert.IsType<CashRegisterResponse>(expense.Value);
        Assert.Equal(
            -30m,
            withExpense.ExpectedByMethod.Single(item => item.Method == "Transfer").Expected
        );

        CashRegisterResult closed = await service.Close(
            restaurantId,
            opened.Id,
            new CloseCashRegisterRequest(new() { ["Cash"] = 0, ["Transfer"] = -30 }),
            user,
            "Bearer test-token",
            CancellationToken.None
        );

        CashRegisterResponse completed = Assert.IsType<CashRegisterResponse>(closed.Value);
        Assert.Equal("Closed", completed.Status);
        Assert.Equal(
            -30m,
            completed.Reconciliations.Single(item => item.Method == "Transfer").Expected
        );
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
            DateOnly? businessDate,
            CancellationToken ct
        )
        {
            return Task.FromResult(
                sessions
                    .Where(x =>
                        x.RestaurantId == restaurantId
                        && (businessDate is null || x.BusinessDate == businessDate)
                    )
                    .ToList()
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
