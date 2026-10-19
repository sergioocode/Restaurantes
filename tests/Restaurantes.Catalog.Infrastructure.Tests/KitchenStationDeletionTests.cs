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
    public async Task Create_station_persists_printing_configuration_and_emits_it()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = CreateDb();
        Guid restaurantId = Guid.NewGuid();
        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);

        KitchenStationResponse? created = await service.CreateStationAsync(
            restaurantId,
            new CreateKitchenStationRequest
            {
                Code = "  bebidas ",
                Name = "Bebidas",
                IsPrimary = false,
                RequiresPrimaryDispatch = true,
                PrintTicket = true,
                Priority = 4,
            },
            ct
        );

        Assert.NotNull(created);
        Assert.Equal("BEBIDAS", created.Code);
        Assert.False(created.IsPrimary);
        Assert.True(created.RequiresPrimaryDispatch);
        Assert.True(created.PrintTicket);
        Assert.Equal(4, created.Priority);
        KitchenStationChanged emitted = JsonSerializer.Deserialize<KitchenStationChanged>(
            Assert.Single(await db.OutboxMessages.ToListAsync(ct)).Payload
        )!;
        Assert.True(emitted.PrintTicket);
    }

    [Fact]
    public async Task Create_station_returns_null_for_a_duplicate_code()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = CreateDb();
        Guid restaurantId = Guid.NewGuid();
        RestaurantKitchenStation existing = RestaurantKitchenStation.Create(
            restaurantId,
            "BEBIDAS",
            "Bebidas",
            new(false, false, 1),
            DateTime.UtcNow
        );
        db.KitchenStations.Add(existing);
        await db.SaveChangesAsync(ct);
        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);

        KitchenStationResponse? created = await service.CreateStationAsync(
            restaurantId,
            new CreateKitchenStationRequest { Code = " bebidas ", Name = "Barra" },
            ct
        );

        Assert.Null(created);
        Assert.Empty(await db.OutboxMessages.ToListAsync(ct));
    }

    [Fact]
    public async Task Create_station_rejects_a_second_active_primary_station()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = CreateDb();
        Guid restaurantId = Guid.NewGuid();
        db.KitchenStations.Add(
            RestaurantKitchenStation.Create(
                restaurantId,
                "COCINA",
                "Cocina",
                new(true, false, 0),
                DateTime.UtcNow
            )
        );
        await db.SaveChangesAsync(ct);
        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.CreateStationAsync(
                    restaurantId,
                    new CreateKitchenStationRequest
                    {
                        Code = "PARRILLA",
                        Name = "Parrilla",
                        IsPrimary = true,
                    },
                    ct
                )
        );

        Assert.Contains("Only one active primary KDS", exception.Message);
    }

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
            new(false, false, 1),
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
            new(false, true, 3),
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

    [Fact]
    public async Task Primary_and_ticket_field_updates_keep_station_rules_and_publish_current_state()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = CreateDb();
        Guid restaurantId = Guid.NewGuid();
        RestaurantKitchenStation station = RestaurantKitchenStation.Create(
            restaurantId,
            "PARRILLA",
            "Parrilla",
            new(false, true, 3),
            DateTime.UtcNow
        );
        db.KitchenStations.Add(station);
        await db.SaveChangesAsync(ct);
        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);

        KitchenStationResponse? primary = await service.SetStationPrimaryAsync(
            restaurantId,
            station.Id,
            true,
            ct
        );
        Assert.NotNull(primary);
        Assert.True(primary.IsPrimary);
        Assert.False(primary.RequiresPrimaryDispatch);
        Assert.Equal(0, primary.Priority);

        KitchenStationResponse? secondary = await service.SetStationPrimaryAsync(
            restaurantId,
            station.Id,
            false,
            ct
        );
        Assert.NotNull(secondary);
        Assert.False(secondary.IsPrimary);
        Assert.Equal(1, secondary.Priority);

        KitchenStationResponse? dispatch = await service.SetStationRequiresPrimaryDispatchAsync(
            restaurantId,
            station.Id,
            true,
            ct
        );
        Assert.NotNull(dispatch);
        Assert.True(dispatch.RequiresPrimaryDispatch);

        KitchenStationResponse? printing = await service.SetStationPrintTicketAsync(
            restaurantId,
            station.Id,
            true,
            ct
        );
        Assert.NotNull(printing);
        Assert.True(printing.PrintTicket);
        Assert.Equal(5, printing.Version);
        KitchenStationChanged latest = JsonSerializer.Deserialize<KitchenStationChanged>(
            (await db.OutboxMessages.ToListAsync(ct))[^1].Payload
        )!;
        Assert.True(latest.PrintTicket);

        KitchenStationResponse? renamed = await service.UpdateStationAsync(
            restaurantId,
            station.Id,
            new UpdateKitchenStationRequest
            {
                Name = "Parrilla grande",
                IsActive = true,
                Priority = 2,
                RequiresPrimaryDispatch = true,
            },
            ct
        );
        Assert.NotNull(renamed);
        Assert.True(renamed.PrintTicket);
        Assert.Equal(2, renamed.Priority);
    }

    [Fact]
    public async Task Station_updates_reject_conflicting_primary_and_unknown_station()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using CatalogWriteDbContext db = CreateDb();
        Guid restaurantId = Guid.NewGuid();
        RestaurantKitchenStation primary = RestaurantKitchenStation.Create(
            restaurantId,
            "MAIN",
            "Principal",
            new(true, false, 0),
            DateTime.UtcNow
        );
        RestaurantKitchenStation secondary = RestaurantKitchenStation.Create(
            restaurantId,
            "BAR",
            "Barra",
            new(false, false, 1),
            DateTime.UtcNow
        );
        db.KitchenStations.AddRange(primary, secondary);
        await db.SaveChangesAsync(ct);
        CatalogCommandService service = new(new CatalogWriteStore(db), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateStationAsync(
                restaurantId,
                secondary.Id,
                new UpdateKitchenStationRequest
                {
                    Name = "Barra",
                    IsActive = true,
                    IsPrimary = true,
                },
                ct
            )
        );
        Assert.False(secondary.IsPrimary);
        Assert.Null(
            await service.SetStationPrintTicketAsync(restaurantId, Guid.NewGuid(), true, ct)
        );
        Assert.Null(
            await service.UpdateStationAsync(
                restaurantId,
                Guid.NewGuid(),
                new UpdateKitchenStationRequest { Name = "Inexistente" },
                ct
            )
        );
    }

    [Fact]
    public void Primary_station_cannot_dispatch_itself_or_change_priority()
    {
        RestaurantKitchenStation station = RestaurantKitchenStation.Create(
            Guid.NewGuid(),
            "MAIN",
            "Principal",
            new(true, true, 9),
            DateTime.UtcNow
        );

        Assert.True(station.IsPrimary);
        Assert.False(station.RequiresPrimaryDispatch);
        Assert.Equal(0, station.Priority);
        Assert.Throws<InvalidOperationException>(() => station.SetPriority(2, DateTime.UtcNow));
        Assert.Throws<InvalidOperationException>(() =>
            station.SetRequiresPrimaryDispatch(true, DateTime.UtcNow)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RestaurantKitchenStation.Create(
                Guid.NewGuid(),
                "BAR",
                "Barra",
                new(false, false, 0),
                DateTime.UtcNow
            )
        );
    }

    private static CatalogWriteDbContext CreateDb() =>
        new(
            new DbContextOptionsBuilder<CatalogWriteDbContext>()
                .UseInMemoryDatabase("station-lifecycle-" + Guid.NewGuid().ToString("N"))
                .Options
        );
}
