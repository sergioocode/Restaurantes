using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Sales.Application;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Sales.Domain;
using Restaurantes.Sales.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.Sales.Application.Tests;

public sealed class CheckoutProcessWorkerCoverageTests
{
    [Fact]
    public async Task Supported_checkout_events_are_processed_and_acknowledged()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        DateTime occurredAt = new(2026, 10, 21, 18, 0, 0, DateTimeKind.Utc);
        PaymentCaptured captured = new(
            Guid.NewGuid(),
            orderId,
            restaurantId,
            12m,
            "Cash",
            "DineIn",
            "Pos",
            "",
            occurredAt,
            IsFullyPaid: true
        );
        PaymentRefunded refunded = new(
            Guid.NewGuid(),
            orderId,
            restaurantId,
            12m,
            "Cash",
            "DineIn",
            "Pos",
            "Customer request",
            occurredAt
        );
        OrderDelivered delivered = new(
            orderId,
            restaurantId,
            null,
            null,
            null,
            "",
            "",
            "DineIn",
            "Pos",
            "AtCheckout",
            1,
            occurredAt,
            occurredAt,
            occurredAt,
            occurredAt,
            occurredAt,
            [],
            []
        );

        await fixture.DeliverAsync(Guid.NewGuid(), captured);
        await fixture.DeliverAsync(Guid.NewGuid(), refunded);
        await fixture.DeliverAsync(Guid.NewGuid(), delivered);

        Assert.Equal(3, fixture.Store.ProcessedMessages.Count);
        Assert.Equal(3, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(0, fixture.ChannelProxy.Rejections);
        Assert.Equal(orderId, Assert.Single(fixture.Store.DeliveredOrders).OrderId);
    }

    [Theory]
    [InlineData("not-a-guid", "Restaurantes.Payments.Contracts.Events.PaymentCaptured", "{}")]
    [InlineData("{guid}", "unknown.event", "{}")]
    [InlineData("{guid}", "Restaurantes.Payments.Contracts.Events.PaymentCaptured", "{")]
    [InlineData("{guid}", "Restaurantes.Payments.Contracts.Events.PaymentCaptured", "null")]
    public async Task Invalid_checkout_messages_are_rejected_without_processing(
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

        await fixture.DeliverRawAsync(messageId, eventType, Encoding.UTF8.GetBytes(body));

        Assert.Empty(fixture.Store.ProcessedMessages);
        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
    }

    [Fact]
    public async Task Transient_checkout_failure_is_republished_to_the_retry_queue()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        fixture.Store.Failure = new InvalidOperationException("store unavailable");
        PaymentCaptured captured = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            12m,
            "Cash",
            "DineIn",
            "Pos",
            "",
            DateTime.UtcNow
        );

        await fixture.DeliverAsync(Guid.NewGuid(), captured);

        Assert.Equal(1, fixture.ChannelProxy.Publications);
        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(0, fixture.ChannelProxy.Rejections);
        Assert.Empty(fixture.Store.ProcessedMessages);
    }

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly CheckoutProcessWorker worker;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            CheckoutProcessWorker worker,
            IChannel channel,
            ChannelProxy channelProxy,
            InMemorySaleStore store
        )
        {
            this.services = services;
            this.worker = worker;
            this.channel = channel;
            ChannelProxy = channelProxy;
            Store = store;
        }

        public ChannelProxy ChannelProxy { get; }
        public InMemorySaleStore Store { get; }

        public static Task<WorkerFixture> CreateAsync()
        {
            InMemorySaleStore store = new();
            SaleCommandService commandService = new(store, TimeProvider.System);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(commandService)
                .BuildServiceProvider();
            CheckoutProcessWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                NullLogger<CheckoutProcessWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            ChannelProxy proxy = (ChannelProxy)(object)channel;
            typeof(CheckoutProcessWorker)
                .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(worker, channel);
            return Task.FromResult(new WorkerFixture(services, worker, channel, proxy, store));
        }

        public Task DeliverAsync<TEvent>(Guid messageId, TEvent message) =>
            DeliverRawAsync(
                messageId.ToString(),
                typeof(TEvent).FullName!,
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
            MethodInfo handle = typeof(CheckoutProcessWorker).GetMethod(
                "Handle",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)handle.Invoke(worker, [channel, args])!;
        }

        public async ValueTask DisposeAsync() => await services.DisposeAsync();
    }

    private sealed class InMemorySaleStore : ISaleWriteStore
    {
        private readonly HashSet<Guid> processed = [];

        public List<Guid> ProcessedMessages { get; } = [];
        public List<DeliveredOrderSnapshot> DeliveredOrders { get; } = [];
        public List<Sale> Sales { get; } = [];
        public Exception? Failure { get; set; }

        public Task<bool> WasProcessedAsync(Guid messageId, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(processed.Contains(messageId));
        }

        public Task<Sale?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Sales.SingleOrDefault(sale => sale.Id == id));

        public Task<Sale?> FindByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(
                Sales.SingleOrDefault(sale => sale.Orders.Any(order => order.OrderId == orderId))
            );

        public Task<DeliveredOrderSnapshot?> FindDeliveredOrderAsync(
            Guid orderId,
            CancellationToken cancellationToken
        ) => Task.FromResult(DeliveredOrders.SingleOrDefault(item => item.OrderId == orderId));

        public void Add(Sale sale) => Sales.Add(sale);

        public Task UpsertDeliveredOrderAsync(
            DeliveredOrderSnapshot deliveredOrder,
            CancellationToken cancellationToken
        )
        {
            DeliveredOrders.Add(deliveredOrder);
            return Task.CompletedTask;
        }

        public void AddIntegrationEvent(SaleCompleted integrationEvent) { }

        public void MarkProcessed(Guid messageId, DateTime processedAtUtc)
        {
            processed.Add(messageId);
            ProcessedMessages.Add(messageId);
        }

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public class ChannelProxy : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }
        public int Publications { get; private set; }

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
                Publications++;
                return ValueTask.CompletedTask;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
