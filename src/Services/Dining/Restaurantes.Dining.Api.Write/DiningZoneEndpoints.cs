using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Restaurantes.Security;

namespace Restaurantes.Dining.Api.Write;

public sealed class SaveZoneRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }
}

public static partial class DiningEndpoints
{
    private static void MapZoneEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/restaurants/{restaurantId:guid}/zones", ListZones);
        group.MapPost("/restaurants/{restaurantId:guid}/zones", CreateZone);
        group.MapPut("/restaurants/{restaurantId:guid}/zones/{zoneId:guid}", UpdateZone);
        group.MapDelete("/restaurants/{restaurantId:guid}/zones/{zoneId:guid}", DeleteZone);
    }

    private static async Task<IResult> ListZones(
        Guid restaurantId,
        ClaimsPrincipal principal,
        DiningDbContext db,
        CancellationToken ct
    )
    {
        return !principal.CanAccessRestaurant(restaurantId, RestaurantPermissions.TablesRead)
            ? Results.Forbid()
            : Results.Ok(
                await db
                    .Zones.AsNoTracking()
                    .Where(x => x.RestaurantId == restaurantId && x.DeletedAtUtc == null)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Name)
                    .ToListAsync(ct)
            );
    }

    private static async Task<IResult> CreateZone(
        Guid restaurantId,
        SaveZoneRequest request,
        ClaimsPrincipal principal,
        DiningDbContext db,
        CancellationToken ct
    )
    {
        if (restaurantId == Guid.Empty)
        {
            return Results.BadRequest(new { detail = "RestaurantId is required." });
        }

        if (!principal.CanAccessRestaurant(restaurantId, RestaurantPermissions.TablesManage))
        {
            return Results.Forbid();
        }

        DiningZone zone = new() { Id = Guid.NewGuid(), RestaurantId = restaurantId };
        db.Zones.Add(zone);
        return await SaveZone(zone, request, db, true, ct);
    }

    private static async Task<IResult> UpdateZone(
        Guid restaurantId,
        Guid zoneId,
        SaveZoneRequest request,
        ClaimsPrincipal principal,
        DiningDbContext db,
        CancellationToken ct
    )
    {
        if (!principal.CanAccessRestaurant(restaurantId, RestaurantPermissions.TablesManage))
        {
            return Results.Forbid();
        }

        DiningZone? zone = await db.Zones.SingleOrDefaultAsync(
            x => x.Id == zoneId && x.RestaurantId == restaurantId && x.DeletedAtUtc == null,
            ct
        );
        return zone is null ? Results.NotFound() : await SaveZone(zone, request, db, false, ct);
    }

    private static async Task<IResult> SaveZone(
        DiningZone zone,
        SaveZoneRequest request,
        DiningDbContext db,
        bool created,
        CancellationToken ct
    )
    {
        string name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 80 || request.SortOrder < 0)
        {
            return Results.BadRequest(
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
                ? Results.Created(
                    $"/api/dining/restaurants/{zone.RestaurantId}/zones/{zone.Id}",
                    zone
                )
                : Results.Ok(zone);
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: "23505" })
        {
            return Results.Conflict(
                new { detail = "A zone with this name already exists in the restaurant." }
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(
                new { detail = "The zone was changed or deleted. Refresh and retry." }
            );
        }
    }

    // Call within a transaction. Assignment and deletion serialize on the same zone row.
    private static Task<DiningZone?> LockZone(
        DiningDbContext db,
        Guid restaurantId,
        Guid zoneId,
        CancellationToken ct
    )
    {
        return db
            .Zones.FromSqlInterpolated(
                $"SELECT * FROM dining_zones WHERE \"Id\" = {zoneId} AND \"RestaurantId\" = {restaurantId} FOR UPDATE"
            )
            .SingleOrDefaultAsync(ct);
    }

    private static async Task<IResult> DeleteZone(
        Guid restaurantId,
        Guid zoneId,
        ClaimsPrincipal principal,
        DiningDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!principal.CanAccessRestaurant(restaurantId, RestaurantPermissions.TablesManage))
        {
            return Results.Forbid();
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        DiningZone? zone = await LockZone(db, restaurantId, zoneId, ct);
        if (zone is null)
        {
            return Results.NotFound();
        }

        if (zone.DeletedAtUtc is not null)
        {
            return Results.NoContent();
        }

        if (
            await db.Tables.AnyAsync(
                x => x.ZoneId == zoneId && x.RestaurantId == restaurantId && x.DeletedAtUtc == null,
                ct
            )
        )
        {
            return Results.Conflict(
                new
                {
                    detail = "La zona contiene ubicaciones sin eliminar, incluidas las inactivas. Muévelas o elimínalas primero.",
                }
            );
        }

        zone.DeletedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }
}
