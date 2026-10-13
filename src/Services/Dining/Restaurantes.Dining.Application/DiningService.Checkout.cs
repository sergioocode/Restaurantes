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

        bool canCaptureAnyPayment = access.CanAccessRestaurant(
            principal,
            session.RestaurantId,
            DiningPermission.PaymentsCapture
        );
        bool canCaptureCardPayment =
            request.Method == "Card"
            && access.CanAccessRestaurant(
                principal,
                session.RestaurantId,
                DiningPermission.PaymentsCaptureCard
            );
        if (!canCaptureAnyPayment && !canCaptureCardPayment)
        {
            return DiningResults.Forbid();
        }

        if (session.Status != "Open")
        {
            return session.CheckoutIdempotencyKey == request.IdempotencyKey
                ? DiningResults.Ok(BillResponse(session))
                : DiningResults.Conflict(new { detail = "The dining session is already closed." });
        }
        if (session.CheckoutIdempotencyKey == request.IdempotencyKey)
        {
            return DiningResults.Ok(BillResponse(session));
        }
        if (session.Orders.Count == 0)
        {
            return DiningResults.Conflict(
                new { detail = "The dining session has no projected orders." }
            );
        }

        Dictionary<Guid, decimal> chargeableAmounts = ChargeableAmounts(session);
        List<DiningSessionOrder> unpaidOrders = session
            .Orders.Where(x =>
                x.AmountPaid < chargeableAmounts[x.OrderId] && x.OrderStatus != "Cancelled"
            )
            .ToList();
        Guid? currentUserId = Guid.TryParse(
            principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            out Guid parsedUserId
        )
            ? parsedUserId
            : null;
        List<DiningSessionOrder> payableOrders =
            canCaptureAnyPayment ? unpaidOrders
            : canCaptureCardPayment && currentUserId is Guid userId
                ? unpaidOrders
                    .Where(order => order.Source == "Comandero" && order.CreatedByUserId == userId)
                    .ToList()
            : [];
        if (payableOrders.Count == 0)
        {
            return canCaptureAnyPayment
                ? DiningResults.Conflict(new { detail = "There are no unpaid session orders." })
                : DiningResults.Forbid();
        }

        List<CheckoutOrderAllocation> allocations =
            request.Allocations.Count == 0
                ? payableOrders
                    .Select(order => new CheckoutOrderAllocation(
                        order.OrderId,
                        chargeableAmounts[order.OrderId] - order.AmountPaid
                    ))
                    .ToList()
                : request.Allocations;
        if (
            allocations.Any(allocation => allocation.Amount <= 0)
            || allocations.Select(allocation => allocation.OrderId).Distinct().Count()
                != allocations.Count
            || allocations.Any(allocation =>
                !payableOrders.Any(order =>
                    order.OrderId == allocation.OrderId
                    && allocation.Amount <= chargeableAmounts[order.OrderId] - order.AmountPaid
                )
            )
        )
        {
            return DiningResults.BadRequest(new { detail = "Invalid payment allocations." });
        }

        decimal allocatedAmount = allocations.Sum(allocation => allocation.Amount);
        if (allocatedAmount <= 0 || request.TipAmount < 0)
        {
            return DiningResults.BadRequest(new { detail = "Invalid payment amount or tip." });
        }

        decimal amountDue = allocatedAmount + request.TipAmount;
        decimal amountReceived = request.AmountReceived ?? amountDue;
        if (
            amountReceived < amountDue
            || amountReceived != decimal.Round(amountReceived, 2)
            || request.Method == "Transfer" && amountReceived != amountDue
        )
        {
            return DiningResults.BadRequest(new { detail = "Invalid amount received." });
        }
        decimal changeAmount = amountReceived - amountDue;

        string? authorization = httpContext.Authorization;
        foreach (DiningSessionOrder order in unpaidOrders)
        {
            decimal chargeableAmount = chargeableAmounts[order.OrderId];
            if (chargeableAmount != order.Amount)
            {
                DiningPaymentResult adjusted = await payments.AdjustAmountAsync(
                    order.OrderId,
                    session.Id,
                    chargeableAmount,
                    authorization,
                    ct
                );
                if (!adjusted.Succeeded)
                {
                    return DiningResults.Conflict(
                        new
                        {
                            detail = $"No se pudo actualizar el importe del pedido '{order.OrderId}' para aplicar el descuento.",
                        }
                    );
                }
            }
        }
        decimal allocatedTip = 0;
        for (int index = 0; index < allocations.Count; index++)
        {
            CheckoutOrderAllocation allocation = allocations[index];
            DiningSessionOrder order = unpaidOrders.Single(x => x.OrderId == allocation.OrderId);
            decimal remainingTip = Math.Max(0, request.TipAmount - allocatedTip);
            decimal tip =
                index == allocations.Count - 1
                    ? remainingTip
                    : Math.Min(
                        remainingTip,
                        Math.Round(
                            request.TipAmount * allocation.Amount / allocatedAmount,
                            2,
                            MidpointRounding.AwayFromZero
                        )
                    );
            allocatedTip += tip;
            DiningPaymentResult response = await payments.CaptureAsync(
                allocation.OrderId,
                new DiningPaymentRequest(
                    request.IdempotencyKey,
                    session.Orders.Count(x => x.OrderStatus != "Cancelled"),
                    allocation.Amount,
                    tip,
                    request.Method,
                    string.IsNullOrWhiteSpace(request.ExternalReference)
                        ? $"DINING-{session.Id:N}"
                        : request.ExternalReference.Trim(),
                    allocation.Amount + tip + (index == 0 ? changeAmount : 0),
                    session.Id,
                    session.DiscountPercentage > 0 ? "Descuento" : null,
                    session.DiscountPercentage,
                    order.Amount - chargeableAmounts[order.OrderId]
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
            order.AmountPaid = response.AmountPaid;
            order.TipPaid = response.TipAmountPaid;
            order.PaymentStatus =
                order.AmountPaid >= chargeableAmounts[order.OrderId] ? "Paid" : "PartiallyPaid";
            order.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        session.RecordPayment(request.IdempotencyKey, request.Method, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return DiningResults.Ok(BillResponse(session));
    }

    private static object BillResponse(DiningSession session)
    {
        Dictionary<Guid, decimal> chargeableAmounts = ChargeableAmounts(session);
        decimal consumption = session
            .Orders.Where(x => x.OrderStatus != "Cancelled")
            .Sum(x => chargeableAmounts[x.OrderId]);
        decimal discountAmount =
            session.Orders.Where(x => x.OrderStatus != "Cancelled").Sum(x => x.Amount)
            - consumption;
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
            total = consumption + session.Orders.Sum(x => x.TipPaid),
            session.DiscountPercentage,
            discountAmount = Math.Max(0, discountAmount),
            tipTotal = session.Orders.Sum(x => x.TipPaid),
            outstanding = session
                .Orders.Where(x => x.OrderStatus != "Cancelled")
                .Sum(x => Math.Max(0, chargeableAmounts[x.OrderId] - x.AmountPaid)),
            canCheckout = session.Status == "Open"
                && session.Orders.Any(order =>
                    order.OrderStatus != "Cancelled"
                    && order.AmountPaid < chargeableAmounts[order.OrderId]
                ),
            canRelease = session.Status == "Open"
                && session.Orders.Count > 0
                && session.Orders.All(order => order.OrderStatus is "Delivered" or "Cancelled")
                && session.Orders.All(order =>
                    order.AmountPaid >= chargeableAmounts[order.OrderId]
                    || order.OrderStatus == "Cancelled"
                ),
            orders = session
                .Orders.OrderBy(x => x.AddedAtUtc)
                .Select(x => new
                {
                    x.OrderId,
                    x.Source,
                    x.CreatedByUserId,
                    x.OrderStatus,
                    x.PaymentStatus,
                    amount = chargeableAmounts[x.OrderId],
                    x.AmountPaid,
                    x.TipPaid,
                    x.AddedAtUtc,
                }),
        };
    }

    private static Dictionary<Guid, decimal> ChargeableAmounts(DiningSession session)
    {
        decimal factor = 1m - session.DiscountPercentage / 100m;
        return session.Orders.ToDictionary(
            order => order.OrderId,
            order => Math.Round(order.Amount * factor, 2, MidpointRounding.AwayFromZero)
        );
    }
}
