using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Domain;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashRegisterServiceTests
{
    [Fact]
    public async Task Open_session_keeps_its_business_date_after_midnight_until_closed()
    {
        Guid restaurantId = Guid.NewGuid();
        TestTimeProvider time = new(new DateTimeOffset(2026, 10, 4, 21, 30, 0, TimeSpan.Zero));
        TestStore store = new();
        CashRegisterService service = new(
            store,
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
    public async Task New_session_uses_the_configured_time_zone()
    {
        TestTimeProvider time = new(new DateTimeOffset(2026, 10, 4, 22, 30, 0, TimeSpan.Zero));
        CashRegisterService service = new(new TestStore(), time, TimeZoneInfo.Utc);

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

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class TestStore : ICashRegisterStore
    {
        private readonly List<CashRegisterSession> sessions = [];

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
        ) =>
            Task.FromResult(
                sessions.Where(x => x.RestaurantId == restaurantId).Take(take).ToList()
            );

        public async Task<bool> HasOpenSessionAsync(Guid restaurantId, CancellationToken ct)
        {
            return await GetOpenSessionAsync(restaurantId, ct) is not null;
        }

        public Task<CashRegisterSession?> GetSessionAsync(
            Guid restaurantId,
            Guid sessionId,
            CancellationToken ct
        ) =>
            Task.FromResult(
                sessions.SingleOrDefault(x => x.RestaurantId == restaurantId && x.Id == sessionId)
            );

        public Task<bool> HasProcessedMessageAsync(Guid messageId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<Guid?> GetSaleSessionIdAsync(Guid paymentId, CancellationToken ct) =>
            Task.FromResult<Guid?>(null);

        public Task<CashRegisterSession?> FindProjectionSessionAsync(
            PaymentProjection payment,
            Guid? sessionId,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public void AddSession(CashRegisterSession session) => sessions.Add(session);

        public void AddReconciliation(CashRegisterReconciliation reconciliation)
        {
            sessions
                .Single(x => x.Id == reconciliation.CashRegisterSessionId)
                .Reconciliations.Add(reconciliation);
        }

        public void AddMovement(CashMovement movement) => throw new NotSupportedException();

        public void MarkMessageProcessed(Guid messageId, DateTime processedAtUtc) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
