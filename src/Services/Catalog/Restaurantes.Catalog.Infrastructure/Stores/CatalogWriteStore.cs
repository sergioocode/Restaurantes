using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Domain;
using Restaurantes.Catalog.Infrastructure.Persistence;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;

namespace Restaurantes.Catalog.Infrastructure.Stores;

public sealed class CatalogWriteStore(CatalogWriteDbContext db) : ICatalogWriteStore
{
    public Task<bool> CategoryCodeExistsAsync(string code, CancellationToken ct)
    {
        return db.Categories.AnyAsync(x => x.Code == code, ct);
    }

    public Task<bool> ProductSkuExistsAsync(string sku, CancellationToken ct)
    {
        return db.Products.AnyAsync(x => x.Sku == sku, ct);
    }

    public Task<Category?> FindCategoryAsync(Guid id, CancellationToken ct)
    {
        return db.Categories.SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<Product?> FindProductAsync(Guid id, CancellationToken ct)
    {
        return db.Products.SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<RestaurantMenuItem?> FindMenuItemAsync(
        Guid restaurantId,
        Guid productId,
        CancellationToken ct
    )
    {
        return db.MenuItems.SingleOrDefaultAsync(
            x => x.RestaurantId == restaurantId && x.ProductId == productId,
            ct
        );
    }

    public Task<RestaurantKitchenStation?> FindStationAsync(
        Guid restaurantId,
        Guid id,
        CancellationToken ct
    )
    {
        return db.KitchenStations.SingleOrDefaultAsync(
            x => x.RestaurantId == restaurantId && x.Id == id,
            ct
        );
    }

    public Task<RestaurantKitchenStation?> FindStationByCodeAsync(
        Guid restaurantId,
        string code,
        CancellationToken ct
    )
    {
        return db.KitchenStations.SingleOrDefaultAsync(
            x => x.RestaurantId == restaurantId && x.Code == code,
            ct
        );
    }

    public Task<bool> HasOtherActivePrimaryStationAsync(
        Guid restaurantId,
        Guid? excludedStationId,
        CancellationToken ct
    )
    {
        return db.KitchenStations.AnyAsync(
            x =>
                x.RestaurantId == restaurantId
                && x.IsActive
                && x.IsPrimary
                && (!excludedStationId.HasValue || x.Id != excludedStationId.Value),
            ct
        );
    }

    public async Task SaveWithEventAsync(
        object aggregate,
        object integrationEvent,
        CancellationToken ct
    )
    {
        if (db.Entry(aggregate).State == EntityState.Detached)
        {
            db.Add(aggregate);
        }

        DateTime occurred = integrationEvent switch
        {
            CategoryChanged x => x.OccurredAtUtc,
            ProductChanged x => x.OccurredAtUtc,
            CatalogItemChanged x => x.OccurredAtUtc,
            KitchenStationChanged x => x.OccurredAtUtc,
            _ => throw new NotSupportedException(),
        };
        Type type = integrationEvent.GetType();
        db.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = type.FullName!,
                Payload = JsonSerializer.Serialize(integrationEvent, type),
                OccurredAtUtc = occurred,
            }
        );
        await db.SaveChangesAsync(ct);
    }
}
