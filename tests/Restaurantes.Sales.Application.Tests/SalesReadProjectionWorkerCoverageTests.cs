using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Sales.Infrastructure.Persistence.Read;
using Restaurantes.Sales.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.Sales.Application.Tests;

public sealed class SalesReadProjectionWorkerCoverageTests
{
    [Fact]
    public async Task Sale_event_projects_and_updates_read_model_once_per_message()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid saleId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid firstMessageId = Guid.NewGuid();
        SaleCompleted first = Sale(saleId, restaurantId, orderId, "Cash", 25m) with
        {
            TipTotal = 2m,
            DiscountName = "Comida de equipo",
            DiscountPercentage = 10m,
            DiscountAmount = 2.5m,
            PaymentMethods = [new SalePaymentBreakdown("Cash", 1, 25m)],
        };

        await fixture.DeliverAsync(firstMessageId, typeof(SaleCompleted).FullName!, first);

        SaleReadModel projection = await fixture.Db.Sales.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(restaurantId, projection.RestaurantId);
        Assert.Equal("Pos", projection.Source);
        Assert.Equal("Cash", projection.PaymentMethod);
        Assert.Equal(25m, projection.Total);
        Assert.Equal(2m, projection.TipTotal);
        Assert.Equal("Comida de equipo", projection.DiscountName);
        Assert.Equal(10m, projection.DiscountPercentage);
        Assert.Equal(2.5m, projection.DiscountAmount);
        Assert.Equal(1, projection.OrderCount);
        Assert.Equal(first.CompletedAtUtc, projection.CompletedAtUtc);
        Assert.Equal(first.BusinessDate, projection.BusinessDate);
        Assert.Equal("[\"" + orderId + "\"]", projection.OrderIdsJson);
        Assert.Single(fixture.Db.InboxMessages);
        SalePaymentBreakdown payment = Assert.Single(
            JsonSerializer.Deserialize<SalePaymentBreakdown[]>(projection.PaymentMethodsJson)!
        );
        Assert.Equal("Cash", payment.Method);

        SaleCompleted duplicate = first with { Total = 999m };
        await fixture.DeliverAsync(firstMessageId, typeof(SaleCompleted).FullName!, duplicate);
        Assert.Equal(25m, projection.Total);
        Assert.Single(fixture.Db.InboxMessages);

