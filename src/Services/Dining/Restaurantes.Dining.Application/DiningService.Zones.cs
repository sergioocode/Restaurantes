using System.Security.Claims;
using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Application;

public sealed partial class DiningService
{
    public async Task<DiningResult> ListZones(
        Guid restaurantId,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        return !access.CanAccessRestaurant(principal, restaurantId, DiningPermission.TablesRead)
            ? DiningResults.Forbid()
            : DiningResults.Ok(await db.ListZonesAsync(restaurantId, ct));
    }

    public async Task<DiningResult> CreateZone(
        Guid restaurantId,
        SaveZoneRequest request,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        if (restaurantId == Guid.Empty)
        {
            return DiningResults.BadRequest(new { detail = "RestaurantId is required." });
        }

        if (!access.CanAccessRestaurant(principal, restaurantId, DiningPermission.TablesManage))
        {
            return DiningResults.Forbid();
        }

        DiningZone zone = new() { Id = Guid.NewGuid(), RestaurantId = restaurantId };
        db.Add(zone);
        return await SaveZone(zone, request, db, true, ct);
    }

    public async Task<DiningResult> UpdateZone(
        Guid restaurantId,
        Guid zoneId,
        SaveZoneRequest request,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        if (!access.CanAccessRestaurant(principal, restaurantId, DiningPermission.TablesManage))
        {
            return DiningResults.Forbid();
        }

        DiningZone? zone = await db.FindZoneAsync(restaurantId, zoneId, ct);
        return zone is null ? DiningResults.NotFound() : await SaveZone(zone, request, db, false, ct);
    }

    private static async Task<DiningResult> SaveZone(
        DiningZone zone,
        SaveZoneRequest request,
        IDiningStore db,
        bool created,
        CancellationToken ct
    )
    {
        string name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 80 || request.SortOrder < 0)
        {
            return DiningResults.BadRequest(
                new
                {
                    detail = "A zone name of 1–80 characters and a non-negative sort order are required.",
                }
            );
        }

        zone.Name = name;
        zone.SortOrder = request.SortOrder;
        try
        {
            await db.SaveChangesAsync(ct);
            return created
                ? DiningResults.Created(
                    $"/api/dining/restaurants/{zone.RestaurantId}/zones/{zone.Id}",
                    zone
                )
                : DiningResults.Ok(zone);
        }
        catch (DiningStoreException e) when (e.Failure == DiningStoreFailure.Duplicate)
        {
            return DiningResults.Conflict(
                new { detail = "A zone with this name already exists in the restaurant." }
            );
        }
        catch (DiningStoreException e) when (e.Failure == DiningStoreFailure.Concurrency)
        {
            return DiningResults.Conflict(
                new { detail = "The zone was changed or deleted. Refresh and retry." }
            );
        }
    }

    public async Task<DiningResult> DeleteZone(
        Guid restaurantId,
        Guid zoneId,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        if (!access.CanAccessRestaurant(principal, restaurantId, DiningPermission.TablesManage))
        {
            return DiningResults.Forbid();
        }

        await using IDiningTransaction transaction = await db.BeginTransactionAsync(ct);
        DiningZone? zone = await db.LockZoneAsync(restaurantId, zoneId, ct);
        if (zone is null)
        {
            return DiningResults.NotFound();
        }

        if (zone.DeletedAtUtc is not null)
        {
            return DiningResults.NoContent();
        }

        if (await db.HasTablesInZoneAsync(restaurantId, zoneId, ct))
        {
            return DiningResults.Conflict(
                new
                {
                    detail = "La zona contiene ubicaciones sin eliminar, incluidas las inactivas. Muévelas o elimínalas primero.",
                }
            );
        }

        zone.DeletedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return DiningResults.NoContent();
    }
}
