using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Contracts.Requests;
using Restaurantes.Catalog.Contracts.Responses;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Catalog.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class KitchenStationLifecycleTests
{
    [Fact]
    public async Task Deactivate_station_keeps_it_and_publishes_inactive_state()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = CreateDb();
        Guid restaurantId = Guid.NewGuid();
        RestaurantKitchenStation station = RestaurantKitchenStation.Create(
            restaurantId,
            "BEBIDAS",
            "Bebidas",
            false,
            false,
            1,
            DateTime.UtcNow
        );
        db.KitchenStations.Add(station);
        await db.SaveChangesAsync(ct);

        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);
        KitchenStationResponse? updated = await service.UpdateStationAsync(
            restaurantId,
            station.Id,
            new UpdateKitchenStationRequest { Name = "Bebidas", IsActive = false },
            ct
        );

        Assert.NotNull(updated);
        Assert.False(updated.IsActive);
        Assert.Single(await db.KitchenStations.ToListAsync(ct));
        KitchenStationChanged message = JsonSerializer.Deserialize<KitchenStationChanged>(
            Assert.Single(await db.OutboxMessages.ToListAsync(ct)).Payload
        )!;
        Assert.False(message.IsDeleted);
        Assert.False(message.IsActive);
        Assert.Equal(station.Id, message.StationId);
    }

    [Fact]
    public async Task Field_updates_preserve_other_station_values()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = CreateDb();
        Guid restaurantId = Guid.NewGuid();
        RestaurantKitchenStation station = RestaurantKitchenStation.Create(
            restaurantId,
            "BEBIDAS",
            "Bebidas",
            false,
            true,
            3,
            DateTime.UtcNow
        );
        db.KitchenStations.Add(station);
        await db.SaveChangesAsync(ct);

        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);
        await service.SetStationNameAsync(restaurantId, station.Id, "Barra", ct);
        KitchenStationResponse? updated = await service.SetStationPriorityAsync(
            restaurantId,
            station.Id,
            5,
            ct
        );

        Assert.NotNull(updated);
        Assert.Equal("Barra", updated.Name);
        Assert.Equal(5, updated.Priority);
        Assert.True(updated.RequiresPrimaryDispatch);
        Assert.True(updated.IsActive);

        KitchenStationResponse? deactivated = await service.SetStationActiveAsync(
            restaurantId,
            station.Id,
            false,
            ct
        );
        Assert.NotNull(deactivated);
        Assert.Equal("Barra", deactivated.Name);
        Assert.Equal(5, deactivated.Priority);
        Assert.True(deactivated.RequiresPrimaryDispatch);
        Assert.False(deactivated.IsActive);
    }

    private static CatalogWriteDbContext CreateDb() =>
        new(
            new DbContextOptionsBuilder<CatalogWriteDbContext>()
                .UseInMemoryDatabase("station-lifecycle-" + Guid.NewGuid().ToString("N"))
                .Options
        );
}
