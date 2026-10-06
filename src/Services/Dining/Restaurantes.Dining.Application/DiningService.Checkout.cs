using System.Security.Claims;
using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Application;

public sealed partial class DiningService
{
    public async Task<DiningResult> GetBill(
        Guid sessionId,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        DiningSession? session = await db.ReadSessionWithOrdersAsync(sessionId, ct);
        return session is null ? DiningResults.NotFound()
            : !access.CanAccessRestaurant(
                principal,
                session.RestaurantId,
                DiningPermission.TablesRead
            )
                ? DiningResults.Forbid()
            : DiningResults.Ok(BillResponse(session));
    }

    public async Task<DiningResult> Checkout(
        Guid sessionId,
        CheckoutSessionRequest request,
        ClaimsPrincipal principal,
        DiningRequest httpContext,
        CancellationToken ct
    )
    {
        if (request.IdempotencyKey == Guid.Empty)
        {
            return DiningResults.BadRequest(new { detail = "IdempotencyKey is required." });
        }

        await using IDiningTransaction transaction = await db.BeginTransactionAsync(ct);
        DiningSession? session = await db.LockSessionAsync(sessionId, ct);
        if (session is null)
        {
            return DiningResults.NotFound();
        }

        await db.LoadOrdersAsync(session, ct);

        if (
            !access.CanAccessRestaurant(
                principal,
                session.RestaurantId,
                DiningPermission.PaymentsCapture
            )
        )
        {
            return DiningResults.Forbid();
        }

        if (session.Status == "Closed")
        {
            return session.CheckoutIdempotencyKey == request.IdempotencyKey
                ? DiningResults.Ok(BillResponse(session))
                : DiningResults.Conflict(new { detail = "The dining session is already closed." });
        }
        if (session.Orders.Count == 0)
        {
            return DiningResults.Conflict(
                new { detail = "The dining session has no projected orders." }
            );
        }

        List<DiningSessionOrder> unpaidOrders = session
            .Orders.Where(x => x.PaymentStatus != "Paid" && x.OrderStatus != "Cancelled")
            .ToList();
        string? authorization = httpContext.Authorization;
        foreach (DiningSessionOrder order in unpaidOrders)
        {
            DiningPaymentResult response = await payments.CaptureAsync(
                order.OrderId,
                new DiningPaymentRequest(
                    request.IdempotencyKey,
                    unpaidOrders.Count,
                    request.Method,
                    string.IsNullOrWhiteSpace(request.ExternalReference)
                        ? $"DINING-{session.Id:N}"
                        : request.ExternalReference.Trim()
                ),
                authorization,
                ct
            );
            if (!response.Succeeded)
            {
                return DiningResults.Conflict(
                    new
                    {
                        detail = $"Payment for order '{order.OrderId}' was rejected with HTTP {response.StatusCode}.",
                        paymentResponse = response.Body,
                    }
                );
            }
            order.PaymentStatus = "Paid";
            order.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        session.CompleteCheckout(request.IdempotencyKey, request.Method, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await PublishTableChanged(realtime, session, "Available", time);
        return DiningResults.Ok(BillResponse(session));
    }

    private static object BillResponse(DiningSession session)
    {
        return new
        {
            session.Id,
            session.RestaurantId,
            session.TableId,
            session.RequestGuestCount,
            session.GuestCount,
            session.Status,
            session.PaymentMethod,
            session.PaidAtUtc,
            total = session.Orders.Sum(x => x.Amount),
            outstanding = session.Orders.Where(x => x.PaymentStatus != "Paid").Sum(x => x.Amount),
            canCheckout = session.CanCheckout,
            orders = session
                .Orders.OrderBy(x => x.AddedAtUtc)
                .Select(x => new
                {
                    x.OrderId,
                    x.OrderStatus,
                    x.PaymentStatus,
                    x.Amount,
                    x.AddedAtUtc,
                }),
        };
    }
}
