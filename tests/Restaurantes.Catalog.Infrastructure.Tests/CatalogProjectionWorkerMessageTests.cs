using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;
using Restaurantes.Catalog.Infrastructure.Workers;
using Restaurantes.Messaging.RabbitMq;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogProjectionWorkerMessageTests
{
    [Fact]
    public async Task Valid_event_is_projected_persisted_in_inbox_and_acknowledged_once()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid messageId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        CategoryChanged message = Category(categoryId, "Pizzas", 1);

        await fixture.DeliverAsync(
            messageId.ToString(),
            typeof(CategoryChanged).FullName!,
            message
        );

        CategoryReadModel category = await fixture.GetCategoryAsync(categoryId);
        Assert.Equal("Pizzas", category.Name);
        Assert.Equal(1, category.Version);
        Assert.Equal(1, await fixture.GetInboxCountAsync());
        Assert.Equal(1, fixture.Channel.Acknowledgements);
        Assert.Equal(0, fixture.Channel.Rejections);
    }

    [Fact]
    public async Task Duplicate_message_is_acknowledged_without_reapplying_projection()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid messageId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        await fixture.DeliverAsync(
            messageId.ToString(),
            typeof(CategoryChanged).FullName!,
            Category(categoryId, "Original", 1)
        );
        await fixture.DeliverAsync(
            messageId.ToString(),
            typeof(CategoryChanged).FullName!,
            Category(categoryId, "Duplicate", 2)
        );

        CategoryReadModel category = await fixture.GetCategoryAsync(categoryId);
        Assert.Equal("Original", category.Name);
        Assert.Equal(1, category.Version);
        Assert.Equal(1, await fixture.GetInboxCountAsync());
        Assert.Equal(2, fixture.Channel.Acknowledgements);
    }

    [Theory]
    [InlineData("invalid-id", "unknown.event", "{}")]
    [InlineData("", "unknown.event", "{}")]
    [InlineData("7b9d", "unknown.event", "{}")]
    [InlineData(
        "11111111-1111-1111-1111-111111111111",
        "Restaurantes.Catalog.Contracts.Events.CategoryChanged",
        "{"
    )]
    public async Task Invalid_message_is_rejected_without_requeue(
        string messageId,
        string eventType,
        string body
    )
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        await fixture.DeliverRawAsync(
            messageId,
            eventType,
            System.Text.Encoding.UTF8.GetBytes(body)
        );

        Assert.Equal(0, fixture.Channel.Acknowledgements);
        Assert.Equal(1, fixture.Channel.Rejections);
        Assert.False(fixture.Channel.LastRequeue);
        Assert.Equal(0, await fixture.GetInboxCountAsync());
    }

    [Fact]
    public async Task Handler_without_channel_returns_without_projecting_message()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(setChannel: false);

        await fixture.DeliverAsync(
            Guid.NewGuid().ToString(),
            typeof(CategoryChanged).FullName!,
            Category(Guid.NewGuid(), "No procesada", 1)
        );

        Assert.Equal(0, fixture.Channel.Acknowledgements);
        Assert.Equal(0, fixture.Channel.Rejections);
        Assert.Equal(0, await fixture.GetInboxCountAsync());
    }

    [Fact]
    public async Task Projection_storage_failure_is_retried_with_incremented_attempt_metadata()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(failOnSave: true);

        await fixture.DeliverAsync(
            Guid.NewGuid().ToString(),
            typeof(CategoryChanged).FullName!,
            Category(Guid.NewGuid(), "Pendiente", 1)
        );

        Assert.Equal(1, fixture.Channel.Acknowledgements);
        Assert.Equal(0, fixture.Channel.Rejections);
        PublishedRetry retry = Assert.Single(fixture.Channel.PublishedMessages);
        Assert.Equal($"{CatalogTopology.ReadModelQueueName}.retry.1", retry.RoutingKey);
        Assert.Equal(1, retry.Properties.Headers!["x-restaurantes-retry-count"]);
        Assert.Equal(0, await fixture.GetInboxCountAsync());
    }

    [Fact]
    public async Task Storage_failure_at_retry_limit_is_dead_lettered_without_requeue()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(failOnSave: true);
        string messageId = Guid.NewGuid().ToString();
        BasicProperties properties = new()
        {
            MessageId = messageId,
            Type = typeof(CategoryChanged).FullName,
            Headers = new Dictionary<string, object?>
            {
                ["x-restaurantes-retry-count"] = RabbitMqRetry.MaximumAttempts,
            },
        };

        await fixture.DeliverRawAsync(
            messageId,
            properties.Type!,
            JsonSerializer.SerializeToUtf8Bytes(Category(Guid.NewGuid(), "Pendiente", 1)),
            properties
        );

        Assert.Equal(0, fixture.Channel.Acknowledgements);
        Assert.Equal(1, fixture.Channel.Rejections);
        Assert.False(fixture.Channel.LastRequeue);
        Assert.Empty(fixture.Channel.PublishedMessages);
        Assert.Equal(0, await fixture.GetInboxCountAsync());
    }

    private static CategoryChanged Category(Guid id, string name, int version) =>
        new(id, $"CAT-{version}", name, "PASTAS", "Pastas", true, version, DateTime.UtcNow);

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly CatalogProjectionWorker worker;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            CatalogProjectionWorker worker,
            IChannel channel,
            ChannelProxy proxy
        )
        {
            this.services = services;
            this.worker = worker;
            this.channel = channel;
            Channel = proxy;
        }

        public ChannelProxy Channel { get; }

        public static async Task<WorkerFixture> CreateAsync(
            bool setChannel = true,
            bool failOnSave = false
        )
        {
            DbContextOptionsBuilder<CatalogReadDbContext> optionsBuilder =
                new DbContextOptionsBuilder<CatalogReadDbContext>().UseInMemoryDatabase(
                    Guid.NewGuid().ToString()
                );
            if (failOnSave)
            {
                optionsBuilder.AddInterceptors(new ThrowOnSaveChangesInterceptor());
            }
            CatalogReadDbContext db = new(optionsBuilder.Options);
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider();
            CatalogProjectionWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                TimeProvider.System,
                NullLogger<CatalogProjectionWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            if (setChannel)
            {
                typeof(CatalogProjectionWorker)
                    .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(worker, channel);
            }
            return new WorkerFixture(services, worker, channel, (ChannelProxy)(object)channel);
        }

        public Task DeliverAsync<TEvent>(string messageId, string eventType, TEvent message) =>
            DeliverRawAsync(messageId, eventType, JsonSerializer.SerializeToUtf8Bytes(message));

        public async Task DeliverRawAsync(
            string messageId,
            string eventType,
            byte[] body,
            BasicProperties? properties = null
        )
        {
            properties ??= new BasicProperties { MessageId = messageId, Type = eventType };
            BasicDeliverEventArgs args = new(
                "consumer",
                1,
                false,
                "exchange",
                "routing-key",
                properties,
                body,
                TestContext.Current.CancellationToken
            );
            MethodInfo handle = typeof(CatalogProjectionWorker).GetMethod(
                "Handle",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)handle.Invoke(worker, [channel, args])!;
        }

        public async Task<CategoryReadModel> GetCategoryAsync(Guid id)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<CatalogReadDbContext>()
                .Categories.SingleAsync(
                    category => category.Id == id,
                    TestContext.Current.CancellationToken
                );
        }

        public async Task<int> GetInboxCountAsync()
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<CatalogReadDbContext>()
                .InboxMessages.CountAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync() => await services.DisposeAsync();
    }

    public class ChannelProxy : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }
        public bool LastRequeue { get; private set; }
        public List<PublishedRetry> PublishedMessages { get; } = [];

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
                LastRequeue = (bool)args![2]!;
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.BasicPublishAsync))
            {
                PublishedMessages.Add(
                    new PublishedRetry((string)args![1]!, (BasicProperties)args[3]!)
                );
                return ValueTask.CompletedTask;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private sealed class ThrowOnSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Temporary projection storage failure.");
    }

    public sealed record PublishedRetry(string RoutingKey, BasicProperties Properties);
}
