using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Sales.Infrastructure.Persistence;
using Restaurantes.Sales.Infrastructure.Persistence.Write;
using Restaurantes.Sales.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.Sales.Application.Tests;

public sealed class SalesOutboxPublisherWorkerCoverageTests
{
    [Fact]
    public async Task Sale_completed_event_is_published_and_marked_processed()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        OutboxMessage message = new()
        {
            Id = Guid.NewGuid(),
            Type = typeof(SaleCompleted).FullName!,
            Payload = "{\"saleId\":\"sale-1\"}",
            OccurredAtUtc = DateTime.UnixEpoch,
        };
        fixture.Db.OutboxMessages.Add(message);
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(await fixture.PublishAsync());

        Assert.NotNull(message.ProcessedAtUtc);
        PublishedMessage publication = Assert.Single(fixture.Channel.Publications);
        Assert.Equal(SalesTopology.ExchangeName, publication.Exchange);
        Assert.Equal(SalesTopology.SaleCompletedRoutingKey, publication.RoutingKey);
        Assert.Equal(message.Id.ToString(), publication.MessageId);
        Assert.Equal("application/json", publication.ContentType);
        Assert.Equal(message.Payload, Encoding.UTF8.GetString(publication.Body));
    }

    [Fact]
    public async Task Empty_batch_returns_false_without_publishing()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        Assert.False(await fixture.PublishAsync());
        Assert.Empty(fixture.Channel.Publications);
    }

    [Fact]
    public async Task Unsupported_event_remains_pending()
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
    public async Task Broker_failure_does_not_mark_event_processed()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        OutboxMessage message = new()
        {
            Id = Guid.NewGuid(),
            Type = typeof(SaleCompleted).FullName!,
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
        private readonly SalesOutboxPublisherWorker worker;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            SaleWriteDbContext db,
            SalesOutboxPublisherWorker worker,
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

        public SaleWriteDbContext Db { get; }
        public ChannelProxy Channel { get; }

        public static async Task<WorkerFixture> CreateAsync()
        {
            SaleWriteDbContext db = new(
                new DbContextOptionsBuilder<SaleWriteDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider();
            SalesOutboxPublisherWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                TimeProvider.System,
                NullLogger<SalesOutboxPublisherWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            return new WorkerFixture(services, db, worker, channel, (ChannelProxy)(object)channel);
        }

        public async Task<bool> PublishAsync()
        {
            MethodInfo publish = typeof(SalesOutboxPublisherWorker).GetMethod(
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
