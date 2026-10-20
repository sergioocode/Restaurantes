using System.Text.Json;
using Restaurantes.Dining.Domain;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Payments.Contracts.Events;

namespace Restaurantes.Dining.Application;

public sealed class DiningIntegrationService(IDiningStore db, TimeProvider time)
{
    public async Task ProcessAsync(Guid messageId, string type, ReadOnlyMemory<byte> body)
    {
        if (await db.HasProcessedMessageAsync(messageId))
        {
            return;
        }

        Guid? updatedSessionId = null;
        if (type == typeof(OrderCreated).FullName)
        {
            await ApplyOrderCreated(db, body);
        }
        else if (type == typeof(OrderSubmitted).FullName)
        {
            updatedSessionId = await ApplyOrderSubmitted(db, body);
        }
        else if (type == typeof(KitchenTicketPreparationStarted).FullName)
        {
            updatedSessionId = await ApplyKitchenTicketPreparationStarted(db, body);
        }
        else if (type == typeof(OrderDelivered).FullName)
        {
            updatedSessionId = await ApplyOrderDelivered(db, body);
        }
        else if (type == typeof(OrderReady).FullName)
        {
            updatedSessionId = await ApplyOrderReady(db, body);
        }
        else if (type == typeof(KitchenTicketReady).FullName)
        {
            updatedSessionId = await ApplyKitchenTicketReady(db, body);
        }
        else if (type == typeof(KitchenTicketDispatched).FullName)
        {
            updatedSessionId = await ApplyKitchenTicketDispatched(db, body);
        }
        else if (type == typeof(OrderLineCancelled).FullName)
        {
            updatedSessionId = await ApplyOrderLineCancelled(db, body);
        }
        else if (type == typeof(OrderCancelled).FullName)
        {
            updatedSessionId = await ApplyOrderCancelled(db, body);
        }
        else if (type == typeof(PaymentCaptured).FullName)
        {
            await ApplyPayment(db, body, "Paid");
        }
        else if (type == typeof(PaymentRefunded).FullName)
        {
            await ApplyPayment(db, body, "Refunded");
        }
        else
        {
            throw new JsonException($"Unsupported integration event '{type}'.");
        }

        if (updatedSessionId is Guid sessionId)
        {
            await db.SaveChangesAsync();
        }
        db.MarkMessageProcessed(messageId, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
    }

    private static async Task ApplyOrderCreated(IDiningStore db, ReadOnlyMemory<byte> body)
    {
        OrderCreated e =
            JsonSerializer.Deserialize<OrderCreated>(body.Span)
            ?? throw new JsonException("OrderCreated is empty.");
        if (e.DiningSessionId is null)
        {
            return;
        }

        DiningSession? session = await db.FindSessionWithOrdersAsync(
            e.DiningSessionId.Value,
            CancellationToken.None
        );
        if (session is null)
        {
            throw new JsonException($"Dining session '{e.DiningSessionId}' does not exist.");
        }

        if (
            session.RestaurantId != e.RestaurantId
            || session.TableId != e.TableId
            || session.Status != "Open"
        )
        {
            throw new JsonException("Order does not match an open Dining session.");
        }

        if (await db.FindOrderAsync(e.OrderId) is not null)
        {
            return;
        }

        DiningPendingPayment? pending = await db.FindPendingPaymentAsync(e.OrderId);
        DiningSessionOrder projectedOrder = new()
        {
            OrderId = e.OrderId,
            DiningSessionId = e.DiningSessionId.Value,
            Source = e.Source,
            CreatedByUserId = e.CreatedByUserId,
            PaymentStatus =
                pending is null || pending.AmountPaid <= 0 ? "Unpaid"
                : pending.AmountPaid >= e.Lines.Sum(x => x.UnitPrice * x.Quantity) ? "Paid"
                : "PartiallyPaid",
            OrderStatus = "Draft",
            Amount = e.Lines.Sum(x => x.UnitPrice * x.Quantity),
            AmountPaid = pending?.AmountPaid ?? 0,
            TipPaid = pending?.TipPaid ?? 0,
            AddedAtUtc = e.OccurredAtUtc,
            UpdatedAtUtc = pending?.UpdatedAtUtc ?? e.OccurredAtUtc,
        };
        db.Add(projectedOrder);
        if (pending is not null)
        {
            db.RemovePendingPayment(pending);
        }
    }

    private static async Task<Guid?> ApplyOrderDelivered(IDiningStore db, ReadOnlyMemory<byte> body)
    {
        OrderDelivered e =
            JsonSerializer.Deserialize<OrderDelivered>(body.Span)
            ?? throw new JsonException("OrderDelivered is empty.");
        if (e.DiningSessionId is null)
        {
            return null;
        }
        DiningSessionOrder? order = await db.FindOrderAsync(e.OrderId);
        if (order is null)
        {
            throw new JsonException($"Dining order '{e.OrderId}' does not exist.");
        }

        order.OrderStatus = "Delivered";
        order.Amount = e
            .Lines.Where(x => x.Status != "Cancelled")
            .Sum(x => x.UnitPrice * x.Quantity);
        order.UpdatedAtUtc = e.DeliveredAtUtc;
        return e.DiningSessionId;
    }

    private static async Task<Guid?> ApplyOrderReady(IDiningStore db, ReadOnlyMemory<byte> body)
    {
        OrderReady e =
            JsonSerializer.Deserialize<OrderReady>(body.Span)
            ?? throw new JsonException("OrderReady is empty.");
        if (e.DiningSessionId is null)
        {
            return null;
        }
        DiningSessionOrder? order = await db.FindOrderAsync(e.OrderId);
        if (order is null)
        {
            throw new JsonException($"Dining order '{e.OrderId}' does not exist.");
        }
        order.OrderStatus = AllKitchenStationsDispatched(e.Stations) ? "Delivered" : "Ready";
        order.Amount = e
            .Lines.Where(x => x.Status != "Cancelled")
            .Sum(x => x.UnitPrice * x.Quantity);
        order.UpdatedAtUtc = e.ReadyAtUtc;
        return e.DiningSessionId;
    }

    private static async Task<Guid?> ApplyOrderSubmitted(IDiningStore db, ReadOnlyMemory<byte> body)
    {
        OrderSubmitted e =
            JsonSerializer.Deserialize<OrderSubmitted>(body.Span)
            ?? throw new JsonException("OrderSubmitted is empty.");
        if (e.DiningSessionId is null)
        {
            return null;
        }
        DiningSessionOrder order =
            await db.FindOrderAsync(e.OrderId)
            ?? throw new JsonException($"Dining order ''{e.OrderId}'' does not exist.");
        order.OrderStatus = AllKitchenStationsDispatched(e.Stations) ? "Delivered" : e.Status;
        order.Amount = e
            .Lines.Where(x => x.Status != "Cancelled")
            .Sum(x => x.UnitPrice * x.Quantity);
        order.UpdatedAtUtc = e.DeliveredAtUtc ?? e.SubmittedAtUtc;
        return e.DiningSessionId;
    }

    private static async Task<Guid?> ApplyOrderLineCancelled(
        IDiningStore db,
        ReadOnlyMemory<byte> body
    )
    {
        OrderLineCancelled e =
            JsonSerializer.Deserialize<OrderLineCancelled>(body.Span)
            ?? throw new JsonException("OrderLineCancelled is empty.");
        if (e.DiningSessionId is null)
        {
            return null;
        }
        DiningSessionOrder order =
            await db.FindOrderAsync(e.OrderId)
            ?? throw new JsonException($"Dining order ''{e.OrderId}'' does not exist.");
        order.OrderStatus = AllKitchenStationsDispatched(e.Stations) ? "Delivered" : e.Status;
        order.Amount = e.Total;
        order.UpdatedAtUtc = e.CancelledAtUtc;
        return e.DiningSessionId;
    }

    private static async Task<Guid?> ApplyOrderCancelled(IDiningStore db, ReadOnlyMemory<byte> body)
    {
        OrderCancelled e =
            JsonSerializer.Deserialize<OrderCancelled>(body.Span)
            ?? throw new JsonException("OrderCancelled is empty.");
        if (e.DiningSessionId is null)
        {
            return null;
        }
        DiningSessionOrder order =
            await db.FindOrderAsync(e.OrderId)
            ?? throw new JsonException($"Dining order '{e.OrderId}' does not exist.");
        order.OrderStatus = "Cancelled";
        order.Amount = 0;
        order.UpdatedAtUtc = e.CancelledAtUtc;
        return e.DiningSessionId;
    }

    private static async Task<Guid?> ApplyKitchenTicketReady(
        IDiningStore db,
        ReadOnlyMemory<byte> body
    )
    {
        KitchenTicketReady e =
            JsonSerializer.Deserialize<KitchenTicketReady>(body.Span)
            ?? throw new JsonException("KitchenTicketReady is empty.");
        if (e.DiningSessionId is null)
        {
            return null;
        }

        DiningSessionOrder order =
            await db.FindOrderAsync(e.OrderId)
            ?? throw new JsonException($"Dining order '{e.OrderId}' does not exist.");
        order.OrderStatus = e.ReadyAtUtc is null ? "InPreparation" : "Ready";
        order.Amount = e
            .Lines.Where(x => x.Status != "Cancelled")
            .Sum(x => x.UnitPrice * x.Quantity);
        order.UpdatedAtUtc =
            e.ReadyAtUtc
            ?? e.Stations.Single(station => station.Code == e.ChangedStationCode).ReadyAtUtc
            ?? e.PreparationStartedAtUtc;
        return e.DiningSessionId;
    }

    private static async Task<Guid?> ApplyKitchenTicketPreparationStarted(
        IDiningStore db,
        ReadOnlyMemory<byte> body
    )
    {
        KitchenTicketPreparationStarted e =
            JsonSerializer.Deserialize<KitchenTicketPreparationStarted>(body.Span)
            ?? throw new JsonException("KitchenTicketPreparationStarted is empty.");
        if (e.DiningSessionId is null)
        {
            return null;
        }

        DiningSessionOrder order =
            await db.FindOrderAsync(e.OrderId)
            ?? throw new JsonException($"Dining order '{e.OrderId}' does not exist.");
        order.OrderStatus = "InPreparation";
        order.Amount = e
            .Lines.Where(x => x.Status != "Cancelled")
            .Sum(x => x.UnitPrice * x.Quantity);
        order.UpdatedAtUtc = e.PreparationStartedAtUtc;
        return e.DiningSessionId;
    }

    private static async Task<Guid?> ApplyKitchenTicketDispatched(
        IDiningStore db,
        ReadOnlyMemory<byte> body
    )
    {
        KitchenTicketDispatched e =
            JsonSerializer.Deserialize<KitchenTicketDispatched>(body.Span)
            ?? throw new JsonException("KitchenTicketDispatched is empty.");
        if (e.DiningSessionId is null)
        {
            return null;
        }

        DiningSessionOrder order =
            await db.FindOrderAsync(e.OrderId)
            ?? throw new JsonException($"Dining order '{e.OrderId}' does not exist.");
        order.OrderStatus = AllKitchenStationsDispatched(e.Stations) ? "Delivered" : "Ready";
        order.Amount = e
            .Lines.Where(x => x.Status != "Cancelled")
            .Sum(x => x.UnitPrice * x.Quantity);
        order.UpdatedAtUtc = e.DispatchedAtUtc;
        return e.DiningSessionId;
    }

    private static bool AllKitchenStationsDispatched(
        IReadOnlyList<OrderKitchenStationSnapshot> stations
    )
    {
        OrderKitchenStationSnapshot[] activeStations = stations
            .Where(station => station.Status != "Cancelled")
            .ToArray();
        return activeStations.Length > 0
            && activeStations.All(station => station.Status == "Dispatched");
    }

    private static async Task ApplyPayment(
        IDiningStore db,
        ReadOnlyMemory<byte> body,
        string status
    )
    {
        PaymentUpdate? payment = ReadPaymentUpdate(body, status);
        if (payment is null)
        {
            return;
        }

        DiningSessionOrder? order = await db.FindOrderAsync(payment.OrderId);
        if (order is not null)
        {
            await ApplyPaymentToOrderAsync(db, order, payment);
            return;
        }

        await ApplyPaymentToPendingAsync(db, payment);
    }

    private static PaymentUpdate? ReadPaymentUpdate(ReadOnlyMemory<byte> body, string status)
    {
        if (status == "Paid")
        {
            PaymentCaptured capture =
                JsonSerializer.Deserialize<PaymentCaptured>(body.Span)
                ?? throw new JsonException("PaymentCaptured is empty.");
            return capture.ServiceMode == "Takeaway"
                ? null
                : new PaymentUpdate(status, capture.OrderId, capture.CapturedAtUtc, capture);
        }

        PaymentRefunded refund =
            JsonSerializer.Deserialize<PaymentRefunded>(body.Span)
            ?? throw new JsonException("PaymentRefunded is empty.");
        return refund.ServiceMode == "Takeaway"
            ? null
            : new PaymentUpdate(status, refund.OrderId, refund.RefundedAtUtc, null);
    }

    private static async Task ApplyPaymentToOrderAsync(
        IDiningStore db,
        DiningSessionOrder order,
        PaymentUpdate payment
    )
    {
        DiningSession? session = await db.FindSessionWithOrdersAsync(
            order.DiningSessionId,
            CancellationToken.None
        );
        decimal chargeableAmount = session?.ChargeableAmount(order) ?? order.Amount;
        order.AmountPaid = payment.Status == "Paid" ? PaidAmount(payment.Capture, order.Amount) : 0;
        order.TipPaid =
            payment.Status == "Paid" ? CumulativeTip(payment.Capture, order.TipPaid) : 0;
        order.PaymentStatus =
            payment.Status == "Refunded" ? payment.Status
            : order.AmountPaid >= chargeableAmount ? "Paid"
            : "PartiallyPaid";
        order.UpdatedAtUtc = payment.OccurredAtUtc;
    }

    private static async Task ApplyPaymentToPendingAsync(IDiningStore db, PaymentUpdate payment)
    {
        DiningPendingPayment? pending = await db.FindPendingPaymentAsync(payment.OrderId);
        if (pending is null)
        {
            db.Add(CreatePendingPayment(payment));
            return;
        }

        UpdatePendingPayment(pending, payment);
    }

    private static DiningPendingPayment CreatePendingPayment(PaymentUpdate payment)
    {
        return new DiningPendingPayment
        {
            OrderId = payment.OrderId,
            Status = payment.Status,
            AmountPaid = payment.Status == "Paid" ? payment.Capture?.AmountPaid ?? 0 : 0,
            TipPaid = payment.Status == "Paid" ? CumulativeTip(payment.Capture, 0) : 0,
            UpdatedAtUtc = payment.OccurredAtUtc,
        };
    }

    private static void UpdatePendingPayment(DiningPendingPayment pending, PaymentUpdate payment)
    {
        pending.Status = payment.Status;
        if (payment.Status == "Paid")
        {
            pending.AmountPaid = payment.Capture?.AmountPaid ?? 0;
            pending.TipPaid = CumulativeTip(payment.Capture, pending.TipPaid);
        }
        else
        {
            pending.AmountPaid = 0;
            pending.TipPaid = 0;
        }
        pending.UpdatedAtUtc = payment.OccurredAtUtc;
    }

    private static decimal PaidAmount(PaymentCaptured? capture, decimal fallbackAmount)
    {
        return capture?.AmountPaid is > 0 ? capture.AmountPaid : fallbackAmount;
    }

    private static decimal CumulativeTip(PaymentCaptured? capture, decimal previousTip)
    {
        return capture is { TipAmountPaid: > 0 }
            ? capture.TipAmountPaid
            : previousTip + (capture?.TipAmount ?? 0);
    }

    private sealed record PaymentUpdate(
        string Status,
        Guid OrderId,
        DateTime OccurredAtUtc,
        PaymentCaptured? Capture
    );
}
