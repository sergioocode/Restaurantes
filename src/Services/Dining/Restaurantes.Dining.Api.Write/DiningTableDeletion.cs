using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Restaurantes.Security;

namespace Restaurantes.Dining.Api.Write;

public static partial class DiningEndpoints
{
    private static async Task<IResult> DeleteTable(
        Guid tableId,
        ClaimsPrincipal principal,
        DiningDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        // Session creation takes this same row lock, so a table cannot be deleted while opening it.
        RestaurantTable? table = await db
            .Tables.FromSqlInterpolated(
                $"SELECT * FROM restaurant_tables WHERE \"Id\" = {tableId} FOR UPDATE"
            )
            .SingleOrDefaultAsync(ct);
        if (table is null)
        {
            return Results.NotFound();
        }

        if (!principal.CanAccessRestaurant(table.RestaurantId, RestaurantPermissions.TablesManage))
        {
            return Results.Forbid();
        }

        if (table.DeletedAtUtc is not null)
        {
            return Results.NoContent();
        }

        if (await db.Sessions.AnyAsync(x => x.TableId == tableId && x.Status == "Open", ct))
        {
            return Results.Conflict(
                new { detail = "La ubicación está en uso. Cierra su sesión antes de eliminarla." }
            );
        }

        table.DeletedAtUtc = time.GetUtcNow().UtcDateTime;
        table.IsActive = false;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }
}
