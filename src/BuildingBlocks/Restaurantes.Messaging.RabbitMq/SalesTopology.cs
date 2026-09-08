using RabbitMQ.Client;

namespace Restaurantes.Messaging.RabbitMq;

public static class SalesTopology
{
    public const string ExchangeName = "sales.events";
    public const string SaleCompletedRoutingKey = "sale.completed.v1";
    public const string CheckoutQueueName = "sales.checkout-process";
    public const string ReadModelQueueName = "sales.read-model";
    public const string DeadLetterExchangeName = "sales.dead-letter";
    public const string DeadLetterQueueName = "sales.dead-letter.messages";

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        foreach (
            string exchange in new[]
            {
                ExchangeName,
                OrdersTopology.ExchangeName,
                PaymentsTopology.ExchangeName,
            }
        )
        {
            await channel.ExchangeDeclareAsync(
                exchange,
                ExchangeType.Topic,
                true,
                false,
                null,
                cancellationToken: ct
            );
        }

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
            null,
            cancellationToken: ct
        );
        Dictionary<string, object?> args = new()
        {
            ["x-dead-letter-exchange"] = DeadLetterExchangeName,
        };
        await channel.QueueDeclareAsync(
            CheckoutQueueName,
            true,
            false,
            false,
            args,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            CheckoutQueueName,
            PaymentsTopology.ExchangeName,
            PaymentsTopology.PaymentCapturedRoutingKey,
            null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            CheckoutQueueName,
            PaymentsTopology.ExchangeName,
            PaymentsTopology.PaymentRefundedRoutingKey,
            null,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            CheckoutQueueName,
            OrdersTopology.ExchangeName,
            OrdersTopology.OrderDeliveredRoutingKey,
            null,
            cancellationToken: ct
        );
        await channel.QueueDeclareAsync(
            ReadModelQueueName,
            true,
            false,
            false,
            args,
            cancellationToken: ct
        );
        await channel.QueueBindAsync(
            ReadModelQueueName,
            ExchangeName,
            SaleCompletedRoutingKey,
            null,
            cancellationToken: ct
        );
    }
}
