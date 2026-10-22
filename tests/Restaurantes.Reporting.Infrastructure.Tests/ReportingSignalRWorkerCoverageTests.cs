using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Reporting.Api.DeadLetters;
using Restaurantes.Reporting.Api.Realtime;
using Restaurantes.Reporting.Contracts.Events;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class ReportingSignalRWorkerCoverageTests
{
    [Fact]
    public async Task Valid_projection_event_is_sent_to_its_restaurant_group_and_acknowledged()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        DateTime updatedAt = DateTime.UtcNow;
        TrackingClients clients = new();
        IHubContext<ReportingHub> hub = CreateHubContext(clients);
        IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
        ChannelProxy recorder = (ChannelProxy)(object)channel;
        ReportingSignalRWorker worker = CreateWorker(hub);
        SetChannel(worker, channel);

        await HandleAsync(
            worker,
            channel,
            typeof(DashboardProjectionUpdated).FullName!,
            JsonSerializer.Serialize(
                new DashboardProjectionUpdated(orderId, restaurantId, updatedAt)
            )
        );

        Assert.Equal(1, recorder.Acknowledgements);
        Assert.Equal(0, recorder.Rejections);
        Assert.Equal(ReportingHub.GroupName(restaurantId), clients.GroupName);
        Assert.Equal("DashboardUpdated", clients.Method);
        object payload = Assert.Single(clients.Arguments)!;
        Assert.Equal(orderId, payload.GetType().GetProperty("OrderId")!.GetValue(payload));
        Assert.Equal(
            restaurantId,
            payload.GetType().GetProperty("RestaurantId")!.GetValue(payload)
        );
        Assert.Equal(
            updatedAt,
            payload.GetType().GetProperty("ProjectionUpdatedAtUtc")!.GetValue(payload)
        );
    }

    [Theory]
    [InlineData("Unsupported.Event", "{}")]
    [InlineData("Restaurantes.Reporting.Contracts.Events.DashboardProjectionUpdated", "{")]
    public async Task Invalid_projection_events_are_rejected_without_requeue(
        string eventType,
        string body
    )
    {
        IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
        ChannelProxy recorder = (ChannelProxy)(object)channel;
        ReportingSignalRWorker worker = CreateWorker(CreateHubContext(new TrackingClients()));
        SetChannel(worker, channel);

        await HandleAsync(worker, channel, eventType, body);

        Assert.Equal(0, recorder.Acknowledgements);
        Assert.Equal(1, recorder.Rejections);
        Assert.False(recorder.Requeue);
    }

    [Fact]
    public async Task Hub_delivery_failure_is_scheduled_for_retry()
    {
        IHubContext<ReportingHub> hub = CreateHubContext(
            new TrackingClients
            {
                SendFailure = new InvalidOperationException("temporary SignalR failure"),
            }
        );
        IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
        ChannelProxy recorder = (ChannelProxy)(object)channel;
        ReportingSignalRWorker worker = CreateWorker(hub);
        SetChannel(worker, channel);
        string body = JsonSerializer.Serialize(
            new DashboardProjectionUpdated(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow)
        );

        await HandleAsync(worker, channel, typeof(DashboardProjectionUpdated).FullName!, body);

        RecordedPublish publish = Assert.Single(recorder.PublishedMessages);
        Assert.Equal(1, recorder.Acknowledgements);
        Assert.Equal(0, recorder.Rejections);
        Assert.Equal($"{ReportingTopology.SignalRQueueName}.retry.1", publish.RoutingKey);
    }

    private static ReportingSignalRWorker CreateWorker(IHubContext<ReportingHub> hub) =>
        new(
            hub,
            Options.Create(new RabbitMqOptions()),
            NullLogger<ReportingSignalRWorker>.Instance
        );

    private static IHubContext<ReportingHub> CreateHubContext(TrackingClients clients)
    {
        IHubClients hubClients = DispatchProxy.Create<IHubClients, HubClientsProxy>();
        ((HubClientsProxy)(object)hubClients).Tracking = clients;
        IHubContext<ReportingHub> context = DispatchProxy.Create<
            IHubContext<ReportingHub>,
            HubContextProxy
        >();
        ((HubContextProxy)(object)context).Clients = hubClients;
        return context;
    }

    private static void SetChannel(ReportingSignalRWorker worker, IChannel channel) =>
        typeof(ReportingSignalRWorker)
            .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(worker, channel);

    private static async Task HandleAsync(
        ReportingSignalRWorker worker,
        IChannel channel,
        string eventType,
        string body
    )
    {
        BasicDeliverEventArgs delivery = new(
            "consumer",
            23,
            false,
            "exchange",
            "routing-key",
            new BasicProperties { Type = eventType },
            Encoding.UTF8.GetBytes(body),
            TestContext.Current.CancellationToken
        );
        MethodInfo handle = typeof(ReportingSignalRWorker).GetMethod(
            "Handle",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await (Task)handle.Invoke(worker, [channel, delivery])!;
    }

    public class HubContextProxy : DispatchProxy
    {
        public IHubClients Clients { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                "get_Clients" => Clients,
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
    }

    public class ChannelProxy : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }
        public bool Requeue { get; private set; }
        public List<RecordedPublish> PublishedMessages { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IChannel.BasicAckAsync))
            {
                Acknowledgements++;
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.BasicNackAsync))
            {
                Rejections++;
                Requeue = (bool)args![2]!;
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

    public sealed class TrackingClients
    {
        public string? GroupName { get; set; }
        public string? Method { get; private set; }
        public object?[] Arguments { get; private set; } = [];
        public Exception? SendFailure { get; init; }
        public IClientProxy Client { get; }

        public TrackingClients()
        {
            Client = DispatchProxy.Create<IClientProxy, ClientProxy>();
            ((ClientProxy)(object)Client).Tracking = this;
        }

        public void RecordSend(string method, object?[] arguments)
        {
            Method = method;
            Arguments = arguments;
            if (SendFailure is not null)
            {
                throw SendFailure;
            }
        }
    }

    private class ClientProxy : DispatchProxy
    {
        public TrackingClients Tracking { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IClientProxy.SendCoreAsync))
            {
                Tracking.RecordSend((string)args![0]!, (object?[])args[1]!);
                return Task.CompletedTask;
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public class HubClientsProxy : DispatchProxy
    {
        public TrackingClients Tracking { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                "Group" => SetGroup((string)args![0]!),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };

        private IClientProxy SetGroup(string groupName)
        {
            Tracking.GroupName = groupName;
            return Tracking.Client;
        }
    }
}
