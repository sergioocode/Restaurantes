using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.RestaurantOperations.Contracts.Events;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Write;
using Restaurantes.RestaurantOperations.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class RestaurantOperationsOutboxPublisherCoverageTests
{
    [Fact]
    public async Task Supported_events_publish_to_their_route_and_clear_previous_error()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        (string Type, string RoutingKey)[] routes =
        [
            (
                typeof(RestaurantChanged).FullName!,
                RestaurantOperationsTopology.RestaurantChangedRoutingKey
            ),
            (
                typeof(CompanyChanged).FullName!,
                RestaurantOperationsTopology.CompanyChangedRoutingKey
            ),
        ];
        foreach ((string type, _) in routes)
        {
            fixture.Db.OutboxMessages.Add(
                new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = type,
                    Payload = "{\"event\":\"" + type + "\"}",
                    OccurredAtUtc = DateTime.UtcNow,
                    Error = "previous failure",
                }
            );
        }
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(await fixture.PublishBatchAsync());

        Assert.Equal(
            routes.Select(item => item.RoutingKey),
            fixture.Channel.Publications.Select(item => item.RoutingKey)
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
            fixture.Channel.Publications,
            publication =>
            {
                Assert.Equal(RestaurantOperationsTopology.ExchangeName, publication.Exchange);
                Assert.NotEmpty(publication.MessageId);
                Assert.Equal("application/json", publication.ContentType);
                Assert.Equal(DeliveryModes.Persistent, publication.DeliveryMode);
                Assert.Contains(
                    "Restaurantes.RestaurantOperations.Contracts.Events.",
                    publication.EventType
                );
                Assert.Contains("event", Encoding.UTF8.GetString(publication.Body));
            }
        );
    }

    [Fact]
    public async Task Empty_batch_returns_false_without_publishing()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        Assert.False(await fixture.PublishBatchAsync());
        Assert.Empty(fixture.Channel.Publications);
    }

    [Fact]
    public async Task Batch_is_ordered_and_limited_to_fifty_pending_messages()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        DateTime firstOccurredAt = DateTime.UtcNow;
        for (int index = 51; index >= 0; index--)
        {
            fixture.Db.OutboxMessages.Add(
                new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = typeof(RestaurantChanged).FullName!,
                    Payload = "{\"index\":" + index + "}",
                    OccurredAtUtc = firstOccurredAt.AddSeconds(index),
                    ProcessedAtUtc = index == 51 ? firstOccurredAt : null,
                }
            );
        }
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(await fixture.PublishBatchAsync());
        Assert.Equal(50, fixture.Channel.Publications.Count);
        Assert.Equal(
            Enumerable.Range(0, 50),
            fixture.Channel.Publications.Select(publication =>
                JsonDocument.Parse(publication.Body).RootElement.GetProperty("index").GetInt32()
            )
        );
        Assert.Equal(
            1,
            fixture.Db.OutboxMessages.Count(message => !message.ProcessedAtUtc.HasValue)
        );

        Assert.True(await fixture.PublishBatchAsync());
        PublishedMessage remaining = Assert.Single(fixture.Channel.Publications.Skip(50));
        Assert.Equal(
            50,
            JsonDocument.Parse(remaining.Body).RootElement.GetProperty("index").GetInt32()
        );
        Assert.False(await fixture.PublishBatchAsync());
    }

    [Fact]
    public async Task Unsupported_event_is_kept_pending_and_error_is_recorded()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        OutboxMessage message = new()
        {
            Id = Guid.NewGuid(),
            Type = "unknown.event",
            Payload = "{}",
            OccurredAtUtc = DateTime.UtcNow,
        };
        fixture.Db.OutboxMessages.Add(message);
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            fixture.PublishBatchAsync()
        );

        Assert.Contains("unknown.event", exception.Message);
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(exception.Message, message.Error);
        Assert.Empty(fixture.Channel.Publications);
    }

    [Fact]
    public async Task Broker_failure_is_recorded_and_message_remains_pending()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        OutboxMessage message = new()
        {
            Id = Guid.NewGuid(),
            Type = typeof(RestaurantChanged).FullName!,
            Payload = "{}",
            OccurredAtUtc = DateTime.UtcNow,
        };
        fixture.Db.OutboxMessages.Add(message);
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        fixture.Channel.PublishException = new IOException("broker unavailable");

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            fixture.PublishBatchAsync()
        );

        Assert.Equal("broker unavailable", exception.Message);
        Assert.Equal(exception.Message, message.Error);
        Assert.Null(message.ProcessedAtUtc);
    }

    [Fact]
    public async Task Broker_failure_is_truncated_to_the_outbox_error_limit()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        OutboxMessage message = new()
        {
            Id = Guid.NewGuid(),
            Type = typeof(RestaurantChanged).FullName!,
            Payload = "{}",
            OccurredAtUtc = DateTime.UtcNow,
        };
        fixture.Db.OutboxMessages.Add(message);
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        fixture.Channel.PublishException = new IOException(new string('x', 2_500));

        await Assert.ThrowsAsync<IOException>(() => fixture.PublishBatchAsync());

        Assert.Equal(2_000, message.Error!.Length);
        Assert.Null(message.ProcessedAtUtc);
        Assert.Empty(fixture.Channel.Publications);
    }

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly OutboxPublisherWorker worker;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            RestaurantWriteDbContext db,
            OutboxPublisherWorker worker,
            IChannel channel,
            ChannelProxy proxy
        )
        {
            this.services = services;
            Db = db;
            this.worker = worker;
            this.channel = channel;
            Channel = proxy;
        }

        public RestaurantWriteDbContext Db { get; }
        public ChannelProxy Channel { get; }

        public static async Task<WorkerFixture> CreateAsync()
        {
            RestaurantWriteDbContext db = new(
                new DbContextOptionsBuilder<RestaurantWriteDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider();
            OutboxPublisherWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                TimeProvider.System,
                NullLogger<OutboxPublisherWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            return new WorkerFixture(services, db, worker, channel, (ChannelProxy)(object)channel);
        }

        public async Task<bool> PublishBatchAsync()
        {
            MethodInfo publish = typeof(OutboxPublisherWorker).GetMethod(
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
                        properties.Type!,
                        properties.DeliveryMode,
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
        string EventType,
        DeliveryModes DeliveryMode,
        byte[] Body
    );
}
