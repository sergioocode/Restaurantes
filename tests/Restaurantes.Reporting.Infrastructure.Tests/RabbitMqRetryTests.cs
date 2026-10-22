using System.Reflection;
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class RabbitMqRetryTests
{
    public static TheoryData<string, int> RetryHeaders =>
        new()
        {
            { "missing", 1 },
            { "empty", 1 },
            { "byte", 2 },
            { "sbyte", 2 },
            { "short", 2 },
            { "ushort", 2 },
            { "int", 2 },
            { "uint", 2 },
            { "long", 2 },
            { "byte-array", 2 },
            { "string", 1 },
            { "negative-short", 1 },
            { "negative-int", 1 },
            { "negative-long", 1 },
            { "long-max", 1 },
            { "uint-max", 1 },
            { "invalid-byte-array", 1 },
        };

    [Theory]
    [MemberData(nameof(RetryHeaders))]
    public async Task Retry_header_encodings_schedule_the_correct_attempt(
        string encoding,
        int attempt
    )
    {
        IChannel channel = DispatchProxy.Create<IChannel, RecordingChannel>();
        RecordingChannel recorder = (RecordingChannel)(object)channel;
        Dictionary<string, object?>? headers = encoding switch
        {
            "missing" => null,
            "empty" => [],
            _ => new Dictionary<string, object?>
            {
                ["x-restaurantes-retry-count"] = RetryHeaderValue(encoding),
            },
        };
        BasicProperties properties = new()
        {
            MessageId = Guid.NewGuid().ToString(),
            Headers = headers,
            Expiration = "1000",
        };
        BasicDeliverEventArgs delivery = new(
            "consumer",
            7,
            false,
            "exchange",
            "routing-key",
            properties,
            "payload"u8.ToArray(),
            TestContext.Current.CancellationToken
        );

        RabbitMqRetryResult result = await RabbitMqRetry.ScheduleOrDeadLetterAsync(
            channel,
            delivery,
            "source.queue",
            new InvalidOperationException("temporary failure"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(attempt, result.Attempt);
        Assert.Equal(
            attempt == 1 ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30),
            result.Delay
        );
        Assert.Equal(1, recorder.Acknowledgements);
        Assert.Equal(0, recorder.Rejections);
        RecordedPublish publish = Assert.Single(recorder.PublishedMessages);
        Assert.Equal($"source.queue.retry.{attempt}", publish.RoutingKey);
        Assert.Equal("payload"u8.ToArray(), publish.Body);
        Assert.Null(publish.Properties.Expiration);
        Assert.Equal(attempt, publish.Properties.Headers!["x-restaurantes-retry-count"]);
        Assert.Equal(
            Encoding.UTF8.GetBytes("source.queue"),
            (byte[])publish.Properties.Headers["x-restaurantes-original-queue"]!
        );
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            Encoding.UTF8.GetString(
                (byte[])publish.Properties.Headers["x-restaurantes-last-error"]!
            )
        );
        Assert.True(
            DateTimeOffset.TryParse(
                Encoding.UTF8.GetString(
                    (byte[])publish.Properties.Headers["x-restaurantes-last-failed-at"]!
                ),
                out _
            )
        );
    }

    private static object RetryHeaderValue(string encoding) =>
        encoding switch
        {
            "byte" => (byte)1,
            "sbyte" => (sbyte)1,
            "short" => (short)1,
            "ushort" => (ushort)1,
            "int" => 1,
            "uint" => (uint)1,
            "long" => (long)1,
            "byte-array" => Encoding.UTF8.GetBytes("1"),
            "string" => "1",
            "negative-short" => (short)-1,
            "negative-int" => -1,
            "negative-long" => (long)-1,
            "long-max" => long.MaxValue,
            "uint-max" => uint.MaxValue,
            "invalid-byte-array" => Encoding.UTF8.GetBytes("invalid"),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, null),
        };

    [Fact]
    public async Task Retry_queues_are_durable_and_return_to_the_source_queue_after_their_ttl()
    {
        IChannel channel = DispatchProxy.Create<IChannel, RecordingChannel>();
        RecordingChannel recorder = (RecordingChannel)(object)channel;

        await RabbitMqRetry.DeclareAsync(
            channel,
            "orders.kds",
            TestContext.Current.CancellationToken
        );

        Assert.Collection(
            recorder.DeclaredQueues,
            queue => AssertRetryQueue(queue, 1, TimeSpan.FromSeconds(5)),
            queue => AssertRetryQueue(queue, 2, TimeSpan.FromSeconds(30)),
            queue => AssertRetryQueue(queue, 3, TimeSpan.FromMinutes(2))
        );

        void AssertRetryQueue(DeclaredQueue queue, int attempt, TimeSpan delay)
        {
            Assert.Equal($"orders.kds.retry.{attempt}", queue.Name);
            Assert.True(queue.Durable);
            Assert.False(queue.Exclusive);
            Assert.False(queue.AutoDelete);
            Assert.Equal((long)delay.TotalMilliseconds, queue.Arguments["x-message-ttl"]);
            Assert.Equal(string.Empty, queue.Arguments["x-dead-letter-exchange"]);
            Assert.Equal("orders.kds", queue.Arguments["x-dead-letter-routing-key"]);
        }
    }

    [Fact]
    public async Task Retry_at_or_after_the_limit_is_rejected_without_publishing()
    {
        IChannel channel = DispatchProxy.Create<IChannel, RecordingChannel>();
        RecordingChannel recorder = (RecordingChannel)(object)channel;
        BasicProperties properties = new()
        {
            Headers = new Dictionary<string, object?>
            {
                ["x-restaurantes-retry-count"] = RabbitMqRetry.MaximumAttempts,
            },
        };
        BasicDeliverEventArgs delivery = new(
            "consumer",
            1,
            false,
            "exchange",
            "routing-key",
            properties,
            ReadOnlyMemory<byte>.Empty,
            TestContext.Current.CancellationToken
        );

        RabbitMqRetryResult result = await RabbitMqRetry.ScheduleOrDeadLetterAsync(
            channel,
            delivery,
            "source.queue",
            new InvalidOperationException("permanent failure"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(RabbitMqRetry.MaximumAttempts, result.Attempt);
        Assert.False(result.Scheduled);
        Assert.Empty(recorder.PublishedMessages);
        Assert.Equal(0, recorder.Acknowledgements);
        Assert.Equal(1, recorder.Rejections);
    }

    [Fact]
    public async Task Retry_after_the_third_attempt_is_dead_lettered_without_indexing_a_delay()
    {
        IChannel channel = DispatchProxy.Create<IChannel, RecordingChannel>();
        RecordingChannel recorder = (RecordingChannel)(object)channel;
        BasicProperties properties = new()
        {
            Headers = new Dictionary<string, object?>
            {
                ["x-restaurantes-retry-count"] = RabbitMqRetry.MaximumAttempts + 1,
            },
        };
        BasicDeliverEventArgs delivery = new(
            "consumer",
            2,
            false,
            "exchange",
            "routing-key",
            properties,
            "payload"u8.ToArray(),
            TestContext.Current.CancellationToken
        );

        RabbitMqRetryResult result = await RabbitMqRetry.ScheduleOrDeadLetterAsync(
            channel,
            delivery,
            "source.queue",
            new InvalidOperationException("permanent failure"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(RabbitMqRetry.MaximumAttempts + 1, result.Attempt);
        Assert.False(result.Scheduled);
        Assert.Empty(recorder.PublishedMessages);
        Assert.Equal(0, recorder.Acknowledgements);
        Assert.Equal(1, recorder.Rejections);
    }

    [Fact]
    public async Task Third_retry_preserves_original_headers_and_uses_the_longest_delay()
    {
        IChannel channel = DispatchProxy.Create<IChannel, RecordingChannel>();
        RecordingChannel recorder = (RecordingChannel)(object)channel;
        Dictionary<string, object?> headers = new()
        {
            ["x-restaurantes-retry-count"] = 2,
            ["x-restaurantes-original-queue"] = Encoding.UTF8.GetBytes("first.queue"),
            ["custom"] = "preserved",
        };
        BasicDeliverEventArgs delivery = new(
            "consumer",
            3,
            false,
            "exchange",
            "routing-key",
            new BasicProperties { Headers = headers },
            "payload"u8.ToArray(),
            TestContext.Current.CancellationToken
        );

        RabbitMqRetryResult result = await RabbitMqRetry.ScheduleOrDeadLetterAsync(
            channel,
            delivery,
            "source.queue",
            new TimeoutException("temporary failure"),
            TestContext.Current.CancellationToken
        );

        RecordedPublish publish = Assert.Single(recorder.PublishedMessages);
        Assert.Equal(3, result.Attempt);
        Assert.Equal(TimeSpan.FromMinutes(2), result.Delay);
        Assert.Equal("source.queue.retry.3", publish.RoutingKey);
        Assert.Equal(2, headers["x-restaurantes-retry-count"]);
        Assert.Equal(
            "first.queue",
            Encoding.UTF8.GetString(
                (byte[])publish.Properties.Headers!["x-restaurantes-original-queue"]!
            )
        );
        Assert.Equal("preserved", publish.Properties.Headers["custom"]);
    }

    public class RecordingChannel : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }
        public List<RecordedPublish> PublishedMessages { get; } = [];
        public List<DeclaredQueue> DeclaredQueues { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IChannel.QueueDeclareAsync))
            {
                DeclaredQueues.Add(
                    new DeclaredQueue(
                        (string)args![0]!,
                        (bool)args[1]!,
                        (bool)args[2]!,
                        (bool)args[3]!,
                        (IDictionary<string, object?>)args[4]!
                    )
                );
                Type returnType = targetMethod.ReturnType;
                Type resultType = returnType.GetGenericArguments()[0];
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, [null]);
            }
            if (targetMethod?.Name == nameof(IChannel.BasicAckAsync))
            {
                Acknowledgements++;
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.BasicNackAsync))
            {
                Rejections++;
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.BasicPublishAsync))
            {
                PublishedMessages.Add(
                    new RecordedPublish(
                        (string)args![1]!,
                        ((ReadOnlyMemory<byte>)args[4]!).ToArray(),
                        (BasicProperties)args[3]!
                    )
                );
                return ValueTask.CompletedTask;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public sealed record RecordedPublish(
        string RoutingKey,
        byte[] Body,
        BasicProperties Properties
    );

    public sealed record DeclaredQueue(
        string Name,
        bool Durable,
        bool Exclusive,
        bool AutoDelete,
        IDictionary<string, object?> Arguments
    );
}
