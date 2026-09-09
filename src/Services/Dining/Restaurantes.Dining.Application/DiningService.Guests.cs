using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Application;

public sealed partial class DiningService
{
    public async Task<DiningResult> SetGuestCount(
        Guid sessionId,
        SetGuestCountRequest request,
        DiningRequest http,
        CancellationToken ct
    )
    {
        await using IDiningTransaction transaction = await db.BeginTransactionAsync(ct);
        DiningSession? session = await db.LockSessionAsync(sessionId, ct);
        if (session is null)
        {
            return DiningResults.NotFound();
        }

        bool staff =
            http.User.Identity?.IsAuthenticated == true
            && access.CanAccessRestaurant(
                http.User,
                session.RestaurantId,
                DiningPermission.OrdersCreate
            );
        bool customer =
            session.Source == "CustomerQr"
            && TokenEquals(session.CustomerAccessToken, http.CustomerSessionToken);
        if (!staff && !customer)
        {
            return DiningResults.Unauthorized();
        }

        if (session.Status != "Open")
        {
            return DiningResults.Conflict(new { detail = "La sesión ya está cerrada." });
        }

        if (request.GuestCount is < 1 or > 999)
        {
            return DiningResults.BadRequest(new { detail = "Indica entre 1 y 999 comensales." });
        }

        if (session.GuestCount is not null && session.GuestCount != request.GuestCount)
        {
            return DiningResults.Conflict(
                new
                {
                    detail = "La cantidad de comensales de esta sesión ya está registrada. Actualiza la sesión.",
                }
            );
        }

        session.RegisterGuests(request.GuestCount);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return DiningResults.Ok(SessionResponse(session));
    }
}
