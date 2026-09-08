using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Restaurantes.Security;

namespace Restaurantes.Dining.Api.Write;

public sealed record SetGuestCountRequest(int GuestCount);

public static partial class DiningEndpoints
{
    private static async Task<IResult> SetGuestCount(
        Guid sessionId,
        SetGuestCountRequest request,
        HttpContext http,
        DiningDbContext db,
        CancellationToken ct
    )
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        DiningSession? session = await db
            .Sessions.FromSqlInterpolated(
                $"SELECT * FROM dining_sessions WHERE \"Id\" = {sessionId} FOR UPDATE"
            )
            .SingleOrDefaultAsync(ct);
        if (session is null)
        {
            return Results.NotFound();
        }

        bool staff =
            http.User.Identity?.IsAuthenticated == true
            && http.User.CanAccessRestaurant(
                session.RestaurantId,
                RestaurantPermissions.OrdersCreate
            );
        bool customer =
            session.Source == "CustomerQr"
            && TokenEquals(
                session.CustomerAccessToken,
                http.Request.Headers["X-Customer-Session-Token"].FirstOrDefault()
            );
        if (!staff && !customer)
        {
            return Results.Unauthorized();
        }

        if (session.Status != "Open")
        {
            return Results.Conflict(new { detail = "La sesión ya está cerrada." });
        }

        if (request.GuestCount is < 1 or > 999)
        {
            return Results.BadRequest(new { detail = "Indica entre 1 y 999 comensales." });
        }

        if (session.GuestCount is not null && session.GuestCount != request.GuestCount)
        {
            return Results.Conflict(
                new
                {
                    detail = "La cantidad de comensales de esta sesión ya está registrada. Actualiza la sesión.",
                }
            );
        }

        session.GuestCount = request.GuestCount;
        session.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(SessionResponse(session));
    }
}
