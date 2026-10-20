using System.Security.Claims;
using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Application;

public sealed partial class DiningService
{
    private const string CancelledOrderStatus = "Cancelled";

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

        DiningResult? sessionFailure = ValidateCheckoutSession(
            session,
            request,
            principal,
            out bool canCaptureAnyPayment,
            out bool canCaptureCardPayment
        );
        if (sessionFailure is not null)
        {
            return sessionFailure;
        }

        (CheckoutCaptureContext? context, DiningResult? preparationFailure) = PrepareCheckout(
            session,
            request,
            principal,
            canCaptureAnyPayment,
            canCaptureCardPayment,
            httpContext.Authorization
        );
        if (preparationFailure is not null)
        {
            return preparationFailure;
        }

        DiningResult? paymentFailure = await ExecuteCheckoutAsync(context!, ct);
        if (paymentFailure is not null)
        {
            return paymentFailure;
        }

        await transaction.CommitAsync(ct);
        return DiningResults.Ok(BillResponse(session));
    }

    private DiningResult? ValidateCheckoutSession(
        DiningSession session,
        CheckoutSessionRequest request,
        ClaimsPrincipal principal,
        out bool canCaptureAnyPayment,
        out bool canCaptureCardPayment
    )
    {
        canCaptureAnyPayment = access.CanAccessRestaurant(
            principal,
            session.RestaurantId,
            DiningPermission.PaymentsCapture
        );
        canCaptureCardPayment =
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

        return null;
    }

    private static (CheckoutCaptureContext? Context, DiningResult? Failure) PrepareCheckout(
        DiningSession session,
        CheckoutSessionRequest request,
        ClaimsPrincipal principal,
        bool canCaptureAnyPayment,
        bool canCaptureCardPayment,
        string? authorization
    )
    {
        Dictionary<Guid, decimal> chargeableAmounts = ChargeableAmounts(session);
        List<DiningSessionOrder> unpaidOrders = session
            .Orders.Where(order =>
                order.OrderStatus != CancelledOrderStatus
                && order.AmountPaid < chargeableAmounts[order.OrderId]
            )
            .ToList();
        List<DiningSessionOrder> payableOrders = SelectPayableOrders(
            unpaidOrders,
            principal,
            canCaptureAnyPayment,
            canCaptureCardPayment
        );
        if (payableOrders.Count == 0)
        {
            return (
                null,
                canCaptureAnyPayment
                    ? DiningResults.Conflict(new { detail = "There are no unpaid session orders." })
                    : DiningResults.Forbid()
            );
        }

        if (
            !TryGetAllocations(
                request.Allocations,
                payableOrders,
                chargeableAmounts,
                out List<CheckoutOrderAllocation> allocations
            )
        )
        {
            return (
                null,
                DiningResults.BadRequest(new { detail = "Invalid payment allocations." })
            );
        }

        decimal allocatedAmount = allocations.Sum(allocation => allocation.Amount);
        if (allocatedAmount <= 0 || request.TipAmount < 0)
        {
            return (
                null,
                DiningResults.BadRequest(new { detail = "Invalid payment amount or tip." })
            );
        }

        if (!TryGetPaymentAmounts(request, allocatedAmount, out decimal changeAmount))
        {
            return (null, DiningResults.BadRequest(new { detail = "Invalid amount received." }));
        }

        return (
            new CheckoutCaptureContext(
                allocations,
                AllocateTips(allocations, allocatedAmount, request.TipAmount),
                unpaidOrders,
                chargeableAmounts,
                session,
                request,
                changeAmount,
                authorization
            ),
            null
        );
    }

    private static List<DiningSessionOrder> SelectPayableOrders(
        List<DiningSessionOrder> unpaidOrders,
        ClaimsPrincipal principal,
        bool canCaptureAnyPayment,
        bool canCaptureCardPayment
    )
    {
        if (canCaptureAnyPayment)
        {
            return unpaidOrders;
        }

        if (
            canCaptureCardPayment
            && Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out Guid userId)
        )
        {
            return unpaidOrders
                .Where(order => order.Source == "Comandero" && order.CreatedByUserId == userId)
                .ToList();
        }

        return [];
    }

    private async Task<DiningResult?> ExecuteCheckoutAsync(
        CheckoutCaptureContext context,
        CancellationToken ct
    )
    {
        DiningResult? adjustmentFailure = await AdjustOrderAmountsAsync(
            context.UnpaidOrders,
            context.OrderChargeableAmounts,
            context.Session,
            context.Authorization,
            ct
        );
        if (adjustmentFailure is not null)
        {
            return adjustmentFailure;
        }

        DiningResult? captureFailure = await CaptureAllocationsAsync(context, ct);
        if (captureFailure is not null)
        {
            return captureFailure;
        }

        context.Session.RecordPayment(
            context.Request.IdempotencyKey,
            context.Request.Method,
            time.GetUtcNow().UtcDateTime
        );
        await db.SaveChangesAsync(ct);
        return null;
    }

    private static bool TryGetAllocations(
        List<CheckoutOrderAllocation> requestedAllocations,
        List<DiningSessionOrder> payableOrders,
        Dictionary<Guid, decimal> chargeableAmounts,
        out List<CheckoutOrderAllocation> allocations
    )
    {
        allocations =
            requestedAllocations.Count == 0
                ? payableOrders
                    .Select(order => new CheckoutOrderAllocation(
                        order.OrderId,
                        chargeableAmounts[order.OrderId] - order.AmountPaid
                    ))
                    .ToList()
                : requestedAllocations;
        return !allocations.Any(allocation => allocation.Amount <= 0)
            && allocations.Select(allocation => allocation.OrderId).Distinct().Count()
                == allocations.Count
            && !allocations.Any(allocation =>
                !payableOrders.Any(order =>
                    order.OrderId == allocation.OrderId
                    && allocation.Amount <= chargeableAmounts[order.OrderId] - order.AmountPaid
                )
            );
    }

    private static bool TryGetPaymentAmounts(
        CheckoutSessionRequest request,
        decimal allocatedAmount,
        out decimal changeAmount
    )
    {
        decimal amountDue = allocatedAmount + request.TipAmount;
        decimal amountReceived = request.AmountReceived ?? amountDue;
        changeAmount = amountReceived - amountDue;
        return amountReceived >= amountDue
            && amountReceived == decimal.Round(amountReceived, 2)
            && (request.Method != "Transfer" || amountReceived == amountDue);
    }

    private async Task<DiningResult?> AdjustOrderAmountsAsync(
        List<DiningSessionOrder> unpaidOrders,
        Dictionary<Guid, decimal> chargeableAmounts,
        DiningSession session,
        string? authorization,
        CancellationToken ct
    )
    {
        foreach (DiningSessionOrder order in unpaidOrders)
        {
            decimal chargeableAmount = chargeableAmounts[order.OrderId];
            if (chargeableAmount == order.Amount)
            {
                continue;
            }

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

        return null;
    }

    private async Task<DiningResult?> CaptureAllocationsAsync(
        CheckoutCaptureContext context,
        CancellationToken ct
    )
    {
        for (int index = 0; index < context.Allocations.Count; index++)
        {
            CheckoutOrderAllocation allocation = context.Allocations[index];
            DiningSessionOrder order = context.UnpaidOrders.Single(x =>
                x.OrderId == allocation.OrderId
            );
            DiningPaymentResult response = await payments.CaptureAsync(
                allocation.OrderId,
                CreatePaymentRequest(
                    allocation,
                    context.Tips[index],
                    index == 0 ? context.ChangeAmount : 0,
                    order,
                    context.OrderChargeableAmounts[order.OrderId],
                    context.Session,
                    context.Request
                ),
                context.Authorization,
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
                order.AmountPaid >= context.OrderChargeableAmounts[order.OrderId]
                    ? "Paid"
                    : "PartiallyPaid";
            order.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        }

        return null;
    }

    private static DiningPaymentRequest CreatePaymentRequest(
        CheckoutOrderAllocation allocation,
        decimal tip,
        decimal changeAmount,
        DiningSessionOrder order,
        decimal chargeableAmount,
        DiningSession session,
        CheckoutSessionRequest request
    )
    {
        return new DiningPaymentRequest(
            request.IdempotencyKey,
            session.Orders.Count(x => x.OrderStatus != CancelledOrderStatus),
            allocation.Amount,
            tip,
            request.Method,
            string.IsNullOrWhiteSpace(request.ExternalReference)
                ? $"DINING-{session.Id:N}"
                : request.ExternalReference.Trim(),
            allocation.Amount + tip + changeAmount,
            session.Id,
            session.DiscountPercentage > 0 ? "Descuento" : null,
            session.DiscountPercentage,
            order.Amount - chargeableAmount
        );
    }

    private static decimal[] AllocateTips(
        List<CheckoutOrderAllocation> allocations,
        decimal allocatedAmount,
        decimal tipAmount
    )
    {
        decimal[] tips = new decimal[allocations.Count];
        decimal allocatedTip = 0;
        for (int index = 0; index < allocations.Count; index++)
        {
            decimal remainingTip = Math.Max(0, tipAmount - allocatedTip);
            tips[index] =
                index == allocations.Count - 1
                    ? remainingTip
                    : Math.Min(
                        remainingTip,
                        Math.Round(
                            tipAmount * allocations[index].Amount / allocatedAmount,
                            2,
                            MidpointRounding.AwayFromZero
                        )
                    );
            allocatedTip += tips[index];
        }

        return tips;
    }

    private sealed record CheckoutCaptureContext(
        List<CheckoutOrderAllocation> Allocations,
        decimal[] Tips,
        List<DiningSessionOrder> UnpaidOrders,
        Dictionary<Guid, decimal> OrderChargeableAmounts,
        DiningSession Session,
        CheckoutSessionRequest Request,
        decimal ChangeAmount,
        string? Authorization
    );

    private static object BillResponse(DiningSession session)
    {
        Dictionary<Guid, decimal> chargeableAmounts = ChargeableAmounts(session);
        decimal consumption = session
            .Orders.Where(x => x.OrderStatus != CancelledOrderStatus)
            .Sum(x => chargeableAmounts[x.OrderId]);
        decimal discountAmount =
            session.Orders.Where(x => x.OrderStatus != CancelledOrderStatus).Sum(x => x.Amount)
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
                .Orders.Where(x => x.OrderStatus != CancelledOrderStatus)
                .Sum(x => Math.Max(0, chargeableAmounts[x.OrderId] - x.AmountPaid)),
            canCheckout = session.Status == "Open"
                && session.Orders.Any(order =>
                    order.OrderStatus != CancelledOrderStatus
                    && order.AmountPaid < chargeableAmounts[order.OrderId]
                ),
            canRelease = session.Status == "Open"
                && session.Orders.Count > 0
                && session.Orders.All(order =>
                    order.OrderStatus is "Delivered" or CancelledOrderStatus
                )
                && session.Orders.All(order =>
                    order.AmountPaid >= chargeableAmounts[order.OrderId]
                    || order.OrderStatus == CancelledOrderStatus
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
