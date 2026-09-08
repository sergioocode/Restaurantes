using RabbitMQ.Client;

namespace Restaurantes.Messaging.RabbitMq;

public static class CatalogTopology
{
    public const string ExchangeName = "catalog.events";
    public const string CategoryChangedRoutingKey = "category.changed.v1";
    public const string ProductChangedRoutingKey = "product.changed.v1";
    public const string CatalogItemChangedRoutingKey = "catalog-item.changed.v1";
    public const string KitchenStationChangedRoutingKey = "kitchen-station.changed.v1";
    public const string ReadModelQueueName = "catalog.read-model";
    public const string OrdersIntegrationQueueName = "catalog.orders-integration";
    public const string DeadLetterExchangeName = "catalog.dead-letter";
    public const string DeadLetterQueueName = "catalog.dead-letter.messages";

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
        Dictionary<string, object?> arguments = new()
        {
            ["x-dead-letter-exchange"] = DeadLetterExchangeName,
        };
        await channel.QueueDeclareAsync(
            ReadModelQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: arguments,
            cancellationToken: cancellationToken
        );
        foreach (
            string key in new[]
            {
                CategoryChangedRoutingKey,
                ProductChangedRoutingKey,
                CatalogItemChangedRoutingKey,
                KitchenStationChangedRoutingKey,
            }
        )
        {
            await channel.QueueBindAsync(
                ReadModelQueueName,
                ExchangeName,
                key,
                arguments: null,
                cancellationToken: cancellationToken
            );
        }

        await channel.QueueDeclareAsync(
            OrdersIntegrationQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: arguments,
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            OrdersIntegrationQueueName,
            ExchangeName,
            CatalogItemChangedRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            OrdersIntegrationQueueName,
            ExchangeName,
            KitchenStationChangedRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken
        );
    }
}
