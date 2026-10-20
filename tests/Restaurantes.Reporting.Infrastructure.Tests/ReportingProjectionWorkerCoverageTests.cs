using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;
using Restaurantes.Reporting.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.Reporting.Infrastructure.Tests;

public sealed class ReportingProjectionWorkerCoverageTests
{
    [Fact]
    public async Task Valid_order_event_is_projected_and_duplicate_is_acknowledged()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid orderId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        DateTime occurredAt = new(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);
        OrderSubmitted submitted = new(
            orderId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            2,
            Guid.NewGuid(),
            "Mesa 4",
            "Luz",
            "DineIn",
            "Commander",
            "OnAccount",
            3,
            occurredAt.AddMinutes(-5),
            occurredAt,
            [
                new OrderLineSnapshot(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Pasta",
                    12m,
                    2,
                    "",
                    null,
                    "",
                    "CALIENTE",
                    "Caliente"
                ),
            ],
            [
                new OrderKitchenStationSnapshot(
                    "CALIENTE",
                    "Caliente",
                    "Pending",
                    false,
                    1,
                    null,
                    null,
                    null
                ),
            ]
        );

        await fixture.DeliverAsync(messageId, typeof(OrderSubmitted).FullName!, submitted);

        OrderReportingFact projection = await fixture.Db.Orders.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(orderId, projection.OrderId);
        Assert.Equal("Commander", projection.Source);
        Assert.Equal("Submitted", projection.OrderStatus);
        Assert.Equal(24m, projection.Total);
        Assert.Equal(3, projection.Version);
        Assert.Equal(occurredAt, projection.UpdatedAtUtc);
        Assert.Single(fixture.Db.InboxMessages);

        await fixture.DeliverAsync(
            messageId,
            typeof(OrderSubmitted).FullName!,
            submitted with
            {
                Version = 8,
                TableLabel = "No debe aplicarse",
            }
        );

        Assert.Equal(3, projection.Version);
        Assert.Equal("Mesa 4", projection.TableLabel);
        Assert.Single(fixture.Db.InboxMessages);
        Assert.Equal(2, fixture.ChannelProxy.Acknowledgements);
    }

    [Theory]
    [InlineData("invalid-id", "Restaurantes.Orders.Contracts.Events.OrderSubmitted", "{}")]
    [InlineData("{guid}", "unknown.event", "{}")]
    [InlineData("{guid}", "Restaurantes.Orders.Contracts.Events.OrderSubmitted", "null")]
    public async Task Invalid_or_unsupported_events_are_dead_lettered(
        string messageId,
        string eventType,
        string body
    )
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        await fixture.DeliverRawAsync(
            messageId == "{guid}" ? Guid.NewGuid().ToString() : messageId,
            eventType,
            System.Text.Encoding.UTF8.GetBytes(body)
        );

        Assert.Empty(fixture.Db.Orders);
        Assert.Empty(fixture.Db.InboxMessages);
        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
    }

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly ReportingProjectionWorker worker;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            ReportingReadDbContext db,
            ReportingProjectionWorker worker,
            IChannel channel,
            ChannelProxy channelProxy
        )
        {
            this.services = services;
            Db = db;
            this.worker = worker;
            this.channel = channel;
            ChannelProxy = channelProxy;
        }

        public ReportingReadDbContext Db { get; }
        public ChannelProxy ChannelProxy { get; }

        public static async Task<WorkerFixture> CreateAsync()
        {
            ReportingReadDbContext db = new(
                new DbContextOptionsBuilder<ReportingReadDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(db)
                .AddSingleton(TimeProvider.System)
                .AddScoped<ReportingProjectionHandler>()
                .BuildServiceProvider();
            ReportingProjectionWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                NullLogger<ReportingProjectionWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            ChannelProxy proxy = (ChannelProxy)(object)channel;
            typeof(ReportingProjectionWorker)
                .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(worker, channel);
            return new WorkerFixture(services, db, worker, channel, proxy);
        }

        public Task DeliverAsync<TEvent>(Guid messageId, string eventType, TEvent message) =>
            DeliverRawAsync(
                messageId.ToString(),
                eventType,
                JsonSerializer.SerializeToUtf8Bytes(message)
            );

        public async Task DeliverRawAsync(string messageId, string eventType, byte[] body)
        {
            BasicProperties properties = new() { MessageId = messageId, Type = eventType };
            BasicDeliverEventArgs args = new(
                "consumer",
                (ulong)(ChannelProxy.Acknowledgements + ChannelProxy.Rejections + 1),
                false,
                "exchange",
                "routing-key",
                properties,
                body,
                TestContext.Current.CancellationToken
            );
            MethodInfo handle = typeof(ReportingProjectionWorker).GetMethod(
                "HandleMessageAsync",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)handle.Invoke(worker, [channel, args])!;
        }

        public async ValueTask DisposeAsync() => await services.DisposeAsync();
    }

    public class ChannelProxy : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }

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
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
