using Restaurantes.Payments.Contracts;
using Restaurantes.Payments.Domain;

namespace Restaurantes.Payments.Application;

public sealed class PaymentCommandService(IPaymentWriteStore store, TimeProvider time)
{
    public async Task<PaymentResponse?> CaptureAsync(
        Guid orderId,
        CapturePaymentRequest request,
        CancellationToken ct
    )
    {
        PayableOrder? order = await store.FindAsync(orderId, ct);
        if (order is null)
        {
            return null;
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        bool changed = order.Capture(
            request.Method,
            request.ExternalReference,
            request.IdempotencyKey,
            now
        );
        if (!changed)
        {
            return Map(order);
        }

        PaymentCaptured e = new(
            order.PaymentId!.Value,
            order.OrderId,
            order.RestaurantId,
            order.Amount,
            order.Method,
            order.ServiceMode,
            order.Source,
            order.ExternalReference,
            now,
            order.CaptureIdempotencyKey!.Value,
            request.TransactionOrderCount
        );
        await store.SaveWithEventAsync(order, e, ct);
        return Map(order);
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
            x.Status.ToString(),
            x.CapturedAtUtc,
            x.RefundedAtUtc
        );
    }
}
