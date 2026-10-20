using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Restaurantes.Messaging.RabbitMq;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class RabbitMqTopologyCoverageTests
{
    [Fact]
    public async Task Application_topologies_declare_durable_exchanges_queues_retry_routes_and_bindings()
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

        string[] exchanges = recorder
            .Calls.Where(call => call.Method == nameof(IChannel.ExchangeDeclareAsync))
            .Select(call => (string)call.Arguments[0]!)
            .Distinct()
            .ToArray();
        Assert.Contains(CatalogTopology.ExchangeName, exchanges);
        Assert.Contains(DiningTopology.DeadLetterExchangeName, exchanges);
        Assert.Contains(OrdersTopology.ExchangeName, exchanges);
        Assert.Contains(PaymentsTopology.ExchangeName, exchanges);
        Assert.Contains(ReportingTopology.ExchangeName, exchanges);
        Assert.Contains(RestaurantOperationsTopology.ExchangeName, exchanges);
        Assert.Contains(SalesTopology.ExchangeName, exchanges);
        Assert.All(
            recorder.Calls.Where(call => call.Method == nameof(IChannel.ExchangeDeclareAsync)),
            call => Assert.True((bool)call.Arguments[2]!)
        );

        string[] queues = recorder
            .Calls.Where(call => call.Method == nameof(IChannel.QueueDeclareAsync))
            .Select(call => (string)call.Arguments[0]!)
            .ToArray();
        Assert.Contains(OrdersTopology.KdsQueueName, queues);
        Assert.Contains(OrdersTopology.KdsSignalRQueueName, queues);
        Assert.Contains(ReportingTopology.ReadModelQueueName, queues);
        Assert.Contains(ReportingTopology.SignalRQueueName, queues);
        Assert.Contains(RestaurantOperationsTopology.ReadModelQueueName, queues);
        Assert.All(
            recorder.Calls.Where(call => call.Method == nameof(IChannel.QueueDeclareAsync)),
            call => Assert.True((bool)call.Arguments[1]!)
        );
        Assert.Contains(
            recorder.Calls.Where(call => call.Method == nameof(IChannel.QueueDeclareAsync)),
            call =>
                call.Arguments[0]!.ToString()!.Contains(".retry.1", StringComparison.Ordinal)
                && call.Arguments[4] is IDictionary<string, object?> arguments
                && arguments.ContainsKey("x-message-ttl")
                && arguments.ContainsKey("x-dead-letter-routing-key")
        );

        var bindings = recorder
            .Calls.Where(call => call.Method == nameof(IChannel.QueueBindAsync))
            .Select(call =>
                (
                    Queue: (string)call.Arguments[0]!,
                    Exchange: (string)call.Arguments[1]!,
                    Key: (string)call.Arguments[2]!
                )
            )
            .ToArray();
        Assert.Contains(
            bindings,
            binding =>
                binding.Queue == OrdersTopology.KdsQueueName
                && binding.Key == OrdersTopology.OrderCreatedRoutingKey
        );
        Assert.Contains(
            bindings,
            binding =>
                binding.Queue == ReportingTopology.ReadModelQueueName
                && binding.Key == PaymentsTopology.PaymentCapturedRoutingKey
        );
        Assert.Contains(
            bindings,
            binding =>
                binding.Queue == RestaurantOperationsTopology.ReadModelQueueName
                && binding.Key == RestaurantOperationsTopology.RestaurantChangedRoutingKey
        );
        Assert.Contains(
            bindings,
            binding =>
                binding.Queue == SalesTopology.ReadModelQueueName
                && binding.Key == SalesTopology.SaleCompletedRoutingKey
        );
    }

    public class RecordingChannel : DispatchProxy
    {
        public List<RecordedCall> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            string method = targetMethod?.Name ?? throw new InvalidOperationException();
            object?[] arguments = args ?? [];
            Calls.Add(new RecordedCall(method, arguments));
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
                    .Invoke(null, [null]);
            }
            throw new NotSupportedException(method);
        }
    }

    public sealed record RecordedCall(string Method, object?[] Arguments);
}
