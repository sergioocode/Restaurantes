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
            NotificationChannelProxy notificationProxy
        )
        {
            this.services = services;
            this.worker = worker;
            this.channel = channel;
            ChannelProxy = channelProxy;
            NotificationChannel = notificationChannel;
            NotificationProxy = notificationProxy;
        }

        public ChannelProxy ChannelProxy { get; }
        public IChannel NotificationChannel { get; }
        public NotificationChannelProxy NotificationProxy { get; }

        public static async Task<WorkerFixture> CreateAsync(bool setChannels = true)
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
            if (setChannels)
            {
                typeof(KdsProjectionWorker)
                    .GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(worker, channel);
                typeof(KdsProjectionWorker)
                    .GetField(
                        "_notificationChannel",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    )!
                    .SetValue(worker, notificationChannel);
            }

            return new WorkerFixture(
                services,
                worker,
                channel,
                channelProxy,
                notificationChannel,
                notificationProxy
            );
        }

        public Task DeliverAsync<TEvent>(string messageId, string eventType, TEvent message) =>
            DeliverRawAsync(messageId, eventType, JsonSerializer.SerializeToUtf8Bytes(message));

        public async Task DeliverRawAsync(string messageId, string eventType, byte[] body)
        {
            BasicProperties properties = new() { MessageId = messageId, Type = eventType };
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

        public async ValueTask DisposeAsync() => await services.DisposeAsync();
    }

    public class ChannelProxy : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }

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

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public class NotificationChannelProxy : DispatchProxy
    {
        public List<PublishedMessage> PublishedMessages { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IChannel.BasicPublishAsync))
            {
                PublishedMessages.Add(
                    new PublishedMessage(
                        (string)args![0]!,
                        (string)args[1]!,
                        ((ReadOnlyMemory<byte>)args[4]!).ToArray()
                    )
                );
                return ValueTask.CompletedTask;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public sealed record PublishedMessage(string Exchange, string RoutingKey, byte[] Body);
}
