using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Catalog.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class KitchenStationDeletionTests
{
    [Fact]
    public async Task Delete_unassigned_station_removes_it_and_records_deletion_event()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = CreateDb();
        Guid restaurantId = Guid.NewGuid();
        RestaurantKitchenStation station = RestaurantKitchenStation.Create(
            restaurantId,
            "EXTRAS",
            "Extras",
            false,
            false,
            1,
            DateTime.UtcNow
        );
        db.KitchenStations.Add(station);
        await db.SaveChangesAsync(ct);

        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);
        Assert.True(await service.DeleteStationAsync(restaurantId, station.Id, ct));
        Assert.Empty(await db.KitchenStations.ToListAsync(ct));
        KitchenStationChanged message = JsonSerializer.Deserialize<KitchenStationChanged>(
            Assert.Single(await db.OutboxMessages.ToListAsync(ct)).Payload
        )!;
        Assert.True(message.IsDeleted);
        Assert.Equal(station.Id, message.StationId);
    }

    [Fact]
    public async Task Delete_assigned_station_keeps_it()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = CreateDb();
        Guid restaurantId = Guid.NewGuid();
        RestaurantKitchenStation station = RestaurantKitchenStation.Create(
            restaurantId,
            "EXTRAS",
            "Extras",
            false,
            false,
            1,
            DateTime.UtcNow
        );
        db.KitchenStations.Add(station);
        db.Categories.Add(Category.Create("EXTRAS", "Extras", "EXTRAS", "Extras", DateTime.UtcNow));
        await db.SaveChangesAsync(ct);

        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteStationAsync(restaurantId, station.Id, ct)
        );
        Assert.Single(await db.KitchenStations.ToListAsync(ct));
        Assert.Empty(await db.OutboxMessages.ToListAsync(ct));
    }

    private static CatalogWriteDbContext CreateDb() =>
        new(
            new DbContextOptionsBuilder<CatalogWriteDbContext>()
                .UseInMemoryDatabase("station-delete-" + Guid.NewGuid().ToString("N"))
                .Options
        );
}
