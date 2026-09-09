using System.Security.Claims;
using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Application;

public sealed partial class DiningService
{
    public async Task<DiningResult> CreateTable(
        Guid restaurantId,
        CreateTableRequest request,
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

        await using IDiningTransaction transaction = await db.BeginTransactionAsync(ct);
        DiningZone? zone = await db.LockZoneAsync(restaurantId, request.ZoneId, ct);
        if (zone is null || zone.DeletedAtUtc is not null)
        {
            return DiningResults.BadRequest(
                new { detail = "Select a zone belonging to this restaurant." }
            );
        }

        RestaurantTable table = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            ZoneId = zone.Id,
            Zone = zone,
            RequestGuestCount = request.RequestGuestCount,
            Code = request.Code.Trim().ToUpperInvariant(),
            Label = request.Label.Trim(),
            QrCode = NewQrCode(),
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        db.Add(table);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return DiningResults.Created(
                $"/api/dining/tables/{table.Id}",
                TableResponse(table, null)
            );
        }
        catch (DiningStoreException e) when (e.Failure == DiningStoreFailure.Duplicate)
        {
            return DiningResults.Conflict(
                new { detail = $"Table code '{table.Code}' already exists in this restaurant." }
            );
        }
        catch (DiningStoreException e) when (e.Failure == DiningStoreFailure.ForeignKey)
        {
            return DiningResults.Conflict(
                new { detail = "The selected zone was deleted. Refresh and retry." }
            );
        }
    }

    public async Task<DiningResult> UpdateTable(
        Guid tableId,
        UpdateTableRequest request,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        await using IDiningTransaction transaction = await db.BeginTransactionAsync(ct);
        RestaurantTable? table = await db.LockTableAsync(tableId, ct);
        if (table is null || table.DeletedAtUtc is not null)
        {
            return DiningResults.NotFound();
        }

        if (
            !access.CanAccessRestaurant(
                principal,
                table.RestaurantId,
                DiningPermission.TablesManage
            )
        )
        {
            return DiningResults.Forbid();
        }
        bool occupied = await db.HasOpenSessionAsync(tableId, ct);
        if (occupied && !request.IsActive)
        {
            return DiningResults.Conflict(
                new { detail = "An occupied location cannot be disabled." }
            );
        }
        DiningZone? zone = await db.LockZoneAsync(table.RestaurantId, request.ZoneId, ct);
        if (zone is null || zone.DeletedAtUtc is not null)
        {
            return DiningResults.BadRequest(
                new { detail = "Select a zone belonging to this restaurant." }
            );
        }

        table.ZoneId = zone.Id;
        table.Zone = zone;
        table.RequestGuestCount = request.RequestGuestCount;
        table.Label = request.Label.Trim();
        table.IsActive = request.IsActive;
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DiningStoreException e) when (e.Failure == DiningStoreFailure.ForeignKey)
        {
            return DiningResults.Conflict(
                new { detail = "The selected zone was deleted. Refresh and retry." }
            );
        }
        DiningSession? session = occupied
            ? await db.ReadRequiredActiveSessionAsync(tableId, ct)
            : null;
        return DiningResults.Ok(TableResponse(table, session));
    }

    public async Task<DiningResult> ListTables(
        Guid restaurantId,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        if (!access.CanAccessRestaurant(principal, restaurantId, DiningPermission.TablesRead))
        {
            return DiningResults.Forbid();
        }

        Dictionary<Guid, DiningSession> active = await db.ReadActiveSessionsAsync(restaurantId, ct);
        List<RestaurantTable> tables = await db.ListTablesAsync(restaurantId, ct);
        return DiningResults.Ok(
            tables.Select(x => TableResponse(x, active.GetValueOrDefault(x.Id)))
        );
    }

    public async Task<DiningResult> RotateQr(
        Guid tableId,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        await using IDiningTransaction transaction = await db.BeginTransactionAsync(ct);
        RestaurantTable? table = await db.LockTableAsync(tableId, ct);
        if (table is null || table.DeletedAtUtc is not null)
        {
            return DiningResults.NotFound();
        }

        if (
            !access.CanAccessRestaurant(
                principal,
                table.RestaurantId,
                DiningPermission.TablesManage
            )
        )
        {
            return DiningResults.Forbid();
        }

        table.QrCode = NewQrCode();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return DiningResults.Ok(QrResponse(table));
    }
}
