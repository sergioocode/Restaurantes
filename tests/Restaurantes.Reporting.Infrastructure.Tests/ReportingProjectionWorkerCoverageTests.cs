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
using Restaurantes.Reporting.Infrastructure.Persistence.Read;
using Restaurantes.Reporting.Infrastructure.Workers;
using Restaurantes.Sales.Contracts.Events;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class ReportingProjectionWorkerCoverageTests
{
    [Fact]
    public async Task Valid_order_event_is_projected_and_duplicate_is_acknowledged()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid orderId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        DateTime occurredAt = new(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);
        OrderSubmitted submitted = new(
            orderId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            2,
            Guid.NewGuid(),
            "Mesa 4",
            "Luz",
            "DineIn",
            "Commander",
            "OnAccount",
            3,
            occurredAt.AddMinutes(-5),
            occurredAt,
            [
                new OrderLineSnapshot(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Pasta",
                    12m,
                    2,
                    "",
                    null,
                    "",
                    "CALIENTE",
                    "Caliente"
                ),
            ],
            [
                new OrderKitchenStationSnapshot(
                    "CALIENTE",
                    "Caliente",
                    "Pending",
                    false,
                    1,
                    null,
                    null,
                    null
                ),
            ]
        );

        await fixture.DeliverAsync(messageId, typeof(OrderSubmitted).FullName!, submitted);

        OrderReportingFact projection = await fixture.Db.Orders.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(orderId, projection.OrderId);
        Assert.Equal("Commander", projection.Source);
        Assert.Equal("Submitted", projection.OrderStatus);
        Assert.Equal(24m, projection.Total);
        Assert.Equal(3, projection.Version);
        Assert.Equal(occurredAt, projection.UpdatedAtUtc);
        Assert.Single(fixture.Db.InboxMessages);

        await fixture.DeliverAsync(
            messageId,
            typeof(OrderSubmitted).FullName!,
            submitted with
            {
                Version = 8,
                TableLabel = "No debe aplicarse",
            }
        );

        Assert.Equal(3, projection.Version);
        Assert.Equal("Mesa 4", projection.TableLabel);
        Assert.Single(fixture.Db.InboxMessages);
        Assert.Equal(2, fixture.ChannelProxy.Acknowledgements);
    }

    [Theory]
    [InlineData("invalid-id", "Restaurantes.Orders.Contracts.Events.OrderSubmitted", "{}")]
    [InlineData("{guid}", "unknown.event", "{}")]
    [InlineData("{guid}", "Restaurantes.Orders.Contracts.Events.OrderSubmitted", "null")]
    public async Task Invalid_or_unsupported_events_are_dead_lettered(
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

        Assert.Empty(fixture.Db.Orders);
        Assert.Empty(fixture.Db.InboxMessages);
        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
    }

    [Fact]
    public async Task Sale_arriving_before_order_projection_is_scheduled_for_retry()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        SaleCompleted sale = SaleWithUnprojectedOrder();

        await fixture.DeliverAsync(Guid.NewGuid(), typeof(SaleCompleted).FullName!, sale);

        Assert.Empty(fixture.Db.Sales);
        Assert.Empty(fixture.Db.InboxMessages);
        Assert.Equal(1, fixture.ChannelProxy.RetryPublications);
        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(0, fixture.ChannelProxy.Rejections);
    }

    [Fact]
    public async Task Sale_arriving_before_order_projection_is_dead_lettered_after_retry_limit()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        await fixture.DeliverRawAsync(
            Guid.NewGuid().ToString(),
            typeof(SaleCompleted).FullName!,
            JsonSerializer.SerializeToUtf8Bytes(SaleWithUnprojectedOrder()),
            RabbitMqRetry.MaximumAttempts
        );

        Assert.Empty(fixture.Db.Sales);
        Assert.Empty(fixture.Db.InboxMessages);
        Assert.Equal(0, fixture.ChannelProxy.RetryPublications);
        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
    }

    [Fact]
    public async Task Dispose_releases_and_clears_rabbitmq_resources()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        await fixture.DisposeWorkerResourcesAsync();

        Assert.Equal(1, fixture.ChannelProxy.Disposals);
        Assert.Equal(1, fixture.ConnectionProxy.Disposals);
        Assert.Null(
            typeof(ReportingProjectionWorker)
                .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(fixture.Worker)
        );
        Assert.Null(
            typeof(ReportingProjectionWorker)
                .GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(fixture.Worker)
        );
    }

    private static SaleCompleted SaleWithUnprojectedOrder() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Pos",
            "Cash",
            15m,
            DateTime.UtcNow,
            [Guid.NewGuid()],
            []
        );

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            ReportingReadDbContext db,
            ReportingProjectionWorker worker,
            IChannel channel,
            ChannelProxy channelProxy,
            ConnectionProxy connectionProxy
        )
        {
            this.services = services;
            Db = db;
            Worker = worker;
            this.channel = channel;
            ChannelProxy = channelProxy;
            ConnectionProxy = connectionProxy;
        }

        public ReportingReadDbContext Db { get; }
        public ReportingProjectionWorker Worker { get; }
        public ChannelProxy ChannelProxy { get; }
        public ConnectionProxy ConnectionProxy { get; }

        public static async Task<WorkerFixture> CreateAsync()
        {
            ReportingReadDbContext db = new(
                new DbContextOptionsBuilder<ReportingReadDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(db)
                .AddSingleton(TimeProvider.System)
                .AddScoped<ReportingProjectionHandler>()
                .BuildServiceProvider();
            ReportingProjectionWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                NullLogger<ReportingProjectionWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            ChannelProxy proxy = (ChannelProxy)(object)channel;
            IConnection connection = DispatchProxy.Create<IConnection, ConnectionProxy>();
            ConnectionProxy connectionProxy = (ConnectionProxy)(object)connection;
            typeof(ReportingProjectionWorker)
                .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(worker, channel);
            typeof(ReportingProjectionWorker)
                .GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(worker, connection);
            return new WorkerFixture(services, db, worker, channel, proxy, connectionProxy);
        }

        public Task DeliverAsync<TEvent>(Guid messageId, string eventType, TEvent message) =>
            DeliverRawAsync(
                messageId.ToString(),
                eventType,
                JsonSerializer.SerializeToUtf8Bytes(message)
            );

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
            MethodInfo handle = typeof(ReportingProjectionWorker).GetMethod(
                "HandleMessageAsync",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)handle.Invoke(Worker, [channel, args])!;
        }

        public async Task DisposeWorkerResourcesAsync()
        {
            MethodInfo dispose = typeof(ReportingProjectionWorker).GetMethod(
                "DisposeResourcesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)dispose.Invoke(Worker, null)!;
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

    public class ConnectionProxy : DispatchProxy
    {
        public int Disposals { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IConnection.DisposeAsync))
            {
                Disposals++;
                return ValueTask.CompletedTask;
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
