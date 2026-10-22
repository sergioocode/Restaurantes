using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Api.Realtime;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Security;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrdersSignalRWorkerCoverageTests
{
    [Fact]
    public async Task Kds_hub_scopes_station_groups_and_rejects_invalid_or_unauthorized_joins()
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal kdsUser = new(
            new ClaimsIdentity(
                [
                    new Claim(
                        RestaurantClaimTypes.Permission,
                        RestaurantSecurityExtensions.ScopedPermission(
                            restaurantId,
                            RestaurantPermissions.KdsUse
                        )
                    ),
                ],
                "test"
            )
        );
        IGroupManager groupManager = DispatchProxy.Create<IGroupManager, GroupManagerProxy>();
        GroupManagerProxy groups = (GroupManagerProxy)(object)groupManager;
        KdsHub hub = new() { Context = new TestHubCallerContext(kdsUser), Groups = groupManager };

        await hub.JoinRestaurant(restaurantId);
        await hub.JoinStation(restaurantId, "  bebidas ");
        await hub.LeaveStation(restaurantId, " bebidas ");
        await hub.LeaveRestaurant(restaurantId);

        Assert.Equal(
            [
                ("Add", "connection", KdsHub.StationGroup(restaurantId, "CHEF")),
                ("Add", "connection", KdsHub.StationGroup(restaurantId, "BEBIDAS")),
                ("Remove", "connection", KdsHub.StationGroup(restaurantId, "BEBIDAS")),
                ("Remove", "connection", KdsHub.StationGroup(restaurantId, "CHEF")),
            ],
            groups.Calls
        );

        await Assert.ThrowsAsync<HubException>(() => hub.JoinRestaurant(Guid.Empty));
        await Assert.ThrowsAsync<HubException>(() => hub.JoinStation(restaurantId, "  "));
        hub.Context = new TestHubCallerContext(
            new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(
                            RestaurantClaimTypes.Permission,
                            RestaurantSecurityExtensions.ScopedPermission(
                                restaurantId,
                                RestaurantPermissions.PosUse
                            )
                        ),
                    ],
                    "test"
                )
            )
        );
        await Assert.ThrowsAsync<HubException>(() => hub.JoinStation(restaurantId, "CHEF"));
        hub.Context = new TestHubCallerContext(null);
        await Assert.ThrowsAsync<HubException>(() => hub.JoinStation(restaurantId, "CHEF"));

        Assert.Equal(
            $"restaurant:{restaurantId:N}:station:COLD BAR",
            KdsHub.StationGroup(restaurantId, " cold bar ")
        );
    }

    [Fact]
    public async Task Orders_hub_requires_scoped_permissions_and_notifies_both_client_groups()
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal user = new(
            new ClaimsIdentity(
                [
                    new Claim(
                        RestaurantClaimTypes.Permission,
                        RestaurantSecurityExtensions.ScopedPermission(
                            restaurantId,
                            RestaurantPermissions.PosUse
                        )
                    ),
                ],
                "test"
            )
        );
        TrackingClients clients = new();
        IGroupManager groupManager = DispatchProxy.Create<IGroupManager, GroupManagerProxy>();
        GroupManagerProxy groups = (GroupManagerProxy)(object)groupManager;
        OrdersHub hub = new()
        {
            Context = new TestHubCallerContext(user),
            Clients = CreateHubCallerClients(clients),
            Groups = groupManager,
        };

        await hub.JoinPos(restaurantId);
        await hub.LeavePos(restaurantId);
        await Assert.ThrowsAsync<HubException>(() => hub.JoinCommander(restaurantId));
        await Assert.ThrowsAsync<HubException>(() => hub.JoinPos(Guid.Empty));
        await Assert.ThrowsAsync<HubException>(() => hub.NotifyDiningTableUpdated(Guid.Empty));

        await hub.NotifyDiningTableUpdated(restaurantId);
        hub.Context = new TestHubCallerContext(
            new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(
                            RestaurantClaimTypes.Permission,
                            RestaurantSecurityExtensions.ScopedPermission(
                                restaurantId,
                                RestaurantPermissions.CommanderUse
                            )
                        ),
                    ],
                    "test"
                )
            )
        );
        await hub.JoinCommander(restaurantId);
        await hub.LeaveCommander(restaurantId);
        await hub.NotifyDiningTableUpdated(restaurantId);

        OrdersHub anonymousHub = new()
        {
            Context = new TestHubCallerContext(null),
            Clients = CreateHubCallerClients(clients),
            Groups = groupManager,
        };
        await Assert.ThrowsAsync<HubException>(() => anonymousHub.JoinPos(restaurantId));
        await Assert.ThrowsAsync<HubException>(() =>
            anonymousHub.NotifyDiningTableUpdated(restaurantId)
        );

        Assert.Equal(
            [
                ("Add", "connection", OrdersHub.PosGroup(restaurantId)),
                ("Remove", "connection", OrdersHub.PosGroup(restaurantId)),
                ("Add", "connection", OrdersHub.CommanderGroup(restaurantId)),
                ("Remove", "connection", OrdersHub.CommanderGroup(restaurantId)),
            ],
            groups.Calls
        );
        Assert.Equal(
            Enumerable
                .Repeat(
                    new[]
                    {
                        OrdersHub.CommanderGroup(restaurantId),
                        OrdersHub.PosGroup(restaurantId),
                    },
                    2
                )
                .SelectMany(groups => groups),
            clients.RecordedGroups.TakeLast(4)
        );
        Assert.Equal(Enumerable.Repeat(restaurantId, 4), clients.DiningTableUpdates);
    }

    [Theory]
    [InlineData("Pos", true, false)]
    [InlineData("Comandero", false, true)]
    [InlineData("CustomerQr", true, false)]
    public async Task Kds_notification_reaches_station_and_only_matching_client_group(
        string source,
        bool expectPos,
        bool expectCommander
    )
    {
        Guid restaurantId = Guid.NewGuid();
        KdsOrderUpdated notification = new(
            Guid.NewGuid(),
            restaurantId,
            source,
            "Submitted",
            2,
            DateTime.UtcNow,
            ["BEBIDAS", "CHEF"]
        );
        TrackingClients clients = new();
        KdsSignalRWorker worker = CreateKdsWorker(clients, out ChannelProxy channel);

        await DeliverAsync(worker, "_channel", channel, typeof(KdsOrderUpdated), notification);

        Assert.Equal(1, channel.Acknowledgements);
        Assert.Equal(0, channel.Rejections);
        List<string> expectedGroups =
        [
            KdsHub.StationGroup(restaurantId, "CHEF"),
            KdsHub.StationGroup(restaurantId, "BEBIDAS"),
        ];
        if (expectPos)
        {
            expectedGroups.Add(OrdersHub.PosGroup(restaurantId));
        }
        if (expectCommander)
        {
            expectedGroups.Add(OrdersHub.CommanderGroup(restaurantId));
        }
        Assert.Equal(expectedGroups, clients.RecordedGroups);
        Assert.Equal(
            2 + Convert.ToInt32(expectPos) + Convert.ToInt32(expectCommander),
            clients.Notifications.Count
        );
        Assert.All(
            clients.Notifications,
            delivered =>
            {
                Assert.Equal(notification.OrderId, delivered.OrderId);
                Assert.Equal(notification.RestaurantId, delivered.RestaurantId);
                Assert.Equal(notification.Source, delivered.Source);
                Assert.Equal(notification.Status, delivered.Status);
                Assert.Equal(notification.Version, delivered.Version);
                Assert.Equal(notification.StationCodes, delivered.StationCodes);
            }
        );
    }

    [Fact]
    public async Task Kds_notification_with_an_unknown_type_is_dead_lettered()
    {
        TrackingClients clients = new();
        KdsSignalRWorker worker = CreateKdsWorker(clients, out ChannelProxy channel);

        await DeliverRawAsync(worker, "_channel", channel, "unknown.event", "{}");

        Assert.Equal(0, channel.Acknowledgements);
        Assert.Equal(1, channel.Rejections);
        Assert.Empty(clients.Notifications);
    }

    [Fact]
    public async Task Kds_notification_without_a_channel_is_ignored()
    {
        TrackingClients clients = new();
        KdsSignalRWorker worker = CreateKdsWorker(clients, out ChannelProxy channel);

        await DeliverRawAsync(
            worker,
            "_channel",
            channel,
            "unknown.event",
            "{}",
            assignChannel: false
        );

        Assert.Equal(0, channel.Acknowledgements);
        Assert.Equal(0, channel.Rejections);
    }

    [Fact]
    public async Task Kds_worker_disposal_closes_and_clears_its_channel()
    {
        KdsSignalRWorker worker = CreateKdsWorker(new TrackingClients(), out ChannelProxy channel);
        SetChannel(worker, "_channel", channel);

        await DisposeWorkerResourcesAsync(worker);

        Assert.Equal(1, channel.Disposals);
        Assert.Null(GetChannel(worker, "_channel"));
    }

    [Fact]
    public async Task Kds_delivery_failure_schedules_a_retry_copy()
    {
        TrackingClients clients = new() { FailOrderUpdated = true };
        KdsSignalRWorker worker = CreateKdsWorker(clients, out ChannelProxy channel);
        KdsOrderUpdated notification = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Pos",
            "Ready",
            2,
            DateTime.UtcNow,
            ["COCINA"]
        );

        await DeliverAsync(worker, "_channel", channel, typeof(KdsOrderUpdated), notification);

        Assert.Equal(1, channel.Acknowledgements);
        Assert.Equal(0, channel.Rejections);
        Assert.Equal(1, channel.RetryPublications);
    }

    [Fact]
    public async Task Kds_delivery_failure_at_retry_limit_is_dead_lettered()
    {
        TrackingClients clients = new() { FailOrderUpdated = true };
        KdsSignalRWorker worker = CreateKdsWorker(clients, out ChannelProxy channel);
        KdsOrderUpdated notification = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Pos",
            "Ready",
            2,
            DateTime.UtcNow,
            ["COCINA"]
        );

        await DeliverRawAsync(
            worker,
            "_channel",
            channel,
            typeof(KdsOrderUpdated).FullName!,
            JsonSerializer.Serialize(notification),
            RabbitMqRetry.MaximumAttempts
        );

        Assert.Equal(0, channel.Acknowledgements);
        Assert.Equal(1, channel.Rejections);
        Assert.Equal(0, channel.RetryPublications);
    }

    [Fact]
    public async Task Sale_ticket_notifies_both_pos_and_commander_groups()
    {
        Guid restaurantId = Guid.NewGuid();
        Guid saleId = Guid.NewGuid();
        TrackingClients clients = new();
        SaleTicketSignalRWorker worker = CreateSaleWorker(clients, out ChannelProxy channel);
        SaleCompleted sale = new(saleId, restaurantId, "Pos", "Cash", 18m, DateTime.UtcNow, [], []);

        await DeliverAsync(worker, "channel", channel, typeof(SaleCompleted), sale);

        Assert.Equal(1, channel.Acknowledgements);
        Assert.Equal(0, channel.Rejections);
        Assert.Equal(
            [OrdersHub.PosGroup(restaurantId), OrdersHub.CommanderGroup(restaurantId)],
            clients.RecordedGroups
        );
        Assert.Equal([saleId, saleId], clients.SaleIds);
    }

    [Fact]
    public async Task Sale_ticket_with_invalid_json_is_dead_lettered()
    {
        TrackingClients clients = new();
        SaleTicketSignalRWorker worker = CreateSaleWorker(clients, out ChannelProxy channel);

        await DeliverRawAsync(worker, "channel", channel, typeof(SaleCompleted).FullName!, "null");

        Assert.Equal(0, channel.Acknowledgements);
        Assert.Equal(1, channel.Rejections);
        Assert.Empty(clients.SaleIds);
    }

    [Fact]
    public async Task Sale_ticket_with_an_unknown_type_is_dead_lettered()
    {
        TrackingClients clients = new();
        SaleTicketSignalRWorker worker = CreateSaleWorker(clients, out ChannelProxy channel);

        await DeliverRawAsync(worker, "channel", channel, "unknown.event", "{}");

        Assert.Equal(0, channel.Acknowledgements);
        Assert.Equal(1, channel.Rejections);
        Assert.Empty(clients.SaleIds);
    }

    [Fact]
    public async Task Sale_ticket_without_a_channel_is_ignored()
    {
        TrackingClients clients = new();
        SaleTicketSignalRWorker worker = CreateSaleWorker(clients, out ChannelProxy channel);

        await DeliverRawAsync(
            worker,
            "channel",
            channel,
            "unknown.event",
            "{}",
            assignChannel: false
        );

        Assert.Equal(0, channel.Acknowledgements);
        Assert.Equal(0, channel.Rejections);
        Assert.Empty(clients.SaleIds);
    }

    [Fact]
    public async Task Sale_ticket_worker_disposal_closes_and_clears_its_channel()
    {
        SaleTicketSignalRWorker worker = CreateSaleWorker(
            new TrackingClients(),
            out ChannelProxy channel
        );
        SetChannel(worker, "channel", channel);

        await DisposeWorkerResourcesAsync(worker);

        Assert.Equal(1, channel.Disposals);
        Assert.Null(GetChannel(worker, "channel"));
    }

    [Fact]
    public async Task Sale_ticket_delivery_failure_schedules_a_retry_copy()
    {
        TrackingClients clients = new() { FailSaleCompleted = true };
        SaleTicketSignalRWorker worker = CreateSaleWorker(clients, out ChannelProxy channel);
        SaleCompleted sale = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Pos",
            "Cash",
            18m,
            DateTime.UtcNow,
            [],
            []
        );

        await DeliverAsync(worker, "channel", channel, typeof(SaleCompleted), sale);

        Assert.Equal(1, channel.Acknowledgements);
        Assert.Equal(0, channel.Rejections);
        Assert.Equal(1, channel.RetryPublications);
    }

    [Fact]
    public async Task Sale_ticket_delivery_failure_at_retry_limit_is_dead_lettered()
    {
        TrackingClients clients = new() { FailSaleCompleted = true };
        SaleTicketSignalRWorker worker = CreateSaleWorker(clients, out ChannelProxy channel);
        SaleCompleted sale = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Pos",
            "Cash",
            18m,
            DateTime.UtcNow,
            [],
            []
        );

        await DeliverRawAsync(
            worker,
            "channel",
            channel,
            typeof(SaleCompleted).FullName!,
            JsonSerializer.Serialize(sale),
            RabbitMqRetry.MaximumAttempts
        );

        Assert.Equal(0, channel.Acknowledgements);
        Assert.Equal(1, channel.Rejections);
        Assert.Equal(0, channel.RetryPublications);
    }

    private static KdsSignalRWorker CreateKdsWorker(
        TrackingClients clients,
        out ChannelProxy channel
    )
    {
        IHubContext<KdsHub, IKdsClient> kdsContext = CreateHubContext<KdsHub>(clients);
        IHubContext<OrdersHub, IKdsClient> ordersContext = CreateHubContext<OrdersHub>(clients);
        KdsSignalRWorker worker = new(
            kdsContext,
            ordersContext,
            Options.Create(new RabbitMqOptions()),
            NullLogger<KdsSignalRWorker>.Instance
        );
        channel = CreateChannel();
        return worker;
    }

    private static SaleTicketSignalRWorker CreateSaleWorker(
        TrackingClients clients,
        out ChannelProxy channel
    )
    {
        SaleTicketSignalRWorker worker = new(
            CreateHubContext<OrdersHub>(clients),
            Options.Create(new RabbitMqOptions()),
            NullLogger<SaleTicketSignalRWorker>.Instance
        );
        channel = CreateChannel();
        return worker;
    }

    private static IHubContext<T, IKdsClient> CreateHubContext<T>(TrackingClients trackingClients)
        where T : Hub<IKdsClient>
    {
        IHubContext<T, IKdsClient> context = DispatchProxy.Create<
            IHubContext<T, IKdsClient>,
            HubContextProxy<T>
        >();
        ((HubContextProxy<T>)(object)context).Clients = trackingClients;
        return context;
    }

    private static ChannelProxy CreateChannel()
    {
        IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
        return (ChannelProxy)(object)channel;
    }

    private static Task DeliverAsync<TEvent>(
        object worker,
        string channelField,
        ChannelProxy channel,
        Type eventType,
        TEvent message
    ) =>
        DeliverRawAsync(
            worker,
            channelField,
            channel,
            eventType.FullName!,
            JsonSerializer.Serialize(message, eventType)
        );

    private static async Task DeliverRawAsync(
        object worker,
        string channelField,
        ChannelProxy channel,
        string eventType,
        string body,
        int retryCount = 0,
        bool assignChannel = true
    )
    {
        Type workerType = worker.GetType();
        FieldInfo field = workerType.GetField(
            channelField,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        if (assignChannel)
        {
            field.SetValue(worker, channel);
        }
        BasicProperties properties = new()
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = eventType,
            Headers =
                retryCount == 0
                    ? null
                    : new Dictionary<string, object?>
                    {
                        ["x-restaurantes-retry-count"] = retryCount,
                    },
        };
        BasicDeliverEventArgs args = new(
            "consumer",
            1,
            false,
            "exchange",
            "routing-key",
            properties,
            System.Text.Encoding.UTF8.GetBytes(body),
            TestContext.Current.CancellationToken
        );
        MethodInfo handler = workerType.GetMethod(
            "HandleMessageAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await (Task)handler.Invoke(worker, [worker, args])!;
    }

    private static void SetChannel(object worker, string fieldName, ChannelProxy channel) =>
        GetChannelField(worker, fieldName).SetValue(worker, channel);

    private static object? GetChannel(object worker, string fieldName) =>
        GetChannelField(worker, fieldName).GetValue(worker);

    private static FieldInfo GetChannelField(object worker, string fieldName) =>
        worker.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static async Task DisposeWorkerResourcesAsync(object worker)
    {
        MethodInfo dispose = worker
            .GetType()
            .GetMethod("DisposeResourcesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)dispose.Invoke(worker, null)!;
    }

    public class HubContextProxy<T> : DispatchProxy
        where T : Hub<IKdsClient>
    {
        public IHubClients<IKdsClient> Clients { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                "get_Clients" => Clients,
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
    }

    private sealed class TestHubCallerContext(ClaimsPrincipal? user) : HubCallerContext
    {
        public override string ConnectionId => "connection";
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => user;
        public override IDictionary<object, object?> Items { get; } =
            new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort() { }
    }

    public class GroupManagerProxy : DispatchProxy
    {
        public List<(string Action, string ConnectionId, string Group)> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (
                targetMethod?.Name
                is nameof(IGroupManager.AddToGroupAsync)
                    or nameof(IGroupManager.RemoveFromGroupAsync)
            )
            {
                string action =
                    targetMethod.Name == nameof(IGroupManager.AddToGroupAsync) ? "Add" : "Remove";
                Calls.Add((action, (string)args![0]!, (string)args[1]!));
                return Task.CompletedTask;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private static IHubCallerClients<IKdsClient> CreateHubCallerClients(TrackingClients clients)
    {
        IHubCallerClients<IKdsClient> hubClients = DispatchProxy.Create<
            IHubCallerClients<IKdsClient>,
            HubCallerClientsProxy
        >();
        ((HubCallerClientsProxy)(object)hubClients).Clients = clients;
        return hubClients;
    }

    public class HubCallerClientsProxy : DispatchProxy
    {
        public TrackingClients Clients { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                "Group" => Clients.Group((string)args![0]!),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
    }

    public sealed class TrackingClients : IHubClients<IKdsClient>, IKdsClient
    {
        public List<string> RecordedGroups { get; } = [];
        public List<KdsOrderUpdated> Notifications { get; } = [];
        public List<Guid> SaleIds { get; } = [];
        public List<Guid> DiningTableUpdates { get; } = [];
        public bool FailOrderUpdated { get; init; }
        public bool FailSaleCompleted { get; init; }
        public IKdsClient All => this;

        public IKdsClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => this;

        public IKdsClient Client(string connectionId) => this;

        public IKdsClient Clients(IReadOnlyList<string> connectionIds) => this;

        public IKdsClient Group(string groupName)
        {
            RecordedGroups.Add(groupName);
            return this;
        }

        public IKdsClient GroupExcept(
            string groupName,
            IReadOnlyList<string> excludedConnectionIds
        ) => this;

        public IKdsClient Groups(IReadOnlyList<string> groupNames) => this;

        public IKdsClient User(string userId) => this;

        public IKdsClient Users(IReadOnlyList<string> userIds) => this;

        public Task OrderUpdated(KdsOrderUpdated notification)
        {
            Notifications.Add(notification);
            if (FailOrderUpdated)
            {
                throw new InvalidOperationException("SignalR group is unavailable.");
            }
            return Task.CompletedTask;
        }

        public Task DiningTableUpdated(Guid restaurantId)
        {
            DiningTableUpdates.Add(restaurantId);
            return Task.CompletedTask;
        }

        public Task SaleCompleted(Guid saleId)
        {
            SaleIds.Add(saleId);
            if (FailSaleCompleted)
            {
                throw new InvalidOperationException("SignalR group is unavailable.");
            }
            return Task.CompletedTask;
        }
    }

    public class ChannelProxy : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }
        public int RetryPublications { get; private set; }
        public int Disposals { get; private set; }

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
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.BasicPublishAsync))
            {
                RetryPublications++;
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.DisposeAsync))
            {
                Disposals++;
                return ValueTask.CompletedTask;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
