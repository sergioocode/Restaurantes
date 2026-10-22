using System.Reflection;
using System.Text;
using RabbitMQ.Client;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Reporting.Api.DeadLetters;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class RabbitMqDeadLetterServiceTests
{
    [Fact]
    public void Is_known_queue_accepts_only_registered_dead_letter_queues()
    {
        RabbitMqDeadLetterService service = new(new RecordingConnectionProvider());

        Assert.True(service.IsKnownQueue(CatalogTopology.DeadLetterQueueName));
        Assert.True(service.IsKnownQueue(ReportingTopology.DeadLetterQueueName));
        Assert.False(service.IsKnownQueue("unknown.dead-letter"));
    }

    [Fact]
    public async Task Status_reports_counts_for_every_known_dead_letter_queue()
    {
        RecordingChannel channel = new();
        RecordingConnectionProvider provider = new(channel);
        RabbitMqDeadLetterService service = new(provider);

        IReadOnlyCollection<DeadLetterQueueStatus> status = await service.GetStatusAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(7, status.Count);
        Assert.All(
            status,
            queue =>
            {
                Assert.True(queue.Available);
                Assert.Equal(5u, queue.MessageCount);
                Assert.Equal(2u, queue.ConsumerCount);
            }
        );
        Assert.Equal(
            status.Select(queue => queue.Queue).Order(StringComparer.Ordinal),
            channel.DeclaredQueues
        );
        Assert.Equal("reporting-dead-letter-status", Assert.Single(provider.ConnectionNames));
    }

    [Fact]
    public async Task Replay_stops_when_the_dead_letter_queue_is_empty()
    {
        RecordingChannel channel = new();
        RabbitMqDeadLetterService service = new(new RecordingConnectionProvider(channel));

        DeadLetterReplayResult result = await service.ReplayAsync(
            CatalogTopology.DeadLetterQueueName,
            10,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            new DeadLetterReplayResult(CatalogTopology.DeadLetterQueueName, 10, 0, 0, false),
            result
        );
        Assert.Equal(1, channel.BasicGetCalls);
        Assert.Empty(channel.PublishedMessages);
    }

    [Fact]
    public async Task Replay_requeues_and_blocks_a_message_from_an_unrelated_source_queue()
    {
        RecordingChannel channel = new(
            Message(
                2,
                new Dictionary<string, object?>
                {
                    ["x-restaurantes-original-queue"] = Encoding.UTF8.GetBytes("unrelated.queue"),
                }
            )
        );
        RabbitMqDeadLetterService service = new(new RecordingConnectionProvider(channel));

        DeadLetterReplayResult result = await service.ReplayAsync(
            CatalogTopology.DeadLetterQueueName,
            3,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            new DeadLetterReplayResult(CatalogTopology.DeadLetterQueueName, 3, 0, 3, true),
            result
        );
        Assert.Equal(1, channel.NegativeAcknowledgements);
        Assert.True(channel.Requeue);
        Assert.Empty(channel.PublishedMessages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replay_publishes_allowed_events_and_clears_retry_metadata(
        bool useFallbackHeader
    )
    {
        string origin = CatalogTopology.ReadModelQueueName;
        Dictionary<string, object?> headers = useFallbackHeader
            ? new() { ["x-first-death-queue"] = origin }
            : new() { ["x-restaurantes-original-queue"] = Encoding.UTF8.GetBytes(origin) };
        headers["x-restaurantes-retry-count"] = 2;
        headers["x-restaurantes-last-error"] = "temporary";
        headers["x-restaurantes-last-failed-at"] = "yesterday";
        headers["custom"] = "preserved";
        RecordingChannel channel = new(Message(4, headers));
        RabbitMqDeadLetterService service = new(new RecordingConnectionProvider(channel));

        DeadLetterReplayResult result = await service.ReplayAsync(
            CatalogTopology.DeadLetterQueueName,
            1,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            new DeadLetterReplayResult(CatalogTopology.DeadLetterQueueName, 1, 1, 4, false),
            result
        );
        Assert.Equal(1, channel.Acknowledgements);
        Assert.Equal(0, channel.NegativeAcknowledgements);
        RecordedPublish publish = Assert.Single(channel.PublishedMessages);
        Assert.Equal(origin, publish.RoutingKey);
        Assert.Equal("payload"u8.ToArray(), publish.Body);
        Assert.Null(publish.Properties.Expiration);
        Assert.Equal("preserved", publish.Properties.Headers!["custom"]);
        Assert.Equal(
            Encoding.UTF8.GetBytes(origin),
            (byte[])publish.Properties.Headers["x-restaurantes-original-queue"]!
        );
        Assert.DoesNotContain("x-restaurantes-retry-count", publish.Properties.Headers.Keys);
        Assert.DoesNotContain("x-restaurantes-last-error", publish.Properties.Headers.Keys);
        Assert.DoesNotContain("x-restaurantes-last-failed-at", publish.Properties.Headers.Keys);
    }

    private static BasicGetResult Message(uint remaining, IDictionary<string, object?> headers) =>
        new(
            42,
            false,
            string.Empty,
            "dead-letter",
            remaining,
            new BasicProperties { Headers = headers, Expiration = "1000" },
            "payload"u8.ToArray()
        );

    public sealed class RecordingConnectionProvider(params RecordingChannel[] channels)
        : IRabbitMqConnectionProvider
    {
        private readonly Queue<RecordingChannel> availableChannels = new(channels);

        public List<string> ConnectionNames { get; } = [];

        public Task<IConnection> ConnectAsync(
            string clientProvidedName,
            CancellationToken cancellationToken
        )
        {
            ConnectionNames.Add(clientProvidedName);
            RecordingChannel channel =
                availableChannels.Count == 0 ? new RecordingChannel() : availableChannels.Peek();
            IConnection connection = DispatchProxy.Create<IConnection, ConnectionProxy>();
            ((ConnectionProxy)(object)connection).Channel = channel.Proxy;
            return Task.FromResult(connection);
        }
    }

    public class ConnectionProxy : DispatchProxy
    {
        public IChannel Channel { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(IConnection.CreateChannelAsync) => Task.FromResult(Channel),
                nameof(IConnection.DisposeAsync) => ValueTask.CompletedTask,
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
    }

    public class ChannelProxy : DispatchProxy
    {
        public Func<MethodInfo?, object?[]?, object?> Handler { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod, args);
    }

    public sealed class RecordingChannel
    {
        private readonly Queue<BasicGetResult> messages;

        public IChannel Proxy { get; }
        public List<string> DeclaredQueues { get; } = [];
        public List<RecordedPublish> PublishedMessages { get; } = [];
        public int BasicGetCalls { get; private set; }
        public int Acknowledgements { get; private set; }
        public int NegativeAcknowledgements { get; private set; }
        public bool Requeue { get; private set; }

        public RecordingChannel(params BasicGetResult[] messages)
        {
            this.messages = new Queue<BasicGetResult>(messages);
            Proxy = DispatchProxy.Create<IChannel, ChannelProxy>();
            ((ChannelProxy)(object)Proxy).Handler = Handle;
        }

        private object? Handle(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IChannel.QueueDeclarePassiveAsync))
            {
                string queue = (string)args![0]!;
                DeclaredQueues.Add(queue);
                return Task.FromResult(new QueueDeclareOk(queue, 5, 2));
            }
            if (targetMethod?.Name == nameof(IChannel.BasicGetAsync))
            {
                BasicGetCalls++;
                return Task.FromResult(messages.Count == 0 ? null : messages.Dequeue());
            }
            if (targetMethod?.Name == nameof(IChannel.BasicNackAsync))
            {
                NegativeAcknowledgements++;
                Requeue = (bool)args![2]!;
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.BasicAckAsync))
            {
                Acknowledgements++;
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
            if (targetMethod?.Name == nameof(IChannel.DisposeAsync))
            {
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
}
