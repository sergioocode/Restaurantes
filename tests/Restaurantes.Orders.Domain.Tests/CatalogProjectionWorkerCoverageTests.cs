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
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.Orders.Infrastructure.Workers;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class CatalogProjectionWorkerCoverageTests
{
    [Fact]
    public async Task Category_changes_update_kds_requirement_and_fill_missing_station()
    {
        await using ProjectionFixture fixture = await ProjectionFixture.CreateAsync();
        Guid categoryId = Guid.NewGuid();
        OrderCatalogItem missingStation = CatalogItem(categoryId, stationCode: "");
        OrderCatalogItem existingStation = CatalogItem(categoryId, stationCode: "BAR");
        fixture.Db.CatalogItems.AddRange(missingStation, existingStation);
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await fixture.DeliverAsync(
            new CategoryChanged(
                categoryId,
                "FOOD",
                "Comida",
                "KITCHEN",
                "Cocina",
                true,
                2,
                DateTime.UtcNow,
                RequiresKds: true
            )
        );

        Assert.True(missingStation.RequiresKds);
        Assert.Equal("KITCHEN", missingStation.StationCode);
        Assert.Equal("Cocina", missingStation.StationName);
        Assert.True(existingStation.RequiresKds);
        Assert.Equal("BAR", existingStation.StationCode);

        await fixture.DeliverAsync(
            new CategoryChanged(
                categoryId,
                "FOOD",
                "Comida",
                "KITCHEN",
                "Cocina",
                true,
                3,
                DateTime.UtcNow,
                RequiresKds: false
            )
        );

        Assert.False(missingStation.RequiresKds);
        Assert.Equal("KITCHEN", missingStation.StationCode);
        Assert.Equal(2, fixture.ChannelProxy.Acknowledgements);
    }

    [Fact]
    public async Task Catalog_item_projection_inserts_updates_and_ignores_stale_versions()
    {
        await using ProjectionFixture fixture = await ProjectionFixture.CreateAsync();
        Guid restaurantId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();

        await fixture.DeliverAsync(
            CatalogEvent(restaurantId, productId, categoryId, "Nuevo", 12m, 1) with
            {
                OccurredAtUtc = new DateTime(2026, 10, 20, 8, 30, 0, DateTimeKind.Local),
                TaxRate = 10m,
                DefaultFormat = "RACION",
                Formats = [new MenuItemFormatPrice("RACION", 12m)],
                ExtraGroups =
                [
                    new ProductExtraGroup(
                        Guid.NewGuid(),
                        "Extras",
                        0,
                        1,
                        [new ProductExtraOption(Guid.NewGuid(), "EX-1", "Extra", 2m)]
                    ),
                ],
                RequiresKds = false,
            }
        );
        OrderCatalogItem item = await fixture.Db.CatalogItems.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal("Nuevo", item.ProductName);
        Assert.Equal(12m, item.Price);
        Assert.Equal(10m, item.TaxRate);
        Assert.Equal("RACION", item.DefaultFormat);
        Assert.False(item.RequiresKds);
        Assert.Single(JsonSerializer.Deserialize<MenuItemFormatPrice[]>(item.FormatsJson)!);
        Assert.Single(JsonSerializer.Deserialize<ProductExtraGroup[]>(item.ExtraGroupsJson)!);
        Assert.Equal(DateTimeKind.Utc, item.UpdatedAtUtc.Kind);

        await fixture.DeliverAsync(
            CatalogEvent(restaurantId, productId, categoryId, "Actualizado", 15m, 2)
        );
        await fixture.DeliverAsync(
            CatalogEvent(restaurantId, productId, categoryId, "Antiguo", 1m, 1)
        );

        Assert.Equal("Actualizado", item.ProductName);
        Assert.Equal(15m, item.Price);
        Assert.Equal(2, item.Version);
        Assert.Equal(3, fixture.ChannelProxy.Acknowledgements);
    }

    [Fact]
    public async Task Kitchen_station_projection_creates_updates_and_deletes_only_newer_version()
    {
        await using ProjectionFixture fixture = await ProjectionFixture.CreateAsync();
        Guid restaurantId = Guid.NewGuid();
        Guid stationId = Guid.NewGuid();
        await fixture.DeliverAsync(StationEvent(stationId, restaurantId, "COCINA", "Cocina", 1));
        OrderKitchenStationConfiguration station =
            await fixture.Db.KitchenStationConfigurations.SingleAsync(
                TestContext.Current.CancellationToken
            );
        Assert.True(station.IsPrimary);
        Assert.Equal("Cocina", station.Name);

        await fixture.DeliverAsync(
            StationEvent(stationId, restaurantId, "COCINA", "Cocina caliente", 2) with
            {
                RequiresPrimaryDispatch = true,
                Priority = 4,
                IsActive = false,
            }
        );
        Assert.Equal("Cocina caliente", station.Name);
        Assert.True(station.RequiresPrimaryDispatch);
        Assert.Equal(4, station.Priority);
        Assert.False(station.IsActive);

        await fixture.DeliverAsync(
            StationEvent(Guid.NewGuid(), restaurantId, "COCINA", "Otra estación", 5) with
            {
                IsDeleted = true,
            }
        );
        Assert.Single(fixture.Db.KitchenStationConfigurations);

        await fixture.DeliverAsync(
            StationEvent(stationId, restaurantId, "COCINA", "Cocina", 2) with
            {
                IsDeleted = true,
            }
        );
        Assert.Single(fixture.Db.KitchenStationConfigurations);

        await fixture.DeliverAsync(
            StationEvent(stationId, restaurantId, "COCINA", "Cocina", 3) with
            {
                IsDeleted = true,
            }
        );
        Assert.Empty(fixture.Db.KitchenStationConfigurations);
        Assert.Equal(5, fixture.ChannelProxy.Acknowledgements);
    }

    [Fact]
    public async Task Invalid_or_unsupported_catalog_messages_are_dead_lettered()
    {
        await using ProjectionFixture fixture = await ProjectionFixture.CreateAsync();

        await fixture.DeliverRawAsync("unknown.event", "{}"u8.ToArray());
        await fixture.DeliverRawAsync(typeof(CategoryChanged).FullName!, "{"u8.ToArray());

        Assert.Equal(2, fixture.ChannelProxy.Rejections);
        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
    }

    [Fact]
    public async Task Projection_storage_failure_schedules_a_retry_with_attempt_metadata()
    {
        await using ProjectionFixture fixture = await ProjectionFixture.CreateAsync(
            failOnSave: true
        );

        await fixture.DeliverAsync(
            CatalogEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Producto", 5m, 1)
        );

        Assert.Equal(1, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(0, fixture.ChannelProxy.Rejections);
        PublishedRetry retry = Assert.Single(fixture.ChannelProxy.PublishedMessages);
        Assert.Equal($"{CatalogTopology.OrdersIntegrationQueueName}.retry.1", retry.RoutingKey);
        Assert.Equal(1, retry.Properties.Headers!["x-restaurantes-retry-count"]);
    }

    [Fact]
    public async Task Projection_storage_failure_at_retry_limit_is_dead_lettered()
    {
        await using ProjectionFixture fixture = await ProjectionFixture.CreateAsync(
            failOnSave: true
        );
        BasicProperties properties = new()
        {
            Type = typeof(CategoryChanged).FullName,
            Headers = new Dictionary<string, object?>
            {
                ["x-restaurantes-retry-count"] = RabbitMqRetry.MaximumAttempts,
            },
        };

        await fixture.DeliverRawAsync(
            properties,
            JsonSerializer.SerializeToUtf8Bytes(
                new CategoryChanged(
                    Guid.NewGuid(),
                    "FOOD",
                    "Comida",
                    "KITCHEN",
                    "Cocina",
                    true,
                    1,
                    DateTime.UtcNow
                )
            )
        );

        Assert.Equal(0, fixture.ChannelProxy.Acknowledgements);
        Assert.Equal(1, fixture.ChannelProxy.Rejections);
        Assert.Empty(fixture.ChannelProxy.PublishedMessages);
    }

    private static OrderCatalogItem CatalogItem(Guid categoryId, string stationCode) =>
        new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductName = "Producto",
            CategoryId = categoryId,
            StationCode = stationCode,
            StationName = stationCode,
            IsAvailable = true,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static CatalogItemChanged CatalogEvent(
        Guid restaurantId,
        Guid productId,
        Guid categoryId,
        string name,
        decimal price,
        int version
    ) =>
        new(
            restaurantId,
            productId,
            "SKU-1",
            name,
            categoryId,
            "FOOD",
            "Comida",
            price,
            true,
            "KITCHEN",
            "Cocina",
            version,
            DateTime.UtcNow
        );

    private static KitchenStationChanged StationEvent(
        Guid stationId,
        Guid restaurantId,
        string code,
        string name,
        int version
    ) => new(stationId, restaurantId, code, name, true, false, 1, true, version, DateTime.UtcNow);

    private sealed class ProjectionFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly CatalogProjectionWorker worker;
        private readonly IChannel channel;

        private ProjectionFixture(
            ServiceProvider services,
            OrderWriteDbContext db,
            CatalogProjectionWorker worker,
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

        public OrderWriteDbContext Db { get; }
        public ChannelProxy ChannelProxy { get; }

        public static async Task<ProjectionFixture> CreateAsync(bool failOnSave = false)
        {
            DbContextOptionsBuilder<OrderWriteDbContext> optionsBuilder =
                new DbContextOptionsBuilder<OrderWriteDbContext>().UseInMemoryDatabase(
                    Guid.NewGuid().ToString()
                );
            if (failOnSave)
            {
                optionsBuilder.AddInterceptors(new ThrowOnSaveChangesInterceptor());
            }
            DbContextOptions<OrderWriteDbContext> options = optionsBuilder.Options;
            OrderWriteDbContext db = new(options);
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider();
            CatalogProjectionWorker worker = new(
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RabbitMqOptions()),
                NullLogger<CatalogProjectionWorker>.Instance
            );
            IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
            ChannelProxy proxy = (ChannelProxy)(object)channel;
            typeof(CatalogProjectionWorker)
                .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(worker, channel);
            return new ProjectionFixture(services, db, worker, channel, proxy);
        }

        public Task DeliverAsync<TEvent>(TEvent message)
        {
            string eventType = typeof(TEvent).FullName!;
            return DeliverRawAsync(eventType, JsonSerializer.SerializeToUtf8Bytes(message));
        }

        public async Task DeliverRawAsync(string eventType, byte[] body)
        {
            BasicProperties properties = new() { Type = eventType };
            await DeliverRawAsync(properties, body);
        }

        public async Task DeliverRawAsync(BasicProperties properties, byte[] body)
        {
            BasicDeliverEventArgs args = new(
                "consumer",
                (ulong)(ChannelProxy.Acknowledgements + ChannelProxy.Rejections + 1),
                false,
                "exchange",
                "routing-key",
                properties,
                body
            );
            MethodInfo handle = typeof(CatalogProjectionWorker).GetMethod(
                "Handle",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            await (Task)handle.Invoke(worker, [channel, args])!;
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
        }
    }

    public class ChannelProxy : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }
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
