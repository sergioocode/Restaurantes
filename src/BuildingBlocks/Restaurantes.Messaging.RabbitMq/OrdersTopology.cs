using RabbitMQ.Client;

namespace Restaurantes.Messaging.RabbitMq;

public static class OrdersTopology
{
    public const string ExchangeName = "orders.events";
    public const string OrderCreatedRoutingKey = "order.created.v1";
    public const string OrderSubmittedRoutingKey = "order.submitted.v1";
    public const string OrderPreparationStartedRoutingKey = "order.preparation-started.v1";
    public const string OrderReadyRoutingKey = "order.ready.v1";
    public const string KitchenTicketUpdatedRoutingKey = "kitchen-ticket.updated.v1";
    public const string OrderDeliveredRoutingKey = "order.delivered.v1";
    public const string OrderLineCancelledRoutingKey = "order.line-cancelled.v1";
    public const string OrderCancelledRoutingKey = "order.cancelled.v1";
    public const string KdsOrderUpdatedRoutingKey = "kds.order.updated.v1";
    public const string KdsQueueName = "orders.kds.read-model";
    public const string KdsSignalRQueueName = "orders.kds.signalr";
    public const string DeadLetterExchangeName = "orders.dead-letter";
    public const string DeadLetterQueueName = "orders.kds.read-model.dead-letter";

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.ExchangeDeclareAsync(
            DeadLetterExchangeName,
            ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.QueueDeclareAsync(
            DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            DeadLetterQueueName,
            DeadLetterExchangeName,
            string.Empty,
            arguments: null,
            cancellationToken: cancellationToken
        );

        Dictionary<string, object?> queueArguments = new()
        {
            ["x-dead-letter-exchange"] = DeadLetterExchangeName,
        };

        await channel.QueueDeclareAsync(
            KdsQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            KdsQueueName,
            ExchangeName,
            OrderCreatedRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            KdsQueueName,
            ExchangeName,
            OrderSubmittedRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            KdsQueueName,
            ExchangeName,
            OrderPreparationStartedRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            KdsQueueName,
            ExchangeName,
            OrderReadyRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            KdsQueueName,
            ExchangeName,
            KitchenTicketUpdatedRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            KdsQueueName,
            ExchangeName,
            OrderDeliveredRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            KdsQueueName,
            ExchangeName,
            OrderLineCancelledRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            KdsQueueName,
            ExchangeName,
            OrderCancelledRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.QueueDeclareAsync(
            KdsSignalRQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            KdsSignalRQueueName,
            ExchangeName,
            KdsOrderUpdatedRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken
        );
    }
}
