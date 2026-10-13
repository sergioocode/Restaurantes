using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Payments.Contracts.Requests;
using Restaurantes.Payments.Contracts.Responses;
using Restaurantes.Payments.Domain;

namespace Restaurantes.Payments.Application;

public sealed class PaymentCommandService(IPaymentWriteStore store, TimeProvider time)
{
    public async Task<bool> AdjustAmountAsync(Guid orderId, decimal amount, CancellationToken ct)
    {
        PayableOrder? order = await store.FindAsync(orderId, ct);
        if (order is null)
        {
            return false;
        }
        if (amount == order.Amount)
        {
            return true;
        }
        if (amount <= 0 || order.AmountPaid > 0 || order.Status != PaymentStatus.Open)
        {
            throw new InvalidOperationException("Only unpaid open orders can receive a discount.");
        }
        order.AdjustAmount(amount);
        await store.SaveAsync(ct);
        return true;
    }

    public async Task<PaymentResponse?> CaptureAsync(
        Guid orderId,
        CapturePaymentRequest request,
        CashRegisterSessionDetails cashRegister,
        CancellationToken ct
    )
    {
        PayableOrder? order = await store.FindAsync(orderId, ct);
        if (order is null)
        {
            return null;
        }

        PayableOrderCapture? previousCapture = await store.FindCaptureAsync(
            orderId,
            request.IdempotencyKey,
            ct
        );
        if (previousCapture is not null)
        {
            return Map(order, previousCapture);
        }

        if (
            order.Status == PaymentStatus.Paid
            && order.CaptureIdempotencyKey == request.IdempotencyKey
        )
        {
            return Map(order);
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        decimal amount = request.Amount ?? order.OutstandingAmount;
        decimal amountDue = amount + request.TipAmount;
        decimal tenderedAmount = request.TenderedAmount ?? amountDue;
        if (
            tenderedAmount < amountDue
            || tenderedAmount != decimal.Round(tenderedAmount, 2)
            || (request.Method is "Transfer" or "Online") && tenderedAmount != amountDue
        )
        {
            throw new InvalidOperationException("Invalid amount received.");
        }
        PayableOrderCapture capture = order.Capture(
            amount,
            request.TipAmount,
            request.Method,
            request.ExternalReference,
            request.IdempotencyKey,
            now
        );

        PaymentCaptured e = new(
            capture.PaymentId,
            order.OrderId,
            order.RestaurantId,
            capture.Amount,
            capture.Method,
            order.ServiceMode,
            order.Source,
            capture.ExternalReference,
            now,
            capture.IdempotencyKey,
            request.TransactionOrderCount,
            cashRegister.SessionId,
            cashRegister.BusinessDate,
            capture.TipAmount,
            order.AmountPaid,
            order.Status == PaymentStatus.Paid,
            order.TipAmountPaid,
            tenderedAmount,
            tenderedAmount - amountDue,
            request.SaleGroupId,
            request.DiscountName,
            request.DiscountPercentage,
            request.DiscountAmount
        );
        await store.SaveWithEventAsync(order, capture, e, ct);
        return Map(order, capture);
    }

    public async Task<PaymentResponse?> RefundAsync(
        Guid orderId,
        RefundPaymentRequest request,
        CancellationToken ct
    )
    {
        PayableOrder? order = await store.FindAsync(orderId, ct);
        if (order is null)
        {
            return null;
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        order.Refund(now);
        PaymentRefunded e = new(
            order.PaymentId!.Value,
            order.OrderId,
            order.RestaurantId,
            order.Amount,
            order.Method,
            order.ServiceMode,
            order.Source,
            request.Reason,
            now
        );
        await store.SaveWithEventAsync(order, e, ct);
        return Map(order);
    }

    private static PaymentResponse Map(PayableOrder x)
    {
        return new(
            x.PaymentId ?? Guid.Empty,
            x.OrderId,
            x.RestaurantId,
            x.Amount,
            x.Method,
            PaymentStatusName(x),
            x.CapturedAtUtc,
            x.RefundedAtUtc,
            0,
            x.AmountPaid,
            x.OutstandingAmount,
            x.TipAmountPaid
        );
    }

    private static PaymentResponse Map(PayableOrder order, PayableOrderCapture capture)
    {
        return new(
            capture.PaymentId,
            capture.OrderId,
            order.RestaurantId,
            capture.Amount,
            capture.Method,
            PaymentStatusName(order),
            capture.CapturedAtUtc,
            order.RefundedAtUtc,
            capture.TipAmount,
            order.AmountPaid,
            order.OutstandingAmount,
            order.TipAmountPaid
        );
    }

    private static string PaymentStatusName(PayableOrder order) =>
        order.Status == PaymentStatus.Open && order.AmountPaid > 0
            ? "PartiallyPaid"
            : order.Status.ToString();
}
