using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Infrastructure.Persistence;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Catalog.Infrastructure.Workers;
using Restaurantes.Messaging.RabbitMq;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogOutboxPublisherWorkerCoverageTests
{
    [Fact]
    public async Task Supported_catalog_events_publish_in_order_and_mark_each_outbox_message()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        (string Type, string RoutingKey)[] routes =
        [
            (typeof(CategoryChanged).FullName!, CatalogTopology.CategoryChangedRoutingKey),
            (typeof(ProductChanged).FullName!, CatalogTopology.ProductChangedRoutingKey),
            (typeof(CatalogItemChanged).FullName!, CatalogTopology.CatalogItemChangedRoutingKey),
            (
                typeof(KitchenStationChanged).FullName!,
                CatalogTopology.KitchenStationChangedRoutingKey
            ),
        ];
        for (int index = 0; index < routes.Length; index++)
        {
            fixture.Db.OutboxMessages.Add(
                new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = routes[index].Type,
                    Payload = "{\"sequence\":" + index + "}",
                    OccurredAtUtc = DateTime.UnixEpoch.AddSeconds(index),
                }
            );
        }
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(await fixture.PublishAsync());

        Assert.Equal(
            routes.Select(item => item.RoutingKey),
            fixture.Channel.Publications.Select(item => item.RoutingKey)
        );
        Assert.All(fixture.Db.OutboxMessages, message => Assert.NotNull(message.ProcessedAtUtc));
        Assert.All(
            fixture.Channel.Publications,
            publication =>
            {
                Assert.Equal(CatalogTopology.ExchangeName, publication.Exchange);
                Assert.NotEmpty(publication.MessageId);
                Assert.Equal("application/json", publication.ContentType);
                Assert.Contains("sequence", Encoding.UTF8.GetString(publication.Body));
            }
        );
    }

    [Fact]
    public async Task Empty_batch_returns_false_without_publishing()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        Assert.False(await fixture.PublishAsync());
        Assert.Empty(fixture.Channel.Publications);
    }

    [Fact]
    public async Task Unsupported_event_is_not_marked_processed_or_published()
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

        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.PublishAsync());

        Assert.Null(message.ProcessedAtUtc);
        Assert.Empty(fixture.Channel.Publications);
    }

    [Fact]
    public async Task Broker_failure_leaves_message_pending()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        OutboxMessage message = new()
        {
            Id = Guid.NewGuid(),
            Type = typeof(CategoryChanged).FullName!,
            Payload = "{}",
            OccurredAtUtc = DateTime.UtcNow,
        };
        fixture.Db.OutboxMessages.Add(message);
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        fixture.Channel.PublishException = new IOException("broker unavailable");

        IOException exception = await Assert.ThrowsAsync<IOException>(() => fixture.PublishAsync());

        Assert.Equal("broker unavailable", exception.Message);
        Assert.Null(message.ProcessedAtUtc);
    }

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly CatalogOutboxPublisherWorker worker;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            CatalogWriteDbContext db,
            CatalogOutboxPublisherWorker worker,
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

        public CatalogWriteDbContext Db { get; }
        public ChannelProxy Channel { get; }

        public static async Task<WorkerFixture> CreateAsync()
        {
            CatalogWriteDbContext db = new(
                new DbContextOptionsBuilder<CatalogWriteDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider();
            CatalogOutboxPublisherWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                TimeProvider.System,
                NullLogger<CatalogOutboxPublisherWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            return new WorkerFixture(services, db, worker, channel, (ChannelProxy)(object)channel);
        }

        public async Task<bool> PublishAsync()
        {
            MethodInfo publish = typeof(CatalogOutboxPublisherWorker).GetMethod(
                "Publish",
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
