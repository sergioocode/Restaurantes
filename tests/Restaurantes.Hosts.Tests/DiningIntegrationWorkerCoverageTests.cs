using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Dining.Application;
using Restaurantes.Dining.Domain;
using Restaurantes.Dining.Infrastructure.Workers;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Payments.Contracts.Events;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class DiningIntegrationWorkerCoverageTests
{
    [Fact]
    public async Task Captured_payment_is_projected_and_acknowledged()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid orderId = Guid.NewGuid();
        PaymentCaptured payment = Captured(orderId);

        await fixture.DeliverAsync(
            Guid.NewGuid().ToString(),
            typeof(PaymentCaptured).FullName!,
            JsonSerializer.SerializeToUtf8Bytes(payment)
        );

        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(0, fixture.ChannelProxy.Rejections);
        Assert.Equal(orderId, fixture.Store.PendingPayment!.OrderId);
        Assert.Equal(payment.AmountPaid, fixture.Store.PendingPayment.AmountPaid);
        Assert.True(fixture.Store.MessageWasMarkedProcessed);
    }

    [Fact]
    public async Task Duplicate_messages_are_acknowledged_without_reapplying_them()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(
            hasProcessedMessage: true
        );

        await fixture.DeliverAsync(
            Guid.NewGuid().ToString(),
            typeof(PaymentCaptured).FullName!,
            JsonSerializer.SerializeToUtf8Bytes(Captured(Guid.NewGuid()))
        );

        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
        Assert.Null(fixture.Store.PendingPayment);
        Assert.False(fixture.Store.MessageWasMarkedProcessed);
    }

    [Fact]
    public async Task Invalid_ids_and_event_types_are_rejected_without_requeue()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        await fixture.DeliverAsync(
            "not-a-guid",
            typeof(PaymentCaptured).FullName!,
            "{}"u8.ToArray()
        );
        await fixture.DeliverAsync(Guid.NewGuid().ToString(), "unknown.event", "{}"u8.ToArray());

        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(2, fixture.ChannelProxy.Rejections);
    }

    [Fact]
    public async Task Transient_storage_failure_is_retried_and_exhausted_failure_is_rejected()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(
            failOnOrderLookup: true
        );
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(Captured(Guid.NewGuid()));

        await fixture.DeliverAsync(
            Guid.NewGuid().ToString(),
            typeof(PaymentCaptured).FullName!,
            body
        );
        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(0, fixture.ChannelProxy.Rejections);
        PublishedRetry retry = Assert.Single(fixture.ChannelProxy.PublishedRetries);
        Assert.Equal($"{DiningTopology.QueueName}.retry.1", retry.RoutingKey);
        Assert.Equal(1, retry.Properties.Headers!["x-restaurantes-retry-count"]);

        BasicProperties exhausted = new()
        {
            Type = typeof(PaymentCaptured).FullName,
            Headers = new Dictionary<string, object?>
            {
                ["x-restaurantes-retry-count"] = RabbitMqRetry.MaximumAttempts,
            },
        };
        await fixture.DeliverAsync(exhausted, body);

        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
        Assert.Single(fixture.ChannelProxy.PublishedRetries);
    }

    private static PaymentCaptured Captured(Guid orderId) =>
        new(
            Guid.NewGuid(),
            orderId,
            Guid.NewGuid(),
            15m,
            "Cash",
            "DineIn",
            "POS",
            "ticket-1",
            DateTime.UtcNow,
            AmountPaid: 15m,
            TipAmountPaid: 2m
        );

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly DiningIntegrationWorker worker;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            DiningIntegrationWorker worker,
            IChannel channel,
            StoreProxy store,
            ChannelProxy channelProxy
        )
        {
            this.services = services;
            this.worker = worker;
            this.channel = channel;
            Store = store;
            ChannelProxy = channelProxy;
        }

        public StoreProxy Store { get; }
        public ChannelProxy ChannelProxy { get; }

        public static Task<WorkerFixture> CreateAsync(
            bool hasProcessedMessage = false,
            bool failOnOrderLookup = false
        )
        {
            IDiningStore store = DispatchProxy.Create<IDiningStore, StoreProxy>();
            StoreProxy storeProxy = (StoreProxy)(object)store;
            storeProxy.HasProcessedMessage = hasProcessedMessage;
            storeProxy.FailOnOrderLookup = failOnOrderLookup;
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(store)
                .AddSingleton(TimeProvider.System)
                .AddScoped<DiningIntegrationService>()
                .BuildServiceProvider();
            DiningIntegrationWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                NullLogger<DiningIntegrationWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            ChannelProxy channelProxy = (ChannelProxy)(object)channel;
            typeof(DiningIntegrationWorker)
                .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(worker, channel);
            return Task.FromResult(
                new WorkerFixture(services, worker, channel, storeProxy, channelProxy)
            );
        }

        public Task DeliverAsync(string messageId, string eventType, byte[] body)
        {
            return DeliverAsync(
                new BasicProperties { MessageId = messageId, Type = eventType },
                body
            );
        }

        public async Task DeliverAsync(BasicProperties properties, byte[] body)
        {
            BasicDeliverEventArgs args = new(
                "consumer",
                (ulong)(ChannelProxy.Acknowledgements + ChannelProxy.Rejections + 1),
                false,
                "exchange",
                "routing-key",
                properties,
                body
            );
            MethodInfo handle = typeof(DiningIntegrationWorker).GetMethod(
                "Handle",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)handle.Invoke(worker, [channel, args])!;
        }

        public async ValueTask DisposeAsync()
        {
            await worker.StopAsync(CancellationToken.None);
            await services.DisposeAsync();
        }
    }

    public class StoreProxy : DispatchProxy
    {
        public bool HasProcessedMessage { get; set; }
        public bool FailOnOrderLookup { get; set; }
        public bool MessageWasMarkedProcessed { get; private set; }
        public DiningPendingPayment? PendingPayment { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(IDiningStore.HasProcessedMessageAsync) => Task.FromResult(
                    HasProcessedMessage
                ),
                nameof(IDiningStore.FindOrderAsync) => FindOrder(),
                nameof(IDiningStore.FindPendingPaymentAsync) => Task.FromResult(PendingPayment),
                nameof(IDiningStore.Add) => Add(args![0]!),
                nameof(IDiningStore.MarkMessageProcessed) => MarkMessageProcessed(),
                nameof(IDiningStore.SaveChangesAsync) => Task.CompletedTask,
                _ => throw new NotSupportedException(targetMethod?.Name),
            };

        private Task<DiningSessionOrder?> FindOrder()
        {
            return FailOnOrderLookup
                ? throw new InvalidOperationException("Temporary database outage.")
                : Task.FromResult<DiningSessionOrder?>(null);
        }

        private object? Add(object entity)
        {
            PendingPayment = Assert.IsType<DiningPendingPayment>(entity);
            return null;
        }

        private object? MarkMessageProcessed()
        {
            MessageWasMarkedProcessed = true;
            return null;
        }
    }

    public class ChannelProxy : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }
        public List<PublishedRetry> PublishedRetries { get; } = [];

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
                PublishedRetries.Add(
                    new PublishedRetry((string)args![1]!, (BasicProperties)args[3]!)
                );
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.DisposeAsync))
            {
                return ValueTask.CompletedTask;
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public sealed record PublishedRetry(string RoutingKey, BasicProperties Properties);
}
