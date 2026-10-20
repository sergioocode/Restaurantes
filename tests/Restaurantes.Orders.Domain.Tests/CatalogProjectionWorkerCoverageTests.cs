using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Catalog.Contracts.Events;
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
            }
        );
        OrderCatalogItem item = await fixture.Db.CatalogItems.SingleAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal("Nuevo", item.ProductName);
        Assert.Equal(12m, item.Price);
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
        Assert.Equal(4, fixture.ChannelProxy.Acknowledgements);
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

        public static async Task<ProjectionFixture> CreateAsync()
        {
            DbContextOptions<OrderWriteDbContext> options =
                new DbContextOptionsBuilder<OrderWriteDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options;
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
