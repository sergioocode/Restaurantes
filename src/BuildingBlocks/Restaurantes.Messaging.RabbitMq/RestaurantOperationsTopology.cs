using RabbitMQ.Client;

namespace Restaurantes.Messaging.RabbitMq;

public static class RestaurantOperationsTopology
{
    public const string ExchangeName = "restaurant-operations.events";
    public const string RestaurantChangedRoutingKey = "restaurant.changed.v1";
    public const string ReadModelQueueName = "restaurant-operations.read-model";
    public const string DeadLetterExchangeName = "restaurant-operations.dead-letter";
    public const string DeadLetterQueueName = "restaurant-operations.read-model.dead-letter";

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken
        );

        await channel.ExchangeDeclareAsync(
            exchange: DeadLetterExchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken
        );

        await channel.QueueDeclareAsync(
            queue: DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken
        );

        await channel.QueueBindAsync(
            queue: DeadLetterQueueName,
            exchange: DeadLetterExchangeName,
            routingKey: string.Empty,
            arguments: null,
            cancellationToken: cancellationToken
        );

        Dictionary<string, object?> queueArguments = new()
        {
            ["x-dead-letter-exchange"] = DeadLetterExchangeName,
        };

        await channel.QueueDeclareAsync(
            queue: ReadModelQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: cancellationToken
        );

        await channel.QueueBindAsync(
            queue: ReadModelQueueName,
            exchange: ExchangeName,
            routingKey: RestaurantChangedRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken
        );
    }
}