        SaleCompleted later = first with
        {
            Total = 23m,
            PaymentMethod = "Card",
            PaymentMethods = [new SalePaymentBreakdown("Card", 1, 23m)],
        };
        await fixture.DeliverAsync(Guid.NewGuid(), typeof(SaleCompleted).FullName!, later);
        Assert.Equal(23m, projection.Total);
        Assert.Equal("Card", projection.PaymentMethod);
        Assert.Equal(
            2,
            await fixture.Db.InboxMessages.CountAsync(TestContext.Current.CancellationToken)
        );
        Assert.Equal(3, fixture.ChannelProxy.Acknowledgements);
    }

    [Theory]
    [InlineData("not-a-guid", "Restaurantes.Sales.Contracts.Events.SaleCompleted", "{}")]
    [InlineData("{guid}", "unknown.event", "{}")]
    [InlineData("{guid}", "Restaurantes.Sales.Contracts.Events.SaleCompleted", "null")]
    public async Task Invalid_sales_messages_are_dead_lettered(
        string messageId,
        string eventType,
        string body
    )
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        if (messageId == "{guid}")
        {
            messageId = Guid.NewGuid().ToString();
        }

        await fixture.DeliverRawAsync(
            messageId,
            eventType,
            System.Text.Encoding.UTF8.GetBytes(body)
        );

        Assert.Empty(fixture.Db.Sales);
        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
    }

    [Fact]
    public async Task Delivery_acknowledgement_failure_publishes_a_retry_copy()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        fixture.ChannelProxy.ThrowOnNextAcknowledgement = true;

        await fixture.DeliverAsync(
            Guid.NewGuid(),
            typeof(SaleCompleted).FullName!,
            Sale(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Cash", 12m)
        );

        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(0, fixture.ChannelProxy.Rejections);
        PublishedMessage retry = Assert.Single(fixture.ChannelProxy.PublishedMessages);
        Assert.Equal($"{SalesTopology.ReadModelQueueName}.retry.1", retry.RoutingKey);
        Assert.Equal(1, retry.Properties.Headers!["x-restaurantes-retry-count"]);
        Assert.Single(fixture.Db.Sales);
    }

    [Fact]
    public async Task Delivery_acknowledgement_failure_at_retry_limit_is_dead_lettered()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        fixture.ChannelProxy.ThrowOnNextAcknowledgement = true;

        await fixture.DeliverRawAsync(
            Guid.NewGuid().ToString(),
            typeof(SaleCompleted).FullName!,
            JsonSerializer.SerializeToUtf8Bytes(
                Sale(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Cash", 12m)
            ),
            new Dictionary<string, object?> { ["x-restaurantes-retry-count"] = 3 }
        );

        Assert.Empty(fixture.ChannelProxy.PublishedMessages);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Single(fixture.Db.Sales);
    }

    [Fact]
    public async Task Dispose_releases_and_clears_rabbitmq_resources()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        await fixture.DisposeWorkerResourcesAsync();

        Assert.Equal(1, fixture.ChannelProxy.Disposals);
        Assert.Equal(1, fixture.ConnectionProxy.Disposals);
        Assert.Null(
            typeof(SalesReadProjectionWorker)
                .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(fixture.Worker)
        );
        Assert.Null(
            typeof(SalesReadProjectionWorker)
                .GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(fixture.Worker)
        );
    }

    private static SaleCompleted Sale(
        Guid saleId,
        Guid restaurantId,
        Guid orderId,
        string method,
        decimal total
    ) =>
        new(
            saleId,
            restaurantId,
            "Pos",
            method,
            total,
            new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
            [orderId],
            [new SaleLineSnapshot(Guid.NewGuid(), "Pizza", total, 1, "Comida", "CALIENTE")],
            new DateOnly(2026, 10, 20)
        );

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            SaleReadDbContext db,
            SalesReadProjectionWorker worker,
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

        public SaleReadDbContext Db { get; }
        public SalesReadProjectionWorker Worker { get; }
        public ChannelProxy ChannelProxy { get; }
        public ConnectionProxy ConnectionProxy { get; }

        public static async Task<WorkerFixture> CreateAsync()
        {
            SaleReadDbContext db = new(
                new DbContextOptionsBuilder<SaleReadDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider();
            SalesReadProjectionWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                TimeProvider.System,
                NullLogger<SalesReadProjectionWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            ChannelProxy proxy = (ChannelProxy)(object)channel;
            IConnection connection = DispatchProxy.Create<IConnection, ConnectionProxy>();
            ConnectionProxy connectionProxy = (ConnectionProxy)(object)connection;
            typeof(SalesReadProjectionWorker)
                .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(worker, channel);
            typeof(SalesReadProjectionWorker)
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
            IDictionary<string, object?>? headers = null
        )
        {
            BasicProperties properties = new()
            {
                MessageId = messageId,
                Type = eventType,
                Headers = headers,
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
            MethodInfo handle = typeof(SalesReadProjectionWorker).GetMethod(
                "Handle",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)handle.Invoke(Worker, [channel, args])!;
        }

        public async Task DisposeWorkerResourcesAsync()
        {
            MethodInfo dispose = typeof(SalesReadProjectionWorker).GetMethod(
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
        public bool ThrowOnNextAcknowledgement { get; set; }
        public int Disposals { get; private set; }
        public List<PublishedMessage> PublishedMessages { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IChannel.BasicAckAsync))
            {
                if (ThrowOnNextAcknowledgement)
                {
                    ThrowOnNextAcknowledgement = false;
                    throw new InvalidOperationException("Broker acknowledgement failed.");
                }

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
                PublishedMessages.Add(
                    new PublishedMessage(
                        (string)args![1]!,
                        ((ReadOnlyMemory<byte>)args[4]!).ToArray(),
                        (BasicProperties)args[3]!
                    )
                );
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

    public sealed record PublishedMessage(
        string RoutingKey,
        byte[] Body,
        BasicProperties Properties
    );
}
