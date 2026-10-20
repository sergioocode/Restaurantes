using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Infrastructure.Persistence;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.Orders.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrderOutboxPublisherWorkerCoverageTests
{
    [Fact]
    public async Task Supported_order_events_publish_to_their_routing_key_and_mark_outbox()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        (string EventType, string RoutingKey)[] routes =
        [
            (typeof(OrderCreated).FullName!, OrdersTopology.OrderCreatedRoutingKey),
            (typeof(OrderSubmitted).FullName!, OrdersTopology.OrderSubmittedRoutingKey),
            (
                typeof(OrderPreparationStarted).FullName!,
                OrdersTopology.OrderPreparationStartedRoutingKey
            ),
            (
                typeof(KitchenTicketPreparationStarted).FullName!,
                OrdersTopology.OrderPreparationStartedRoutingKey
            ),
            (typeof(OrderReady).FullName!, OrdersTopology.OrderReadyRoutingKey),
            (typeof(KitchenTicketReady).FullName!, OrdersTopology.KitchenTicketUpdatedRoutingKey),
            (
                typeof(KitchenTicketDispatched).FullName!,
                OrdersTopology.KitchenTicketUpdatedRoutingKey
            ),
            (typeof(OrderDelivered).FullName!, OrdersTopology.OrderDeliveredRoutingKey),
            (typeof(OrderLineCancelled).FullName!, OrdersTopology.OrderLineCancelledRoutingKey),
            (typeof(OrderCancelled).FullName!, OrdersTopology.OrderCancelledRoutingKey),
        ];
        for (int index = 0; index < routes.Length; index++)
        {
            fixture.Db.OutboxMessages.Add(
                new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = routes[index].EventType,
                    Payload = "{\"sequence\":" + index + "}",
                    OccurredAtUtc = DateTime.UtcNow.AddSeconds(index),
                }
            );
        }
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(await fixture.PublishBatchAsync());

        Assert.Equal(routes.Length, fixture.ChannelProxy.Publications.Count);
        Assert.Equal(
            routes.Select(route => route.RoutingKey),
            fixture.ChannelProxy.Publications.Select(publication => publication.RoutingKey)
        );
        Assert.All(
            fixture.Db.OutboxMessages,
            message =>
            {
                Assert.NotNull(message.ProcessedAtUtc);
                Assert.Null(message.Error);
            }
        );
        Assert.All(
            fixture.ChannelProxy.Publications,
            publication =>
            {
                Assert.Equal(OrdersTopology.ExchangeName, publication.Exchange);
                Assert.NotEmpty(publication.MessageId);
                Assert.Equal("application/json", publication.ContentType);
                Assert.Contains("sequence", Encoding.UTF8.GetString(publication.Body));
            }
        );
    }

    [Fact]
    public async Task Empty_batch_returns_false_and_does_not_publish()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        Assert.False(await fixture.PublishBatchAsync());
        Assert.Empty(fixture.ChannelProxy.Publications);
    }

    [Fact]
    public async Task Unsupported_event_is_left_unprocessed()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        OutboxMessage unsupported = new()
        {
            Id = Guid.NewGuid(),
            Type = "unknown.event",
            Payload = "{}",
            OccurredAtUtc = DateTime.UtcNow,
        };
        fixture.Db.OutboxMessages.Add(unsupported);
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.PublishBatchAsync());

        Assert.Null(unsupported.ProcessedAtUtc);
        Assert.Empty(fixture.ChannelProxy.Publications);
    }

    [Fact]
    public async Task Publish_failure_is_saved_to_outbox_and_rethrown()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        OutboxMessage pending = new()
        {
            Id = Guid.NewGuid(),
            Type = typeof(OrderCreated).FullName!,
            Payload = "{}",
            OccurredAtUtc = DateTime.UtcNow,
        };
        fixture.Db.OutboxMessages.Add(pending);
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        fixture.ChannelProxy.PublishException = new InvalidOperationException("broker unavailable");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                fixture.PublishBatchAsync()
        );

        Assert.Equal("broker unavailable", exception.Message);
        Assert.Equal("broker unavailable", pending.Error);
        Assert.Null(pending.ProcessedAtUtc);
    }

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly OrderOutboxPublisherWorker worker;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            OrderWriteDbContext db,
            OrderOutboxPublisherWorker worker,
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

        public OrderWriteDbContext Db { get; }
        public ChannelProxy ChannelProxy { get; }

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
            OrderOutboxPublisherWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                TimeProvider.System,
                NullLogger<OrderOutboxPublisherWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            ChannelProxy proxy = (ChannelProxy)(object)channel;
            return new WorkerFixture(services, db, worker, channel, proxy);
        }

        public async Task<bool> PublishBatchAsync()
        {
            MethodInfo publish = typeof(OrderOutboxPublisherWorker).GetMethod(
                "PublishBatchAsync",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            return await (Task<bool>)
                publish.Invoke(worker, [channel, TestContext.Current.CancellationToken])!;
        }

        public async ValueTask DisposeAsync() => await services.DisposeAsync();
    }

    public class ChannelProxy : DispatchProxy
    {
        public List<PublishedMessage> Publications { get; } = [];
        public Exception? PublishException { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IChannel.BasicPublishAsync))
            {
                if (PublishException is not null)
                {
                    // DispatchProxy must return the boxed ValueTask for the caller to await.
#pragma warning disable CA2012
                    return ValueTask.FromException(PublishException);
#pragma warning restore CA2012
                }
                BasicProperties properties = (BasicProperties)args![3]!;
                Publications.Add(
                    new PublishedMessage(
                        (string)args[0]!,
                        (string)args[1]!,
                        properties.MessageId!,
                        properties.ContentType!,
                        ((ReadOnlyMemory<byte>)args[4]!).ToArray()
                    )
                );
                return ValueTask.CompletedTask;
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public sealed record PublishedMessage(
        string Exchange,
        string RoutingKey,
        string MessageId,
        string ContentType,
        byte[] Body
    );
}
