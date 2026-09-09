using System.Security.Claims;
using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Application;

public sealed partial class DiningService
{
    public async Task<DiningResult> OpenSession(
        Guid tableId,
        OpenSessionRequest request,
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

        if (!access.CanAccessRestaurant(principal, table.RestaurantId, DiningPermission.OrdersCreate))
        {
            return DiningResults.Forbid();
        }

        if (!table.IsActive)
        {
            return DiningResults.Conflict(new { detail = "The table is not active." });
        }

        try
        {
            await cashRegister.EnsureOpenAsync(table.RestaurantId, ct);
        }
        catch (DiningCashRegisterClosedException exception)
        {
            return DiningResults.Conflict(new { detail = exception.Message });
        }

        DiningSession session = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = table.RestaurantId,
            TableId = table.Id,
            RequestGuestCount = table.RequestGuestCount,
            Source = request.Source,
            OpenedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        db.Add(session);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await PublishTableChanged(realtime, session, "Occupied", time);
            return DiningResults.Created($"/api/dining/sessions/{session.Id}", SessionResponse(session));
        }
        catch (DiningStoreException e) when (e.Failure == DiningStoreFailure.Duplicate)
        {
            return DiningResults.Conflict(
                new { detail = "This table already has an open dining session." }
            );
        }
    }

    public async Task<DiningResult> ActiveSession(
        Guid tableId,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        DiningSession? session = await db.ReadActiveSessionWithOrdersAsync(tableId, ct);
        return session is null ? DiningResults.NotFound()
            : access.CanAccessRestaurant(principal, session.RestaurantId, DiningPermission.TablesRead)
                ? DiningResults.Ok(SessionResponse(session))
            : DiningResults.Forbid();
    }

    public async Task<DiningResult> GetSession(
        Guid sessionId,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        DiningSession? session = await db.ReadSessionWithOrdersAsync(sessionId, ct);
        return session is null ? DiningResults.NotFound()
            : access.CanAccessRestaurant(principal, session.RestaurantId, DiningPermission.TablesRead)
                ? DiningResults.Ok(SessionResponse(session))
            : DiningResults.Forbid();
    }

    public async Task<DiningResult> ValidateSession(
        Guid sessionId,
        Guid restaurantId,
        Guid tableId,
        string? source,
        string? serviceMode,
        DiningRequest httpContext,
        CancellationToken ct
    )
    {
        DiningSession? session = await db.ReadValidatedSessionAsync(sessionId, restaurantId, tableId, ct);
        bool valid = session is not null;
        if (valid && session!.RequestGuestCount && session.GuestCount is null)
        {
            return DiningResults.Conflict(
                new { detail = "Indica la cantidad de comensales antes de pedir." }
            );
        }

        if (valid)
        {
            // Bar remains accepted for historical orders; locations no longer have types.
            valid = serviceMode is "DineIn" or "Bar";
        }
        if (valid && string.Equals(source, "CustomerQr", StringComparison.Ordinal))
        {
            string? customerAccessToken = httpContext.CustomerSessionToken;
            valid =
                session!.Source == "CustomerQr"
                && TokenEquals(session.CustomerAccessToken, customerAccessToken);
        }

        return valid
            ? DiningResults.NoContent(session!.GuestCount)
            : DiningResults.Conflict(
                new
                {
                    detail = "The dining session is not open or does not belong to this restaurant and table.",
                }
            );
    }

    public async Task<DiningResult> CloseSession(
        Guid sessionId,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        DiningSession? session = await db.FindSessionWithOrdersAsync(sessionId, ct);
        if (session is null)
        {
            return DiningResults.NotFound();
        }

        if (
            !access.CanAccessRestaurant(
                principal,
                session.RestaurantId,
                DiningPermission.TablesRelease
            )
        )
        {
            return DiningResults.Forbid();
        }

        if (session.Status != "Open")
        {
            return DiningResults.Conflict(new { detail = "The dining session is already closed." });
        }

        if (session.Orders.Count == 0)
        {
            return DiningResults.Conflict(new { detail = "The dining session has no orders." });
        }

        Guid[] unpaid = session
            .Orders.Where(x => x.PaymentStatus != "Paid" && x.OrderStatus != "Cancelled")
            .Select(x => x.OrderId)
            .ToArray();
        if (unpaid.Length > 0)
        {
            return DiningResults.Conflict(
                new
                {
                    detail = "All active session orders must be paid before releasing the table.",
                    unpaidOrderIds = unpaid,
                }
            );
        }

        session.Close(time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await PublishTableChanged(realtime, session, "Available", time);
        return DiningResults.Ok(SessionResponse(session));
    }

    public async Task<DiningResult> CancelSession(
        Guid sessionId,
        CancelSessionRequest request,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        DiningSession? session = await db.FindSessionWithOrdersAsync(sessionId, ct);
        if (session is null)
        {
            return DiningResults.NotFound();
        }

        if (
            !access.CanAccessRestaurant(
                principal,
                session.RestaurantId,
                DiningPermission.TablesRelease
            )
        )
        {
            return DiningResults.Forbid();
        }

        if (session.Status != "Open")
        {
            return DiningResults.Conflict(
                new { detail = "Only an open dining session can be cancelled." }
            );
        }

        if (session.Orders.Count > 0)
        {
            return DiningResults.Conflict(
                new
                {
                    detail = "A dining session with orders cannot be cancelled. Complete its payment and close it normally.",
                }
            );
        }

        string reason = request.Reason.Trim();
        if (reason.Length is < 3 or > 200)
        {
            return DiningResults.BadRequest(
                new { detail = "A cancellation reason between 3 and 200 characters is required." }
            );
        }

        if (
            !Guid.TryParse(
                principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                out Guid cancelledByUserId
            )
        )
        {
            return DiningResults.Forbid();
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        session.Cancel(reason, cancelledByUserId, now);
        await db.SaveChangesAsync(ct);
        await PublishTableChanged(realtime, session, "Available", time);
        return DiningResults.Ok(SessionResponse(session));
    }
}
