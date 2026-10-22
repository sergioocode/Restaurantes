using System.Reflection;
using RabbitMQ.Client;
using Restaurantes.Messaging.RabbitMq;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class RabbitMqTopologyTests
{
    [Fact]
    public async Task Service_topologies_declare_durable_queues_and_bind_required_events()
    {
        IChannel channel = DispatchProxy.Create<IChannel, RecordingChannel>();
        RecordingChannel recorder = (RecordingChannel)(object)channel;
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await CatalogTopology.DeclareAsync(channel, cancellationToken);
        await DiningTopology.DeclareAsync(channel, cancellationToken);
        await OrdersTopology.DeclareAsync(channel, cancellationToken);
        await PaymentsTopology.DeclareAsync(channel, cancellationToken);
        await ReportingTopology.DeclareAsync(channel, cancellationToken);
        await RestaurantOperationsTopology.DeclareAsync(channel, cancellationToken);
        await SalesTopology.DeclareAsync(channel, cancellationToken);

        Assert.Contains(
            recorder.Exchanges,
            exchange =>
                exchange.Name == CatalogTopology.ExchangeName && exchange.Type == ExchangeType.Topic
        );
        Assert.Contains(
            recorder.Exchanges,
            exchange =>
                exchange.Name == OrdersTopology.DeadLetterExchangeName
                && exchange.Type == ExchangeType.Fanout
        );
        Assert.Contains(
            recorder.Queues,
            queue =>
                queue.Name == PaymentsTopology.OrdersIntegrationQueueName
                && queue.Durable
                && Equals(
                    queue.Arguments["x-dead-letter-exchange"],
                    PaymentsTopology.DeadLetterExchangeName
                )
        );
        Assert.Contains(
            recorder.Queues,
            queue =>
                queue.Name == OrdersTopology.KdsQueueName + ".retry.1"
                && Equals(queue.Arguments["x-message-ttl"], 5000L)
                && Equals(queue.Arguments["x-dead-letter-routing-key"], OrdersTopology.KdsQueueName)
        );

        AssertBound(
            CatalogTopology.OrdersIntegrationQueueName,
            CatalogTopology.ExchangeName,
            CatalogTopology.KitchenStationChangedRoutingKey
        );
        AssertBound(
            DiningTopology.QueueName,
            PaymentsTopology.ExchangeName,
            PaymentsTopology.PaymentRefundedRoutingKey
        );
        AssertBound(
            OrdersTopology.KdsSignalRQueueName,
            OrdersTopology.ExchangeName,
            OrdersTopology.KdsOrderUpdatedRoutingKey
        );
        AssertBound(
            PaymentsTopology.PayableOrdersQueueName,
            OrdersTopology.ExchangeName,
            OrdersTopology.OrderLineCancelledRoutingKey
        );
        AssertBound(
            ReportingTopology.ReadModelQueueName,
            SalesTopology.ExchangeName,
            SalesTopology.SaleCompletedRoutingKey
        );
        AssertBound(
            RestaurantOperationsTopology.ReadModelQueueName,
            RestaurantOperationsTopology.ExchangeName,
            RestaurantOperationsTopology.CompanyChangedRoutingKey
        );
        AssertBound(
            SalesTopology.TicketNotificationQueueName,
            SalesTopology.ExchangeName,
            SalesTopology.SaleCompletedRoutingKey
        );

        void AssertBound(string queue, string exchange, string routingKey) =>
            Assert.Contains(
                recorder.Bindings,
                binding =>
                    binding.Queue == queue
                    && binding.Exchange == exchange
                    && binding.RoutingKey == routingKey
            );
    }

    public class RecordingChannel : DispatchProxy
    {
        public List<(string Name, string Type)> Exchanges { get; } = [];
        public List<DeclaredQueue> Queues { get; } = [];
        public List<(string Queue, string Exchange, string RoutingKey)> Bindings { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            string methodName = targetMethod?.Name ?? throw new InvalidOperationException();
            switch (methodName)
            {
                case nameof(IChannel.ExchangeDeclareAsync):
                    Exchanges.Add(((string)args![0]!, (string)args[1]!));
                    break;
                case nameof(IChannel.QueueDeclareAsync):
                    Queues.Add(
                        new DeclaredQueue(
                            (string)args![0]!,
                            (bool)args[1]!,
                            (bool)args[2]!,
                            (bool)args[3]!,
                            (IDictionary<string, object?>)args[4]!
                        )
                    );
                    break;
                case nameof(IChannel.QueueBindAsync):
                    Bindings.Add(((string)args![0]!, (string)args[1]!, (string)args[2]!));
                    break;
                case nameof(IChannel.QueueUnbindAsync):
                    break;
                default:
                    throw new NotSupportedException(methodName);
            }

            Type returnType = targetMethod!.ReturnType;
            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                Type resultType = returnType.GetGenericArguments()[0];
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(
                        null,
                        [resultType.IsValueType ? Activator.CreateInstance(resultType) : null]
                    );
            }

            if (returnType == typeof(ValueTask))
            {
                return ValueTask.CompletedTask;
            }

            throw new NotSupportedException(returnType.FullName);
        }
    }

    public sealed record DeclaredQueue(
        string Name,
        bool Durable,
        bool Exclusive,
        bool AutoDelete,
        IDictionary<string, object?> Arguments
    );
}
