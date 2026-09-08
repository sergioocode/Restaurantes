using RabbitMQ.Client;

namespace Restaurantes.Messaging.RabbitMq;

public static class PaymentsTopology
{
    public const string ExchangeName = "payments.events";
    public const string PaymentCapturedRoutingKey = "payment.captured.v1";
    public const string PaymentRefundedRoutingKey = "payment.refunded.v1";
    public const string OrdersIntegrationQueueName = "payments.orders-integration";
    public const string PayableOrdersQueueName = "payments.payable-orders";
    public const string CashRegisterQueueName = "payments.cash-register";
    public const string DeadLetterExchangeName = "payments.dead-letter";
    public const string DeadLetterQueueName = "payments.dead-letter.messages";

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct
        );
        await channel.ExchangeDeclareAsync(
            OrdersTopology.ExchangeName,
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
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            DeadLetterQueueName,
            DeadLetterExchangeName,
            string.Empty,
            arguments: null,
            cancellationToken: ct
        );
        Dictionary<string, object?> arguments = new()
        {
            ["x-dead-letter-exchange"] = DeadLetterExchangeName,
        };
        await channel.QueueDeclareAsync(
            OrdersIntegrationQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: arguments,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            OrdersIntegrationQueueName,
            ExchangeName,
            PaymentCapturedRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
        await channel.QueueDeclareAsync(
            CashRegisterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: arguments,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            CashRegisterQueueName,
            ExchangeName,
            PaymentCapturedRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            CashRegisterQueueName,
            ExchangeName,
            PaymentRefundedRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            OrdersIntegrationQueueName,
            ExchangeName,
            PaymentRefundedRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
        await channel.QueueDeclareAsync(
            PayableOrdersQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: arguments,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            PayableOrdersQueueName,
            OrdersTopology.ExchangeName,
            OrdersTopology.OrderCreatedRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            PayableOrdersQueueName,
            OrdersTopology.ExchangeName,
            OrdersTopology.OrderLineCancelledRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
    }
}
