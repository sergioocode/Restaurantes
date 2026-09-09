using System.Security.Claims;
using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Application;

public sealed partial class DiningService
{
    public async Task<DiningResult> DeleteTable(
        Guid tableId,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        await using IDiningTransaction transaction = await db.BeginTransactionAsync(ct);
        // Session creation takes this same row lock, so a table cannot be deleted while opening it.
        RestaurantTable? table = await db.LockTableAsync(tableId, ct);
        if (table is null)
        {
            return DiningResults.NotFound();
        }

        if (!access.CanAccessRestaurant(principal, table.RestaurantId, DiningPermission.TablesManage))
        {
            return DiningResults.Forbid();
        }

        if (table.DeletedAtUtc is not null)
        {
            return DiningResults.NoContent();
        }

        if (await db.HasOpenSessionAsync(tableId, ct))
        {
            return DiningResults.Conflict(
                new { detail = "La ubicación está en uso. Cierra su sesión antes de eliminarla." }
            );
        }

        table.DeletedAtUtc = time.GetUtcNow().UtcDateTime;
        table.IsActive = false;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return DiningResults.NoContent();
    }
}
