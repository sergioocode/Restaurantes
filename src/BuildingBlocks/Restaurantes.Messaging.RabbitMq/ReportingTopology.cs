using RabbitMQ.Client;

namespace Restaurantes.Messaging.RabbitMq;

public static class ReportingTopology
{
    public const string ExchangeName = "reporting.events",
        DashboardProjectionUpdatedRoutingKey = "dashboard.projection-updated.v1",
        ReadModelQueueName = "reporting.dashboard.read-model",
        SignalRQueueName = "reporting.dashboard.signalr.projection-updated",
        DeadLetterExchangeName = "reporting.dead-letter",
        DeadLetterQueueName = "reporting.dead-letter.messages";

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            OrdersTopology.ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct
        );
        await channel.ExchangeDeclareAsync(
            PaymentsTopology.ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct
        );
        await channel.ExchangeDeclareAsync(
            SalesTopology.ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct
        );
        await channel.ExchangeDeclareAsync(
            ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct
        );
        await channel.ExchangeDeclareAsync(
            DeadLetterExchangeName,
            ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct
        );
        await channel.QueueDeclareAsync(
            DeadLetterQueueName,
            true,
            false,
            false,
            null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            DeadLetterQueueName,
            DeadLetterExchangeName,
            string.Empty,
            null,
            cancellationToken: ct
        );
        Dictionary<string, object?> arguments = new()
        {
            { "x-dead-letter-exchange", DeadLetterExchangeName },
        };
        await channel.QueueDeclareAsync(
            ReadModelQueueName,
            true,
            false,
            false,
            arguments,
            cancellationToken: ct
        );
        foreach (
            string key in new[]
            {
                OrdersTopology.OrderCreatedRoutingKey,
                OrdersTopology.OrderSubmittedRoutingKey,
                OrdersTopology.OrderPreparationStartedRoutingKey,
                OrdersTopology.OrderReadyRoutingKey,
                OrdersTopology.KitchenTicketUpdatedRoutingKey,
                OrdersTopology.OrderDeliveredRoutingKey,
                OrdersTopology.OrderLineCancelledRoutingKey,
                OrdersTopology.OrderCancelledRoutingKey,
            }
        )
        {
            await channel.QueueBindAsync(
                ReadModelQueueName,
                OrdersTopology.ExchangeName,
                key,
                null,
                cancellationToken: ct
            );
        }
        await channel.QueueBindAsync(
            ReadModelQueueName,
            PaymentsTopology.ExchangeName,
            PaymentsTopology.PaymentCapturedRoutingKey,
            null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            ReadModelQueueName,
            PaymentsTopology.ExchangeName,
            PaymentsTopology.PaymentRefundedRoutingKey,
            null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            ReadModelQueueName,
            SalesTopology.ExchangeName,
            SalesTopology.SaleCompletedRoutingKey,
            null,
            cancellationToken: ct
        );
        await channel.QueueDeclareAsync(
            SignalRQueueName,
            true,
            false,
            false,
            arguments,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            SignalRQueueName,
            ExchangeName,
            DashboardProjectionUpdatedRoutingKey,
            null,
            cancellationToken: ct
        );
    }
}
