using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Payments.Domain;
using Restaurantes.Payments.Infrastructure.Persistence.Write;
using Restaurantes.Payments.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.Payments.Api.Tests;

public sealed class PayableOrderProjectionWorkerCoverageTests
{
    [Fact]
    public async Task Order_creation_and_cancellation_update_payable_amount_once_per_event()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid createdMessageId = Guid.NewGuid();
        OrderCreated created = new(
            orderId,
            restaurantId,
            Guid.NewGuid(),
            3,
            sessionId,
            "Mesa 2",
            "Nora",
            "DineIn",
            "Pos",
            "OnAccount",
            1,
            new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc),
            [Line(10m, 2), Line(3m, 1)],
            []
        );

        await fixture.DeliverAsync(createdMessageId, typeof(OrderCreated).FullName!, created);

        PayableOrder payable = await fixture.Db.PayableOrders.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(orderId, payable.OrderId);
        Assert.Equal(restaurantId, payable.RestaurantId);
        Assert.Equal(created.TableId, payable.TableId);
        Assert.Equal(sessionId, payable.DiningSessionId);
        Assert.Equal("DineIn", payable.ServiceMode);
        Assert.Equal("Pos", payable.Source);
        Assert.Equal(23m, payable.Amount);
        Assert.Equal(PaymentStatus.Open, payable.Status);
        Assert.Single(fixture.Db.InboxMessages);

        await fixture.DeliverAsync(
            createdMessageId,
            typeof(OrderCreated).FullName!,
            created with
            {
                Lines = [Line(100m, 1)],
            }
        );
        Assert.Equal(23m, payable.Amount);
        Assert.Single(fixture.Db.InboxMessages);

        OrderLineCancelled cancelled = new(
            orderId,
            restaurantId,
            created.TableId,
            3,
            sessionId,
            "Mesa 2",
            "Nora",
            "DineIn",
            "Pos",
            "OnAccount",
            Guid.NewGuid(),
            "CALIENTE",
            "Sin stock",
            "Submitted",
            13m,
            2,
            created.OccurredAtUtc,
            created.OccurredAtUtc,
            created.OccurredAtUtc.AddMinutes(2),
            [Line(10m, 1)],
            []
        );
        await fixture.DeliverAsync(Guid.NewGuid(), typeof(OrderLineCancelled).FullName!, cancelled);

        Assert.Equal(13m, payable.Amount);
        Assert.Equal(2, payable.Version);
        Assert.Equal(
            2,
            await fixture.Db.InboxMessages.CountAsync(TestContext.Current.CancellationToken)
        );
        Assert.Equal(3, fixture.ChannelProxy.Acknowledgements);
    }

    [Fact]
    public async Task Duplicate_order_creation_keeps_the_existing_payable_order()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid orderId = Guid.NewGuid();
        OrderCreated created = new(
            orderId,
            Guid.NewGuid(),
            null,
            null,
            null,
            "Mesa",
            "Cliente",
            "DineIn",
            "Pos",
            "OnAccount",
            1,
            DateTime.UtcNow,
            [Line(10m, 1)],
            []
        );
        fixture.Db.PayableOrders.Add(
            PayableOrder.Create(
                orderId,
                created.RestaurantId,
                null,
                null,
                "DineIn",
                "Pos",
                10m,
                DateTime.UtcNow
            )
        );
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await fixture.DeliverAsync(Guid.NewGuid(), typeof(OrderCreated).FullName!, created);

        Assert.Single(fixture.Db.PayableOrders);
        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
        Assert.Single(fixture.Db.InboxMessages);
    }

    [Fact]
    public async Task Message_is_ignored_when_worker_channel_is_unavailable()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        fixture.ClearWorkerChannel();

        await fixture.DeliverAsync(
            Guid.NewGuid(),
            typeof(OrderCreated).FullName!,
            new OrderCreated(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                null,
                null,
                "Mesa",
                "Cliente",
                "DineIn",
                "Pos",
                "OnAccount",
                1,
                DateTime.UtcNow,
                [],
                []
            )
        );

        Assert.Empty(fixture.Db.PayableOrders);
        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(0, fixture.ChannelProxy.Rejections);
    }

    [Fact]
    public async Task Worker_disposal_closes_and_clears_its_channel()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        await fixture.DisposeWorkerResourcesAsync();

        Assert.Equal(1, fixture.ChannelProxy.Disposals);
        Assert.Null(fixture.WorkerChannel);
    }

    [Theory]
    [InlineData("{guid}", "unknown.event", "{}")]
    [InlineData("{guid}", "Restaurantes.Orders.Contracts.Events.OrderCreated", "null")]
    public async Task Invalid_or_unsupported_order_events_are_dead_lettered(
        string messageId,
        string eventType,
        string body
    )
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        await fixture.DeliverRawAsync(
            messageId == "{guid}" ? Guid.NewGuid().ToString() : messageId,
            eventType,
            System.Text.Encoding.UTF8.GetBytes(body)
        );

        Assert.Empty(fixture.Db.PayableOrders);
        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
    }

    [Fact]
    public async Task Cancellation_for_an_order_not_yet_projected_is_dead_lettered()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        OrderLineCancelled cancelled = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            "",
            "",
            "DineIn",
            "Pos",
            "OnAccount",
            Guid.NewGuid(),
            "COCINA",
            "Cancelada",
            "Submitted",
            0m,
            1,
            DateTime.UtcNow,
            null,
            DateTime.UtcNow,
            [],
            []
        );

        await fixture.DeliverAsync(Guid.NewGuid(), typeof(OrderLineCancelled).FullName!, cancelled);

        Assert.Equal(1, fixture.ChannelProxy.Rejections);
        Assert.Empty(fixture.Db.InboxMessages);
    }

    [Fact]
    public async Task Invalid_cancelled_amount_is_scheduled_for_retry_without_changing_the_order()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        PayableOrder payable = PayableOrder.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "DineIn",
            "Pos",
            10m,
            DateTime.UtcNow
        );
        fixture.Db.PayableOrders.Add(payable);
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        OrderLineCancelled cancellation = CancelOrderLine(payable.OrderId, -1m);

        await fixture.DeliverAsync(
            Guid.NewGuid(),
            typeof(OrderLineCancelled).FullName!,
            cancellation
        );

        Assert.Equal(10m, payable.Amount);
        Assert.Empty(fixture.Db.InboxMessages);
        Assert.Equal(1, fixture.ChannelProxy.RetryPublications);
        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(0, fixture.ChannelProxy.Rejections);
    }

    [Fact]
    public async Task Invalid_cancelled_amount_is_dead_lettered_after_retry_limit()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        PayableOrder payable = PayableOrder.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "DineIn",
            "Pos",
            10m,
            DateTime.UtcNow
        );
        fixture.Db.PayableOrders.Add(payable);
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await fixture.DeliverRawAsync(
            Guid.NewGuid().ToString(),
            typeof(OrderLineCancelled).FullName!,
            JsonSerializer.SerializeToUtf8Bytes(CancelOrderLine(payable.OrderId, -1m)),
            RabbitMqRetry.MaximumAttempts
        );

        Assert.Equal(10m, payable.Amount);
        Assert.Empty(fixture.Db.InboxMessages);
        Assert.Equal(0, fixture.ChannelProxy.RetryPublications);
        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
    }

    private static OrderLineCancelled CancelOrderLine(Guid orderId, decimal remainingAmount) =>
        new(
            orderId,
            Guid.NewGuid(),
            null,
            null,
            null,
            "Mesa",
            "Cliente",
            "DineIn",
            "Pos",
            "OnAccount",
            Guid.NewGuid(),
            "COCINA",
            "Producto cancelado",
            "Submitted",
            remainingAmount,
            2,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow,
            [],
            []
        );

    private static OrderLineSnapshot Line(decimal unitPrice, int quantity) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Producto",
            unitPrice,
            quantity,
            "",
            null,
            "",
            "CALIENTE",
            "Caliente"
        );

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly PayableOrderProjectionWorker worker;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            PaymentWriteDbContext db,
            PayableOrderProjectionWorker worker,
            IChannel channel,
            ChannelProxy channelProxy
        )
        {
            this.services = services;
            Db = db;
            this.worker = worker;
            this.channel = channel;
            ChannelProxy = channelProxy;
        }

        public PaymentWriteDbContext Db { get; }
        public ChannelProxy ChannelProxy { get; }
        public object? WorkerChannel =>
            typeof(PayableOrderProjectionWorker)
                .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(worker);

        public void ClearWorkerChannel() =>
            typeof(PayableOrderProjectionWorker)
                .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(worker, null);

        public static async Task<WorkerFixture> CreateAsync()
        {
            PaymentWriteDbContext db = new(
                new DbContextOptionsBuilder<PaymentWriteDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider();
            PayableOrderProjectionWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                TimeProvider.System,
                NullLogger<PayableOrderProjectionWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            ChannelProxy proxy = (ChannelProxy)(object)channel;
            typeof(PayableOrderProjectionWorker)
                .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(worker, channel);
            return new WorkerFixture(services, db, worker, channel, proxy);
        }

        public Task DeliverAsync<TEvent>(Guid messageId, string eventType, TEvent message) =>
            DeliverRawAsync(
                messageId.ToString(),
                eventType,
                JsonSerializer.SerializeToUtf8Bytes(message)
            );

        public async Task DisposeWorkerResourcesAsync()
        {
            MethodInfo dispose = typeof(PayableOrderProjectionWorker).GetMethod(
                "DisposeResourcesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)dispose.Invoke(worker, null)!;
        }

        public async Task DeliverRawAsync(
            string messageId,
            string eventType,
            byte[] body,
            int retryCount = 0
        )
        {
            BasicProperties properties = new()
            {
                MessageId = messageId,
                Type = eventType,
                Headers =
                    retryCount == 0
                        ? null
                        : new Dictionary<string, object?>
                        {
                            ["x-restaurantes-retry-count"] = retryCount,
                        },
            };
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
            MethodInfo handle = typeof(PayableOrderProjectionWorker).GetMethod(
                "Handle",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)handle.Invoke(worker, [channel, args])!;
        }

        public async ValueTask DisposeAsync() => await services.DisposeAsync();
    }

    public class ChannelProxy : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }
        public int RetryPublications { get; private set; }
        public int Disposals { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IChannel.BasicAckAsync))
            {
                Acknowledgements++;
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.BasicNackAsync))
            {
                Rejections++;
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.BasicPublishAsync))
            {
                RetryPublications++;
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.DisposeAsync))
            {
                Disposals++;
                return ValueTask.CompletedTask;
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
