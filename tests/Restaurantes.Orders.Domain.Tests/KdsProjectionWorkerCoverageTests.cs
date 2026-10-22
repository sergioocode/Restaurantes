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
using Restaurantes.Orders.Infrastructure.Persistence.Read;
using Restaurantes.Orders.Infrastructure.Workers;
using Restaurantes.Orders.Infrastructure.Workers.Handlers;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class KdsProjectionWorkerCoverageTests
{
    [Fact]
    public async Task Submitted_order_is_projected_published_and_acknowledged()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        OrderSubmitted message = new(
            orderId,
            restaurantId,
            null,
            2,
            null,
            "Mesa 5",
            "Eva",
            "DineIn",
            "Pos",
            "Immediate",
            1,
            DateTime.UtcNow,
            DateTime.UtcNow,
            [
                new(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Pizza",
                    12m,
                    1,
                    "",
                    null,
                    "",
                    "CALIENTE",
                    "Caliente"
                ),
            ],
            [new("CALIENTE", "Caliente", "Pending", false, 1, null, null, null)]
        );

        await fixture.DeliverAsync(
            Guid.NewGuid().ToString(),
            typeof(OrderSubmitted).FullName!,
            message
        );

        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(0, fixture.ChannelProxy.Rejections);
        PublishedMessage published = Assert.Single(fixture.NotificationProxy.PublishedMessages);
        Assert.Equal(OrdersTopology.ExchangeName, published.Exchange);
        Assert.Equal(OrdersTopology.KdsOrderUpdatedRoutingKey, published.RoutingKey);
        KdsOrderUpdated notification = Assert.IsType<KdsOrderUpdated>(
            JsonSerializer.Deserialize<KdsOrderUpdated>(published.Body)
        );
        Assert.Equal(orderId, notification.OrderId);
        Assert.Equal(restaurantId, notification.RestaurantId);
        Assert.Equal("Submitted", notification.Status);
        Assert.Equal(1, notification.Version);
        Assert.Equal(["CALIENTE"], notification.StationCodes);
    }

    [Fact]
    public async Task Kitchen_and_order_lifecycle_events_are_projected_with_their_statuses()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        DateTime createdAt = DateTime.UtcNow.AddMinutes(-5);
        DateTime submittedAt = createdAt.AddMinutes(1);
        DateTime preparationStartedAt = submittedAt.AddMinutes(1);
        DateTime readyAt = preparationStartedAt.AddMinutes(1);
        DateTime deliveredAt = readyAt.AddMinutes(1);
        Guid restaurantId = Guid.NewGuid();
        Guid zoneId = Guid.NewGuid();

        object[] events =
        [
            new OrderCreated(
                Guid.NewGuid(),
                restaurantId,
                null,
                2,
                null,
                "Mesa 5",
                "Eva",
                "DineIn",
                "Pos",
                "Immediate",
                1,
                createdAt,
                [Line("Active", 12m)],
                [Station("Pending")],
                ZoneId: zoneId
            ),
            new KitchenTicketPreparationStarted(
                Guid.NewGuid(),
                restaurantId,
                null,
                2,
                null,
                "Mesa 5",
                "Eva",
                "DineIn",
                "Pos",
                "Immediate",
                "CALIENTE",
                2,
                createdAt,
                submittedAt,
                preparationStartedAt,
                [Line("Active", 12m)],
                [Station("InPreparation")]
            ),
            new KitchenTicketReady(
                Guid.NewGuid(),
                restaurantId,
                null,
                2,
                null,
                "Mesa 5",
                "Eva",
                "DineIn",
                "Pos",
                "Immediate",
                "CALIENTE",
                3,
                createdAt,
                submittedAt,
                preparationStartedAt,
                readyAt,
                [Line("Active", 12m)],
                [Station("Ready")]
            ),
            new KitchenTicketReady(
                Guid.NewGuid(),
                restaurantId,
                null,
                2,
                null,
                "Mesa 5",
                "Eva",
                "DineIn",
                "Pos",
                "Immediate",
                "CALIENTE",
                3,
                createdAt,
                submittedAt,
                preparationStartedAt,
                null,
                [Line("Active", 12m)],
                [Station("InPreparation")]
            ),
            new OrderReady(
                Guid.NewGuid(),
                restaurantId,
                null,
                2,
                null,
                "Mesa 5",
                "Eva",
                "DineIn",
                "Pos",
                "Immediate",
                4,
                createdAt,
                submittedAt,
                preparationStartedAt,
                readyAt,
                [Line("Active", 12m)],
                [Station("Ready")]
            ),
            new OrderDelivered(
                Guid.NewGuid(),
                restaurantId,
                null,
                2,
                null,
                "Mesa 5",
                "Eva",
                "DineIn",
                "Pos",
                "Immediate",
                5,
                createdAt,
                submittedAt,
                preparationStartedAt,
                readyAt,
                deliveredAt,
                [Line("Active", 12m)],
                [Station("Dispatched")]
            ),
            new OrderCancelled(
                Guid.NewGuid(),
                restaurantId,
                null,
                2,
                null,
                "Mesa 5",
                "Eva",
                "DineIn",
                "Pos",
                "Immediate",
                "Cancelación solicitada",
                "Sergio",
                6,
                createdAt,
                submittedAt,
                deliveredAt,
                [Line("Cancelled", 12m)],
                [Station("Cancelled")]
            ),
            new OrderLineCancelled(
                Guid.NewGuid(),
                restaurantId,
                null,
                2,
                null,
                "Mesa 5",
                "Eva",
                "DineIn",
                "Pos",
                "Immediate",
                Guid.NewGuid(),
                "CALIENTE",
                "Producto no disponible",
                "Ready",
                0m,
                7,
                createdAt,
                submittedAt,
                deliveredAt,
                [Line("Cancelled", 12m)],
                [Station("Ready")]
            ),
        ];

        foreach (object message in events)
        {
            await fixture.DeliverObjectAsync(Guid.NewGuid(), message);
        }

        Assert.Equal(events.Length, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(0, fixture.ChannelProxy.Rejections);
        Assert.Equal(events.Length - 1, fixture.NotificationProxy.PublishedMessages.Count);
        Assert.Equal(events.Length, await fixture.GetProjectedOrderCountAsync());
        Assert.Contains(
            await fixture.GetProjectedZonesAsync(),
            projectedZone => projectedZone == zoneId
        );
    }

    [Fact]
    public async Task Dispatched_ticket_is_delivered_only_when_all_non_cancelled_stations_are_dispatched()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        DateTime createdAt = DateTime.UtcNow.AddMinutes(-5);
        DateTime submittedAt = createdAt.AddMinutes(1);
        DateTime preparationStartedAt = submittedAt.AddMinutes(1);
        DateTime readyAt = preparationStartedAt.AddMinutes(1);
        DateTime dispatchedAt = readyAt.AddMinutes(1);
        OrderKitchenStationSnapshot[] stations =
        [
            Station("Dispatched", "CALIENTE"),
            Station("Dispatched", "BEBIDAS"),
            Station("Cancelled", "POSTRE"),
        ];
        KitchenTicketDispatched[] events =
        [
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                2,
                null,
                "Mesa 5",
                "Eva",
                "DineIn",
                "Pos",
                "Immediate",
                "CALIENTE",
                8,
                createdAt,
                submittedAt,
                preparationStartedAt,
                dispatchedAt,
                [Line("Active", 12m)],
                [Station("Dispatched", "CALIENTE"), Station("Ready", "BEBIDAS")]
            ),
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                2,
                null,
                "Mesa 5",
                "Eva",
                "DineIn",
                "Pos",
                "Immediate",
                "CALIENTE",
                9,
                createdAt,
                submittedAt,
                preparationStartedAt,
                dispatchedAt,
                [Line("Active", 12m)],
                stations
            ),
        ];

        await fixture.DeliverObjectAsync(Guid.NewGuid(), events[0]);
        await fixture.DeliverObjectAsync(Guid.NewGuid(), events[1]);

        KdsOrderUpdated[] notifications = fixture
            .NotificationProxy.PublishedMessages.Select(message =>
                JsonSerializer.Deserialize<KdsOrderUpdated>(message.Body)!
            )
            .ToArray();
        Assert.Equal(["Ready", "Delivered"], notifications.Select(item => item.Status));
        Assert.Equal(dispatchedAt, (await fixture.GetOrderAsync(events[1].OrderId)).DeliveredAtUtc);
    }

    private static OrderLineSnapshot Line(string status, decimal unitPrice) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Pizza",
            unitPrice,
            1,
            "",
            null,
            "",
            "CALIENTE",
            "Caliente",
            status
        );

    private static OrderKitchenStationSnapshot Station(string status, string code = "CALIENTE") =>
        new(
            code,
            code,
            status,
            false,
            1,
            DateTime.UtcNow.AddMinutes(-1),
            status is "Ready" or "Dispatched" ? DateTime.UtcNow : null,
            status == "Dispatched" ? DateTime.UtcNow : null
        );

    [Theory]
    [InlineData("invalid-message-id", "{}")]
    [InlineData("{valid-guid}", "{}")]
    public async Task Invalid_or_unknown_messages_are_dead_lettered(string messageId, string body)
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        if (messageId == "{valid-guid}")
        {
            messageId = Guid.NewGuid().ToString();
        }

        await fixture.DeliverRawAsync(
            messageId,
            "unknown.event",
            System.Text.Encoding.UTF8.GetBytes(body)
        );

        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
    }

    [Fact]
    public async Task Handler_with_missing_channel_returns_without_processing()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(setChannels: false);

        await fixture.DeliverAsync(
            Guid.NewGuid().ToString(),
            typeof(OrderSubmitted).FullName!,
            new OrderSubmitted(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                1,
                null,
                "Mesa",
                "",
                "DineIn",
                "Pos",
                "Immediate",
                1,
                DateTime.UtcNow,
                DateTime.UtcNow,
                [],
                []
            )
        );

        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Empty(fixture.NotificationProxy.PublishedMessages);
    }

    [Fact]
    public async Task Notification_failure_schedules_retry_and_acknowledges_source_delivery()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(
            failNotificationPublish: true
        );

        await fixture.DeliverAsync(
            Guid.NewGuid().ToString(),
            typeof(OrderSubmitted).FullName!,
            SubmittedOrder()
        );

        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(0, fixture.ChannelProxy.Rejections);
        PublishedMessage retry = Assert.Single(fixture.ChannelProxy.PublishedMessages);
        Assert.Equal($"{OrdersTopology.KdsQueueName}.retry.1", retry.RoutingKey);
        Assert.Equal(1, retry.Properties!.Headers!["x-restaurantes-retry-count"]);
    }

    [Fact]
    public async Task Delivery_at_retry_limit_is_rejected_without_requeue()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(
            failNotificationPublish: true
        );
        BasicProperties properties = new()
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = typeof(OrderSubmitted).FullName,
            Headers = new Dictionary<string, object?>
            {
                ["x-restaurantes-retry-count"] = RabbitMqRetry.MaximumAttempts,
            },
        };

        await fixture.DeliverRawAsync(
            properties,
            JsonSerializer.SerializeToUtf8Bytes(SubmittedOrder())
        );

        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
        Assert.Empty(fixture.ChannelProxy.PublishedMessages);
    }

    [Fact]
    public async Task Missing_notification_channel_retries_the_projected_message()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(
            setNotificationChannel: false
        );

        await fixture.DeliverAsync(
            Guid.NewGuid().ToString(),
            typeof(OrderSubmitted).FullName!,
            SubmittedOrder()
        );

        PublishedMessage retry = Assert.Single(fixture.ChannelProxy.PublishedMessages);
        Assert.Equal($"{OrdersTopology.KdsQueueName}.retry.1", retry.RoutingKey);
        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
    }

    [Fact]
    public async Task Disposing_worker_releases_and_clears_both_channels()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        await fixture.DisposeWorkerResourcesAsync();

        Assert.Equal(1, fixture.ChannelProxy.Disposals);
        Assert.Equal(1, fixture.NotificationProxy.Disposals);
        Assert.Equal(1, fixture.ConnectionProxy.Disposals);
        Assert.Null(GetWorkerField(fixture, "_channel"));
        Assert.Null(GetWorkerField(fixture, "_notificationChannel"));
        Assert.Null(GetWorkerField(fixture, "_connection"));
    }

    private static OrderSubmitted SubmittedOrder() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            1,
            null,
            "Mesa",
            "",
            "DineIn",
            "Pos",
            "Immediate",
            1,
            DateTime.UtcNow,
            DateTime.UtcNow,
            [],
            []
        );

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly KdsProjectionWorker worker;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            KdsProjectionWorker worker,
            IChannel channel,
            ChannelProxy channelProxy,
            IChannel notificationChannel,
            NotificationChannelProxy notificationProxy,
            ConnectionProxy connectionProxy
        )
        {
            this.services = services;
            this.worker = worker;
            this.channel = channel;
            ChannelProxy = channelProxy;
            NotificationChannel = notificationChannel;
            NotificationProxy = notificationProxy;
            ConnectionProxy = connectionProxy;
        }

        public ChannelProxy ChannelProxy { get; }
        public IChannel NotificationChannel { get; }
        public NotificationChannelProxy NotificationProxy { get; }
        public ConnectionProxy ConnectionProxy { get; }

        public static async Task<WorkerFixture> CreateAsync(
            bool setChannels = true,
            bool failNotificationPublish = false,
            bool setNotificationChannel = true
        )
        {
            OrderReadDbContext db = new(
                new DbContextOptionsBuilder<OrderReadDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(db)
                .AddSingleton(TimeProvider.System)
                .AddScoped<KdsProjectionHandler>()
                .BuildServiceProvider();
            KdsProjectionWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                TimeProvider.System,
                NullLogger<KdsProjectionWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            ChannelProxy channelProxy = (ChannelProxy)(object)channel;
            IChannel notificationChannel = DispatchProxy.Create<
                IChannel,
                NotificationChannelProxy
            >();
            NotificationChannelProxy notificationProxy = (NotificationChannelProxy)
                (object)notificationChannel;
            notificationProxy.FailPublish = failNotificationPublish;
            IConnection connection = DispatchProxy.Create<IConnection, ConnectionProxy>();
            ConnectionProxy connectionProxy = (ConnectionProxy)(object)connection;
            typeof(KdsProjectionWorker)
                .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(worker, connection);
            if (setChannels)
            {
                typeof(KdsProjectionWorker)
                    .GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(worker, channel);
                if (setNotificationChannel)
                {
                    typeof(KdsProjectionWorker)
                        .GetField(
                            "_notificationChannel",
                            BindingFlags.Instance | BindingFlags.NonPublic
                        )!
                        .SetValue(worker, notificationChannel);
                }
            }

            return new WorkerFixture(
                services,
                worker,
                channel,
                channelProxy,
                notificationChannel,
                notificationProxy,
                connectionProxy
            );
        }

        public Task DeliverAsync<TEvent>(string messageId, string eventType, TEvent message) =>
            DeliverRawAsync(messageId, eventType, JsonSerializer.SerializeToUtf8Bytes(message));

        public async Task DeliverRawAsync(string messageId, string eventType, byte[] body)
        {
            BasicProperties properties = new() { MessageId = messageId, Type = eventType };
            await DeliverRawAsync(properties, body);
        }

        public async Task DeliverRawAsync(BasicProperties properties, byte[] body)
        {
            BasicDeliverEventArgs args = new(
                "consumer",
                1,
                false,
                "exchange",
                "routing-key",
                properties,
                body,
                TestContext.Current.CancellationToken
            );
            MethodInfo handle = typeof(KdsProjectionWorker).GetMethod(
                "HandleMessageAsync",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)handle.Invoke(worker, [channel, args])!;
        }

        public Task DeliverObjectAsync(Guid messageId, object message) =>
            DeliverRawAsync(
                messageId.ToString(),
                message.GetType().FullName!,
                JsonSerializer.SerializeToUtf8Bytes(message, message.GetType())
            );

        public async Task<int> GetProjectedOrderCountAsync()
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<OrderReadDbContext>()
                .KitchenOrders.CountAsync(TestContext.Current.CancellationToken);
        }

        public async Task<Guid?[]> GetProjectedZonesAsync()
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<OrderReadDbContext>()
                .KitchenOrders.Select(order => order.ZoneId)
                .ToArrayAsync(TestContext.Current.CancellationToken);
        }

        public async Task<KitchenOrderReadModel> GetOrderAsync(Guid orderId)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<OrderReadDbContext>()
                .KitchenOrders.SingleAsync(
                    order => order.Id == orderId,
                    TestContext.Current.CancellationToken
                );
        }

        public async Task DisposeWorkerResourcesAsync()
        {
            MethodInfo dispose = typeof(KdsProjectionWorker).GetMethod(
                "DisposeResourcesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)dispose.Invoke(worker, null)!;
        }

        public async ValueTask DisposeAsync() => await services.DisposeAsync();
    }

    private static object? GetWorkerField(WorkerFixture fixture, string name)
    {
        FieldInfo field = typeof(WorkerFixture).GetField(
            "worker",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        object worker = field.GetValue(fixture)!;
        return typeof(KdsProjectionWorker)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(worker);
    }

    public class ChannelProxy : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }
        public int Disposals { get; private set; }
        public List<PublishedMessage> PublishedMessages { get; } = [];

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
            if (targetMethod?.Name == nameof(IChannel.DisposeAsync))
            {
                Disposals++;
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.BasicPublishAsync))
            {
                PublishedMessages.Add(
                    new PublishedMessage(
                        (string)args![0]!,
                        (string)args![1]!,
                        ((ReadOnlyMemory<byte>)args[4]!).ToArray(),
                        (BasicProperties)args[3]!
                    )
                );
                return ValueTask.CompletedTask;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public class NotificationChannelProxy : DispatchProxy
    {
        public List<PublishedMessage> PublishedMessages { get; } = [];
        public bool FailPublish { get; set; }
        public int Disposals { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IChannel.BasicPublishAsync))
            {
                if (FailPublish)
                {
                    throw new InvalidOperationException("Notification broker unavailable.");
                }

                PublishedMessages.Add(
                    new PublishedMessage(
                        (string)args![0]!,
                        (string)args![1]!,
                        ((ReadOnlyMemory<byte>)args[4]!).ToArray()
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
        string Exchange,
        string RoutingKey,
        byte[] Body,
        BasicProperties? Properties = null
    );
}
