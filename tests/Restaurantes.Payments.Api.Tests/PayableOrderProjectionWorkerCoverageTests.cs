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

        public async Task DeliverAsync<TEvent>(Guid messageId, string eventType, TEvent message) =>
            await DeliverRawAsync(
                messageId.ToString(),
                eventType,
                JsonSerializer.SerializeToUtf8Bytes(message)
            );

        public async Task DeliverRawAsync(string messageId, string eventType, byte[] body)
        {
            BasicProperties properties = new() { MessageId = messageId, Type = eventType };
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
}
