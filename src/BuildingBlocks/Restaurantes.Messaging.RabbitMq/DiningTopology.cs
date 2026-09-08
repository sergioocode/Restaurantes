using RabbitMQ.Client;

namespace Restaurantes.Messaging.RabbitMq;

public static class DiningTopology
{
    public const string QueueName = "dining.table-sessions";
    public const string DeadLetterExchangeName = "dining.dead-letter";
    public const string DeadLetterQueueName = "dining.dead-letter.messages";

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            OrdersTopology.ExchangeName,
            ExchangeType.Topic,
            true,
            false,
            null,
            cancellationToken: ct
        );
        await channel.ExchangeDeclareAsync(
            PaymentsTopology.ExchangeName,
            ExchangeType.Topic,
            true,
            false,
            null,
            cancellationToken: ct
        );
        await channel.ExchangeDeclareAsync(
            DeadLetterExchangeName,
            ExchangeType.Fanout,
            true,
            false,
            null,
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
            arguments: null,
            cancellationToken: ct
        );
        Dictionary<string, object?> args = new()
        {
            ["x-dead-letter-exchange"] = DeadLetterExchangeName,
        };
        await channel.QueueDeclareAsync(QueueName, true, false, false, args, cancellationToken: ct);
        await channel.QueueBindAsync(
            QueueName,
            OrdersTopology.ExchangeName,
            OrdersTopology.OrderCreatedRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            QueueName,
            OrdersTopology.ExchangeName,
            OrdersTopology.OrderSubmittedRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            QueueName,
            OrdersTopology.ExchangeName,
            OrdersTopology.OrderReadyRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            QueueName,
            OrdersTopology.ExchangeName,
            OrdersTopology.OrderDeliveredRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            QueueName,
            OrdersTopology.ExchangeName,
            OrdersTopology.OrderLineCancelledRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            QueueName,
            PaymentsTopology.ExchangeName,
            PaymentsTopology.PaymentCapturedRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            QueueName,
            PaymentsTopology.ExchangeName,
            PaymentsTopology.PaymentRefundedRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
    }
}
