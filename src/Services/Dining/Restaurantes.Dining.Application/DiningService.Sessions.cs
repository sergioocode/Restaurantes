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

        if (
            !access.CanAccessRestaurant(
                principal,
                table.RestaurantId,
                DiningPermission.OrdersCreate
            )
        )
        {
            return DiningResults.Forbid();
        }

        if (!table.IsActive)
        {
            return DiningResults.Conflict(new { detail = "The table is not active." });
        }
        if (
            !Guid.TryParse(
                principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                out Guid openedByUserId
            )
        )
        {
            return DiningResults.Forbid();
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
            OpenedByUserId = openedByUserId,
            RequestGuestCount = table.Zone.RequestGuestCount ?? false,
            Source = request.Source,
            OpenedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        db.Add(session);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return DiningResults.Created(
                $"/api/dining/sessions/{session.Id}",
                SessionResponse(session)
            );
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
            : access.CanAccessRestaurant(
                principal,
                session.RestaurantId,
                DiningPermission.TablesRead
            )
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
            : access.CanAccessRestaurant(
                principal,
                session.RestaurantId,
                DiningPermission.TablesRead
            )
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
        DiningSession? session = await db.ReadValidatedSessionAsync(
            sessionId,
            restaurantId,
            tableId,
            ct
        );
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

        bool canReleaseAnySession = access.CanAccessRestaurant(
            principal,
            session.RestaurantId,
            DiningPermission.TablesRelease
        );
        bool allOrdersCancelled =
            session.Orders.Count > 0
            && session.Orders.All(order => order.OrderStatus == "Cancelled");
        bool hasCurrentUser = Guid.TryParse(
            principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            out Guid userId
        );
        bool hasCurrentUserCommanderOrder =
            hasCurrentUser
            && session.Orders.Any(order =>
                order.Source == "Comandero"
                && order.CreatedByUserId == userId
                && order.OrderStatus != "Cancelled"
            );
        bool hasCurrentUserCancelledCommanderOrder =
            hasCurrentUser
            && session.Orders.Any(order =>
                order.Source == "Comandero"
                && order.CreatedByUserId == userId
                && order.OrderStatus == "Cancelled"
            );
        bool canReleasePaidSession =
            session.CanRelease
            && (
                hasCurrentUserCommanderOrder
                || (allOrdersCancelled && hasCurrentUserCancelledCommanderOrder)
            )
            && access.CanAccessRestaurant(
                principal,
                session.RestaurantId,
                DiningPermission.TablesReleasePaid
            );
        if (!canReleaseAnySession && !canReleasePaidSession)
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

        if (
            !allOrdersCancelled
            && !session.Orders.Any(order =>
                order.OrderStatus != "Cancelled" && order.AmountPaid > 0
            )
        )
        {
            return DiningResults.Conflict(
                new { detail = "The table can only be released after a payment has been recorded." }
            );
        }

        Guid[] unfinished = session
            .Orders.Where(x => x.OrderStatus is not ("Delivered" or "Cancelled"))
            .Select(x => x.OrderId)
            .ToArray();
        Guid[] unpaid = session
            .Orders.Where(x =>
                x.OrderStatus != "Cancelled" && x.AmountPaid < session.ChargeableAmount(x)
            )
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
        if (unfinished.Length > 0)
        {
            return DiningResults.Conflict(
                new
                {
                    detail = "All active session orders must be delivered or cancelled before releasing the table.",
                    unfinishedOrderIds = unfinished,
                }
            );
        }
        if (!session.CanRelease)
        {
            return DiningResults.Conflict(
                new { detail = "The table is not ready to be released." }
            );
        }

        session.Close(time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return DiningResults.Ok(SessionResponse(session));
    }

    public async Task<DiningResult> CancelSession(
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
            !Guid.TryParse(
                principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                out Guid cancelledByUserId
            )
        )
        {
            return DiningResults.Forbid();
        }

        bool canReleaseAnySession = access.CanAccessRestaurant(
            principal,
            session.RestaurantId,
            DiningPermission.TablesRelease
        );
        bool canCancelOwnEmptySession =
            session.OpenedByUserId == cancelledByUserId
            && access.CanAccessRestaurant(
                principal,
                session.RestaurantId,
                DiningPermission.OrdersCreate
            );
        if (!canReleaseAnySession && !canCancelOwnEmptySession)
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

        DateTime now = time.GetUtcNow().UtcDateTime;
        session.Cancel(cancelledByUserId, now);
        await db.SaveChangesAsync(ct);
        return DiningResults.Ok(SessionResponse(session));
    }
}
