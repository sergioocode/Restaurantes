using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.RestaurantOperations.Contracts.Events;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read;
using Restaurantes.RestaurantOperations.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class RestaurantProjectionWorkerCoverageTests
{
    [Fact]
    public async Task Restaurant_projection_applies_newer_versions_and_ignores_stale_versions()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid restaurantId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();

        await fixture.DeliverAsync(
            Guid.NewGuid(),
            new RestaurantChanged(
                restaurantId,
                "R-1",
                "Restaurante inicial",
                "Calle 1",
                null,
                true,
                1,
                DateTime.SpecifyKind(new DateTime(2026, 10, 22, 12, 0, 0), DateTimeKind.Unspecified)
            )
        );
        await fixture.DeliverAsync(
            Guid.NewGuid(),
            new RestaurantChanged(
                restaurantId,
                "R-2",
                "Restaurante actualizado",
                "Calle 2",
                companyId,
                false,
                2,
                DateTime.UtcNow
            )
        );
        await fixture.DeliverAsync(
            Guid.NewGuid(),
            new RestaurantChanged(
                restaurantId,
                "R-old",
                "Versión antigua",
                "Calle antigua",
                null,
                true,
                1,
                DateTime.UtcNow.AddDays(-1)
            )
        );

        RestaurantReadModel restaurant = await fixture.GetRestaurantAsync(restaurantId);
        Assert.Equal("R-2", restaurant.Code);
        Assert.Equal("Restaurante actualizado", restaurant.Name);
        Assert.Equal("Calle 2", restaurant.Address);
        Assert.Equal(companyId, restaurant.CompanyId);
        Assert.False(restaurant.IsActive);
        Assert.Equal(2, restaurant.Version);
        Assert.Equal(DateTimeKind.Utc, restaurant.UpdatedAtUtc.Kind);
        Assert.Equal(3, await fixture.GetInboxCountAsync());
        Assert.Equal(3, fixture.Channel.Acknowledgements);
    }

    [Fact]
    public async Task Company_projection_creates_then_applies_newer_version()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid companyId = Guid.NewGuid();

        await fixture.DeliverAsync(
            Guid.NewGuid(),
            new CompanyChanged(
                companyId,
                "Empresa inicial",
                "A-123",
                "Calle 1",
                "600000000",
                "old@example.test",
                1,
                DateTime.UtcNow
            )
        );
        await fixture.DeliverAsync(
            Guid.NewGuid(),
            new CompanyChanged(
                companyId,
                "Empresa actualizada",
                "B-456",
                "Calle 2",
                null,
                "new@example.test",
                2,
                DateTime.UtcNow
            )
        );
        await fixture.DeliverAsync(
            Guid.NewGuid(),
            new CompanyChanged(
                companyId,
                "Versión antigua",
                "OLD-0",
                "Dirección antigua",
                "600000000",
                "old@example.test",
                1,
                DateTime.UtcNow.AddDays(-1)
            )
        );

        CompanyReadModel company = await fixture.GetCompanyAsync(companyId);
        Assert.Equal("Empresa actualizada", company.LegalName);
        Assert.Equal("B-456", company.TaxId);
        Assert.Equal("Calle 2", company.FiscalAddress);
        Assert.Null(company.Phone);
        Assert.Equal("new@example.test", company.Email);
        Assert.Equal(2, company.Version);
        Assert.Equal(3, fixture.Channel.Acknowledgements);
    }

    [Fact]
    public async Task Duplicate_message_is_acknowledged_without_reapplying_projection()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        Guid messageId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        RestaurantChanged original = new(
            restaurantId,
            "R-1",
            "Original",
            "Calle 1",
            null,
            true,
            1,
            DateTime.UtcNow
        );
        await fixture.DeliverAsync(messageId, original);
        await fixture.DeliverAsync(
            messageId,
            original with
            {
                Name = "Duplicado no aplicado",
                Version = 99,
            }
        );

        RestaurantReadModel restaurant = await fixture.GetRestaurantAsync(restaurantId);
        Assert.Equal("Original", restaurant.Name);
        Assert.Equal(1, restaurant.Version);
        Assert.Equal(1, await fixture.GetInboxCountAsync());
        Assert.Equal(2, fixture.Channel.Acknowledgements);
    }

    [Theory]
    [InlineData("not-a-guid", "unknown.event", "{}")]
    [InlineData("", "unknown.event", "{}")]
    [InlineData("8f3c", "unknown.event", "{}")]
    public async Task Invalid_identity_or_event_is_dead_lettered(
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

        Assert.Equal(1, fixture.Channel.Rejections);
        Assert.Equal(0, fixture.Channel.Acknowledgements);
        Assert.False(fixture.Channel.LastRequeue);
    }

    [Fact]
    public async Task Malformed_event_body_is_dead_lettered()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        await fixture.DeliverRawAsync(
            Guid.NewGuid().ToString(),
            typeof(RestaurantChanged).FullName!,
            "{"u8.ToArray()
        );

        Assert.Equal(1, fixture.Channel.Rejections);
        Assert.False(fixture.Channel.LastRequeue);
        Assert.Empty(await fixture.GetRestaurantsAsync());
    }

    [Fact]
    public async Task Unsupported_event_with_valid_message_id_is_dead_lettered()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        await fixture.DeliverRawAsync(
            Guid.NewGuid().ToString(),
            "unsupported.event",
            "{}"u8.ToArray()
        );

        Assert.Equal(1, fixture.Channel.Rejections);
        Assert.False(fixture.Channel.LastRequeue);
        Assert.Equal(0, fixture.Channel.Acknowledgements);
    }

    [Fact]
    public async Task Disposing_worker_closes_and_clears_the_channel()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();

        await fixture.DisposeWorkerResourcesAsync();

        Assert.Equal(1, fixture.Channel.Disposals);
        Assert.Null(fixture.WorkerChannel);
    }

    [Fact]
    public async Task Handler_without_channel_returns_without_processing_message()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(assignChannel: false);

        await fixture.DeliverAsync(
            Guid.NewGuid(),
            new RestaurantChanged(
                Guid.NewGuid(),
                "R-1",
                "Restaurante",
                "Calle 1",
                null,
                true,
                1,
                DateTime.UtcNow
            )
        );

        Assert.Equal(0, fixture.Channel.Acknowledgements);
        Assert.Equal(0, fixture.Channel.Rejections);
        Assert.Equal(0, await fixture.GetInboxCountAsync());
    }

    [Fact]
    public async Task Ack_failure_publishes_a_retry_copy_after_persisting_projection()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync();
        fixture.Channel.ThrowOnNextAcknowledgement = true;
        Guid restaurantId = Guid.NewGuid();

        await fixture.DeliverAsync(
            Guid.NewGuid(),
            new RestaurantChanged(
                restaurantId,
                "R-1",
                "Restaurante",
                "Calle 1",
                null,
                true,
                1,
                DateTime.UtcNow
            )
        );

        Assert.Equal(1, fixture.Channel.Acknowledgements);
        Assert.Equal(0, fixture.Channel.Rejections);
        Assert.Equal(1, fixture.Channel.RetryPublications);
        Assert.Equal("Restaurante", (await fixture.GetRestaurantAsync(restaurantId)).Name);
    }

    [Fact]
    public async Task Storage_failure_schedules_a_retry_without_acknowledging_message()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(failSaveChanges: true);

        await fixture.DeliverAsync(
            Guid.NewGuid(),
            new RestaurantChanged(
                Guid.NewGuid(),
                "R-1",
                "Restaurante",
                "Calle 1",
                null,
                true,
                1,
                DateTime.UtcNow
            )
        );

        Assert.Equal(1, fixture.Channel.RetryPublications);
        Assert.Equal(1, fixture.Channel.Acknowledgements);
        Assert.Empty(await fixture.GetRestaurantsAsync());
    }

    [Fact]
    public async Task Storage_failure_at_retry_limit_is_dead_lettered()
    {
        await using WorkerFixture fixture = await WorkerFixture.CreateAsync(failSaveChanges: true);
        BasicProperties properties = new()
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = typeof(RestaurantChanged).FullName,
            Headers = new Dictionary<string, object?>
            {
                ["x-restaurantes-retry-count"] = RabbitMqRetry.MaximumAttempts,
            },
        };
        RestaurantChanged integrationEvent = new(
            Guid.NewGuid(),
            "R-1",
            "Restaurante",
            "Calle 1",
            null,
            true,
            1,
            DateTime.UtcNow
        );

        await fixture.DeliverRawAsync(
            properties,
            JsonSerializer.SerializeToUtf8Bytes(integrationEvent)
        );

        Assert.Equal(0, fixture.Channel.Acknowledgements);
        Assert.Equal(1, fixture.Channel.Rejections);
        Assert.False(fixture.Channel.LastRequeue);
    }

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly RestaurantProjectionWorker worker;
        private readonly IChannel channel;

        private WorkerFixture(
            ServiceProvider services,
            RestaurantProjectionWorker worker,
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
        public object? WorkerChannel =>
            typeof(RestaurantProjectionWorker)
                .GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(worker);

        public static async Task<WorkerFixture> CreateAsync(
            bool assignChannel = true,
            bool failSaveChanges = false
        )
        {
            DbContextOptionsBuilder<RestaurantReadDbContext> options =
                new DbContextOptionsBuilder<RestaurantReadDbContext>().UseInMemoryDatabase(
                    Guid.NewGuid().ToString()
                );
            if (failSaveChanges)
            {
                options.AddInterceptors(new ThrowOnSaveChangesInterceptor());
            }
            RestaurantReadDbContext db = new(options.Options);
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider();
            RestaurantProjectionWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                TimeProvider.System,
                NullLogger<RestaurantProjectionWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            if (assignChannel)
            {
                typeof(RestaurantProjectionWorker)
                    .GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(worker, channel);
            }
            return new WorkerFixture(services, worker, channel, (ChannelProxy)(object)channel);
        }

        public Task DeliverAsync<TEvent>(Guid messageId, TEvent message) =>
            DeliverRawAsync(
                messageId.ToString(),
                typeof(TEvent).FullName!,
                JsonSerializer.SerializeToUtf8Bytes(message)
            );

        public async Task DeliverRawAsync(string messageId, string eventType, byte[] body)
        {
            BasicProperties properties = new() { MessageId = messageId, Type = eventType };
            await DeliverRawAsync(properties, body);
        }

        public async Task DeliverRawAsync(BasicProperties properties, byte[] body)
        {
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
            MethodInfo handle = typeof(RestaurantProjectionWorker).GetMethod(
                "HandleMessageAsync",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)handle.Invoke(worker, [channel, args])!;
        }

        public async Task DisposeWorkerResourcesAsync()
        {
            MethodInfo dispose = typeof(RestaurantProjectionWorker).GetMethod(
                "DisposeResourcesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)dispose.Invoke(worker, null)!;
        }

        public async Task<RestaurantReadModel> GetRestaurantAsync(Guid id)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<RestaurantReadDbContext>()
                .Restaurants.SingleAsync(
                    item => item.Id == id,
                    TestContext.Current.CancellationToken
                );
        }

        public async Task<CompanyReadModel> GetCompanyAsync(Guid id)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<RestaurantReadDbContext>()
                .Companies.SingleAsync(
                    item => item.Id == id,
                    TestContext.Current.CancellationToken
                );
        }

        public async Task<int> GetInboxCountAsync()
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<RestaurantReadDbContext>()
                .InboxMessages.CountAsync(TestContext.Current.CancellationToken);
        }

        public async Task<List<RestaurantReadModel>> GetRestaurantsAsync()
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<RestaurantReadDbContext>()
                .Restaurants.ToListAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync() => await services.DisposeAsync();
    }

    private sealed class ThrowOnSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Read store unavailable.");
    }

    public class ChannelProxy : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }
        public bool LastRequeue { get; private set; }
        public bool ThrowOnNextAcknowledgement { get; set; }
        public int RetryPublications { get; private set; }
        public int Disposals { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IChannel.BasicAckAsync))
            {
                if (ThrowOnNextAcknowledgement)
                {
                    ThrowOnNextAcknowledgement = false;
                    throw new InvalidOperationException("Broker acknowledgement failed.");
                }

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
