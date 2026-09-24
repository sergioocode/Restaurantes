using System.Globalization;
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Restaurantes.Messaging.RabbitMq;

public readonly record struct RabbitMqRetryResult(int Attempt, TimeSpan? Delay)
{
    public bool Scheduled => Delay.HasValue;
}

public static class RabbitMqRetry
{
    private const string RetryCountHeader = "x-restaurantes-retry-count";
    private const string OriginalQueueHeader = "x-restaurantes-original-queue";
    private const string LastErrorHeader = "x-restaurantes-last-error";
    private const string LastFailedAtHeader = "x-restaurantes-last-failed-at";

    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
    ];

    public static int MaximumAttempts => Delays.Length;

    public static async Task DeclareAsync(
        IChannel channel,
        string sourceQueue,
        CancellationToken cancellationToken
    )
    {
        for (int index = 0; index < Delays.Length; index++)
        {
            Dictionary<string, object?> arguments = new()
            {
                ["x-message-ttl"] = (long)Delays[index].TotalMilliseconds,
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = sourceQueue,
            };

            await channel.QueueDeclareAsync(
                GetRetryQueueName(sourceQueue, index + 1),
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments,
                cancellationToken: cancellationToken
            );
        }
    }

    public static async Task<RabbitMqRetryResult> ScheduleOrDeadLetterAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        string sourceQueue,
        Exception exception,
        CancellationToken cancellationToken = default
    )
    {
        int completedAttempts = GetRetryCount(delivery.BasicProperties.Headers);
        if (completedAttempts >= Delays.Length)
        {
            await channel.BasicNackAsync(
                delivery.DeliveryTag,
                multiple: false,
                requeue: false,
                cancellationToken
            );
            return new RabbitMqRetryResult(completedAttempts, null);
        }

        int attempt = completedAttempts + 1;
        BasicProperties properties = new(delivery.BasicProperties)
        {
            Expiration = null,
            Headers = delivery.BasicProperties.Headers is null
                ? []
                : new Dictionary<string, object?>(delivery.BasicProperties.Headers),
        };
        properties.Headers[RetryCountHeader] = attempt;
        properties.Headers.TryAdd(OriginalQueueHeader, Encoding.UTF8.GetBytes(sourceQueue));
        properties.Headers[LastErrorHeader] = Encoding.UTF8.GetBytes(
            exception.GetType().FullName ?? exception.GetType().Name
        );
        properties.Headers[LastFailedAtHeader] = Encoding.UTF8.GetBytes(
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        );

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: GetRetryQueueName(sourceQueue, attempt),
            mandatory: true,
            basicProperties: properties,
            body: delivery.Body,
            cancellationToken: cancellationToken
        );
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);

        return new RabbitMqRetryResult(attempt, Delays[completedAttempts]);
    }

    private static string GetRetryQueueName(string sourceQueue, int attempt)
    {
        return $"{sourceQueue}.retry.{attempt}";
    }

    private static int GetRetryCount(IDictionary<string, object?>? headers)
    {
        return headers is null || !headers.TryGetValue(RetryCountHeader, out object? value)
            ? 0
            : value switch
            {
                byte number => number,
                sbyte number => number,
                short number => number,
                ushort number => number,
                int number => number,
                uint number when number <= int.MaxValue => (int)number,
                long number when number is >= 0 and <= int.MaxValue => (int)number,
                byte[] bytes
                    when int.TryParse(
                        Encoding.UTF8.GetString(bytes),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int number
                    ) => number,
                _ => 0,
            };
    }
}
