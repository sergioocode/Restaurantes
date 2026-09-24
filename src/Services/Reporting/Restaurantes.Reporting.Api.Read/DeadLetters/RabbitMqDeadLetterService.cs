using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Restaurantes.Messaging.RabbitMq;

namespace Restaurantes.Reporting.Api.Read.DeadLetters;

public sealed record DeadLetterQueueStatus(
    string Queue,
    uint MessageCount,
    uint ConsumerCount,
    bool Available
);

public sealed record DeadLetterReplayResult(
    string Queue,
    int Requested,
    int Replayed,
    uint Remaining,
    bool Blocked
);

public sealed class RabbitMqDeadLetterService(IOptions<RabbitMqOptions> options)
{
    private const string RetryCountHeader = "x-restaurantes-retry-count";
    private const string OriginalQueueHeader = "x-restaurantes-original-queue";
    private const string LastErrorHeader = "x-restaurantes-last-error";
    private const string LastFailedAtHeader = "x-restaurantes-last-failed-at";

    private static readonly IReadOnlyDictionary<string, HashSet<string>> QueueOrigins =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [CatalogTopology.DeadLetterQueueName] =
            [
                CatalogTopology.ReadModelQueueName,
                CatalogTopology.OrdersIntegrationQueueName,
            ],
            [DiningTopology.DeadLetterQueueName] = [DiningTopology.QueueName],
            [OrdersTopology.DeadLetterQueueName] =
            [
                OrdersTopology.KdsQueueName,
                OrdersTopology.KdsSignalRQueueName,
            ],
            [PaymentsTopology.DeadLetterQueueName] =
            [
                PaymentsTopology.OrdersIntegrationQueueName,
                PaymentsTopology.PayableOrdersQueueName,
                PaymentsTopology.CashRegisterQueueName,
            ],
            [ReportingTopology.DeadLetterQueueName] =
            [
                ReportingTopology.ReadModelQueueName,
                ReportingTopology.SignalRQueueName,
            ],
            [RestaurantOperationsTopology.DeadLetterQueueName] =
            [
                RestaurantOperationsTopology.ReadModelQueueName,
            ],
            [SalesTopology.DeadLetterQueueName] =
            [
                SalesTopology.CheckoutQueueName,
                SalesTopology.ReadModelQueueName,
            ],
        };

    public bool IsKnownQueue(string queue)
    {
        return QueueOrigins.ContainsKey(queue);
    }

    public async Task<IReadOnlyCollection<DeadLetterQueueStatus>> GetStatusAsync(
        CancellationToken cancellationToken
    )
    {
        ConnectionFactory factory = RabbitMqConnectionFactory.Create(options.Value);
        await using IConnection connection = await factory.CreateConnectionAsync(
            "reporting-dead-letter-status",
            cancellationToken
        );
        List<DeadLetterQueueStatus> status = [];

        foreach (string queue in QueueOrigins.Keys.Order(StringComparer.Ordinal))
        {
            await using IChannel channel = await connection.CreateChannelAsync(
                cancellationToken: cancellationToken
            );
            try
            {
                QueueDeclareOk result = await channel.QueueDeclarePassiveAsync(
                    queue,
                    cancellationToken
                );
                status.Add(
                    new DeadLetterQueueStatus(
                        queue,
                        result.MessageCount,
                        result.ConsumerCount,
                        true
                    )
                );
            }
            catch (OperationInterruptedException)
            {
                status.Add(new DeadLetterQueueStatus(queue, 0, 0, false));
            }
        }

        return status;
    }

    public async Task<DeadLetterReplayResult> ReplayAsync(
        string queue,
        int count,
        CancellationToken cancellationToken
    )
    {
        HashSet<string> allowedOrigins = QueueOrigins[queue];
        ConnectionFactory factory = RabbitMqConnectionFactory.Create(options.Value);
        await using IConnection connection = await factory.CreateConnectionAsync(
            "reporting-dead-letter-replay",
            cancellationToken
        );
        await using IChannel channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(true, true),
            cancellationToken
        );

        int replayed = 0;
        uint remaining = 0;
        bool blocked = false;
        while (replayed < count)
        {
            BasicGetResult? message = await channel.BasicGetAsync(
                queue,
                autoAck: false,
                cancellationToken
            );
            if (message is null)
            {
                break;
            }

            remaining = message.MessageCount;
            if (
                !TryGetOriginalQueue(message.BasicProperties.Headers, out string originalQueue)
                || !allowedOrigins.Contains(originalQueue)
            )
            {
                await channel.BasicNackAsync(
                    message.DeliveryTag,
                    multiple: false,
                    requeue: true,
                    cancellationToken
                );
                remaining = message.MessageCount + 1;
                blocked = true;
                break;
            }

            BasicProperties properties = new(message.BasicProperties)
            {
                Expiration = null,
                Headers = message.BasicProperties.Headers is null
                    ? []
                    : new Dictionary<string, object?>(message.BasicProperties.Headers),
            };
            properties.Headers.Remove(RetryCountHeader);
            properties.Headers.Remove(LastErrorHeader);
            properties.Headers.Remove(LastFailedAtHeader);
            properties.Headers[OriginalQueueHeader] = Encoding.UTF8.GetBytes(originalQueue);

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: originalQueue,
                mandatory: true,
                basicProperties: properties,
                body: message.Body,
                cancellationToken: cancellationToken
            );
            await channel.BasicAckAsync(message.DeliveryTag, multiple: false, cancellationToken);
            replayed++;
        }

        return new DeadLetterReplayResult(queue, count, replayed, remaining, blocked);
    }

    private static bool TryGetOriginalQueue(
        IDictionary<string, object?>? headers,
        out string originalQueue
    )
    {
        originalQueue = string.Empty;
        if (headers is null)
        {
            return false;
        }

        if (headers.TryGetValue(OriginalQueueHeader, out object? configured))
        {
            originalQueue = GetString(configured) ?? string.Empty;
        }

        if (
            string.IsNullOrWhiteSpace(originalQueue)
            && headers.TryGetValue("x-first-death-queue", out object? firstDeathQueue)
        )
        {
            originalQueue = GetString(firstDeathQueue) ?? string.Empty;
        }

        return !string.IsNullOrWhiteSpace(originalQueue);
    }

    private static string? GetString(object? value)
    {
        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => null,
        };
    }
}
