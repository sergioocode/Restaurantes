using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.Orders.Infrastructure.Workers;
using Restaurantes.Payments.Contracts.Events;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class PaymentProjectionWorkerCoverageTests
{
    [Fact]
    public async Task Captures_update_projection_and_refund_keeps_capture_timestamp()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid orderId = Guid.NewGuid();
        Guid firstPaymentId = Guid.NewGuid();
        DateTime capturedAt = new(2026, 10, 20, 10, 15, 0, DateTimeKind.Utc);
        PaymentCaptured partial = new(
            firstPaymentId,
            orderId,
            Guid.NewGuid(),
            18m,
            "Cash",
            "TakeAway",
            "Pos",
            "",
            capturedAt,
            AmountPaid: 7m,
            IsFullyPaid: false
        );

        await fixture.DeliverAsync(typeof(PaymentCaptured).FullName!, partial);

        OrderPaymentProjection projection = await fixture.Db.OrderPayments.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(firstPaymentId, projection.PaymentId);
        Assert.Equal(7m, projection.Amount);
        Assert.Equal("Cash", projection.Method);
        Assert.Equal("PartiallyPaid", projection.Status);
        Assert.Equal(capturedAt, projection.CapturedAtUtc);

        Guid secondPaymentId = Guid.NewGuid();
        PaymentCaptured completed = partial with
        {
            PaymentId = secondPaymentId,
            Amount = 18m,
            AmountPaid = 18m,
            Method = "Card",
            IsFullyPaid = true,
            CapturedAtUtc = capturedAt.AddMinutes(1),
        };
        await fixture.DeliverAsync(typeof(PaymentCaptured).FullName!, completed);

        Assert.Equal(secondPaymentId, projection.PaymentId);
        Assert.Equal(18m, projection.Amount);
        Assert.Equal("Card", projection.Method);
        Assert.Equal("Paid", projection.Status);
        Assert.Equal(capturedAt.AddMinutes(1), projection.CapturedAtUtc);

        DateTime refundedAt = capturedAt.AddMinutes(5);
        PaymentRefunded refund = new(
            Guid.NewGuid(),
            orderId,
            completed.RestaurantId,
            18m,
            "Card",
            "TakeAway",
            "Pos",
            "Duplicado",
            refundedAt
        );
        await fixture.DeliverAsync(typeof(PaymentRefunded).FullName!, refund);

        Assert.Equal(refund.PaymentId, projection.PaymentId);
        Assert.Equal(18m, projection.Amount);
        Assert.Equal("Refunded", projection.Status);
        Assert.Equal(refundedAt, projection.UpdatedAtUtc);
        Assert.Equal(capturedAt.AddMinutes(1), projection.CapturedAtUtc);
        Assert.Equal(3, fixture.ChannelProxy.Acknowledgements);
    }

    [Fact]
    public async Task Capture_without_paid_amount_uses_total_and_refund_can_create_projection()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid orderId = Guid.NewGuid();
        DateTime capturedAt = new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
        PaymentCaptured capture = new(
            Guid.NewGuid(),
            orderId,
            Guid.NewGuid(),
            24m,
            "Transfer",
            "TakeAway",
            "Pos",
            "REF-789",
            capturedAt,
            IsFullyPaid: true
        );

        await fixture.DeliverAsync(typeof(PaymentCaptured).FullName!, capture);

        OrderPaymentProjection capturedProjection = await fixture.Db.OrderPayments.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(24m, capturedProjection.Amount);
        Assert.Equal("Paid", capturedProjection.Status);
        Assert.Equal(capturedAt, capturedProjection.CapturedAtUtc);
        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);

        PaymentRefunded refund = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            11m,
            "Cash",
            "TakeAway",
            "Pos",
            "Reembolso",
            capturedAt.AddHours(1)
        );
        await fixture.DeliverAsync(typeof(PaymentRefunded).FullName!, refund);

        OrderPaymentProjection[] projections = await fixture
            .Db.OrderPayments.OrderBy(item => item.OrderId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, projections.Length);
        OrderPaymentProjection refundedProjection = Assert.Single(
            projections,
            item => item.OrderId == refund.OrderId
        );
        Assert.Equal(refund.PaymentId, refundedProjection.PaymentId);
        Assert.Equal(11m, refundedProjection.Amount);
        Assert.Equal("Refunded", refundedProjection.Status);
        Assert.Null(refundedProjection.CapturedAtUtc);
        Assert.Equal(capturedAt.AddHours(1), refundedProjection.UpdatedAtUtc);
        Assert.Equal(2, fixture.ChannelProxy.Acknowledgements);
    }

    [Theory]
    [InlineData("unknown.event", "{}")]
    [InlineData("Restaurantes.Payments.Contracts.Events.PaymentCaptured", "null")]
    public async Task Unknown_or_empty_events_are_rejected(string eventType, string body)
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        await fixture.DeliverRawAsync(eventType, System.Text.Encoding.UTF8.GetBytes(body));

        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
    }

    [Fact]
    public async Task Storage_failure_schedules_payment_event_retry()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(failOnSave: true);

        await fixture.DeliverAsync(
            typeof(PaymentCaptured).FullName!,
            new PaymentCaptured(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                12m,
                "Card",
                "DineIn",
                "Pos",
                "",
                DateTime.UtcNow,
                IsFullyPaid: true
            )
        );

        var retry = Assert.Single(fixture.ChannelProxy.PublishedMessages);
        Assert.Equal($"{PaymentsTopology.OrdersIntegrationQueueName}.retry.1", retry.RoutingKey);
        Assert.Equal(1, retry.Properties.Headers!["x-restaurantes-retry-count"]);
        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(0, fixture.ChannelProxy.Rejections);
    }

    [Fact]
    public async Task Storage_failure_at_retry_limit_is_rejected_without_requeue()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(failOnSave: true);
        BasicProperties properties = new()
        {
            Type = typeof(PaymentCaptured).FullName,
            Headers = new Dictionary<string, object?>
            {
                ["x-restaurantes-retry-count"] = RabbitMqRetry.MaximumAttempts,
            },
        };

        await fixture.DeliverRawAsync(
            properties,
            JsonSerializer.SerializeToUtf8Bytes(
                new PaymentCaptured(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    12m,
                    "Card",
                    "DineIn",
                    "Pos",
                    "",
                    DateTime.UtcNow,
                    IsFullyPaid: true
                )
            )
        );

        Assert.Empty(fixture.ChannelProxy.PublishedMessages);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
    }

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly PaymentProjectionWorker worker;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            OrderWriteDbContext db,
            PaymentProjectionWorker worker,
            IChannel channel,
            CatalogProjectionWorkerCoverageTests.ChannelProxy channelProxy
        )
        {
            this.services = services;
            Db = db;
            this.worker = worker;
            this.channel = channel;
            ChannelProxy = channelProxy;
        }

        public OrderWriteDbContext Db { get; }
        public CatalogProjectionWorkerCoverageTests.ChannelProxy ChannelProxy { get; }

        public static async Task<WorkerFixture> CreateAsync(bool failOnSave = false)
        {
            DbContextOptionsBuilder<OrderWriteDbContext> optionsBuilder =
                new DbContextOptionsBuilder<OrderWriteDbContext>().UseInMemoryDatabase(
                    Guid.NewGuid().ToString()
                );
            if (failOnSave)
            {
                optionsBuilder.AddInterceptors(new ThrowOnSaveChangesInterceptor());
            }
            OrderWriteDbContext db = new(optionsBuilder.Options);
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider();
            PaymentProjectionWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                NullLogger<PaymentProjectionWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<
                IChannel,
                CatalogProjectionWorkerCoverageTests.ChannelProxy
            >();
            CatalogProjectionWorkerCoverageTests.ChannelProxy proxy =
                (CatalogProjectionWorkerCoverageTests.ChannelProxy)(object)channel;
            typeof(PaymentProjectionWorker)
                .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(worker, channel);
            return new WorkerFixture(services, db, worker, channel, proxy);
        }

        public Task DeliverAsync<TEvent>(string eventType, TEvent message) =>
            DeliverRawAsync(eventType, JsonSerializer.SerializeToUtf8Bytes(message));

        public Task DeliverRawAsync(string eventType, byte[] body) =>
            DeliverRawAsync(new BasicProperties { Type = eventType }, body);

        public async Task DeliverRawAsync(BasicProperties properties, byte[] body)
        {
            BasicDeliverEventArgs args = new(
                "consumer",
                (ulong)(ChannelProxy.Acknowledgements + ChannelProxy.Rejections + 1),
                false,
                "exchange",
                "routing-key",
                properties,
                body,
                TestContext.Current.CancellationToken
            );
            MethodInfo handle = typeof(PaymentProjectionWorker).GetMethod(
                "Handle",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)handle.Invoke(worker, [channel, args])!;
        }

        public async ValueTask DisposeAsync() => await services.DisposeAsync();
    }

    private sealed class ThrowOnSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Temporary projection storage failure.");
    }

    private sealed record PublishedRetry(string RoutingKey, BasicProperties Properties);
}
