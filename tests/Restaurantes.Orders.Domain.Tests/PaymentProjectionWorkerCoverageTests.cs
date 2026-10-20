using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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

        public static async Task<WorkerFixture> CreateAsync()
        {
            OrderWriteDbContext db = new(
                new DbContextOptionsBuilder<OrderWriteDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );
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

        public async Task DeliverRawAsync(string eventType, byte[] body)
        {
            BasicProperties properties = new() { Type = eventType };
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
}
