using System.Text.Json;
using Restaurantes.Dining.Domain;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Payments.Contracts.Events;

namespace Restaurantes.Dining.Application;

public sealed class DiningIntegrationService(
    IDiningStore db,
    TimeProvider time,
    IDiningNotifications notifications
)
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
            await NotifySessionChanged(sessionId);
        }
        db.MarkMessageProcessed(messageId, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
    }

    private async Task NotifySessionChanged(Guid sessionId)
    {
        DiningSession? session = await db.ReadSessionWithOrdersAsync(
            sessionId,
            CancellationToken.None
        );
        if (session is null || session.Status != "Open")
        {
            return;
        }

        await notifications.TableChanged(
            new DiningTableChanged(
                session.RestaurantId,
                session.TableId,
                "Occupied",
                session.Id,
                session.Source,
                time.GetUtcNow().UtcDateTime
            )
        );
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
        order.OrderStatus = "Submitted";
        order.Amount = e
            .Lines.Where(x => x.Status != "Cancelled")
            .Sum(x => x.UnitPrice * x.Quantity);
        order.UpdatedAtUtc = e.SubmittedAtUtc;
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
        Guid orderId;
        DateTime occurredAtUtc;
        if (status == "Paid")
        {
            PaymentCaptured e =
                JsonSerializer.Deserialize<PaymentCaptured>(body.Span)
                ?? throw new JsonException("PaymentCaptured is empty.");
            if (e.ServiceMode == "Takeaway")
            {
                return;
            }
            orderId = e.OrderId;
            occurredAtUtc = e.CapturedAtUtc;
        }
        else
        {
            PaymentRefunded e =
                JsonSerializer.Deserialize<PaymentRefunded>(body.Span)
                ?? throw new JsonException("PaymentRefunded is empty.");
            if (e.ServiceMode == "Takeaway")
            {
                return;
            }
            orderId = e.OrderId;
            occurredAtUtc = e.RefundedAtUtc;
        }

        DiningSessionOrder? order = await db.FindOrderAsync(orderId);
        if (order is not null)
        {
            PaymentCaptured? capture =
                status == "Paid" ? JsonSerializer.Deserialize<PaymentCaptured>(body.Span) : null;
            decimal amountPaid =
                status == "Paid"
                    ? capture?.AmountPaid is > 0
                        ? capture.AmountPaid
                        : order.Amount
                    : 0;
            order.AmountPaid = amountPaid;
            order.TipPaid =
                status == "Paid"
                    ? capture is { TipAmountPaid: > 0 }
                        ? capture.TipAmountPaid
                        : order.TipPaid + (capture?.TipAmount ?? 0)
                    : 0;
            order.PaymentStatus =
                status == "Refunded" ? status
                : order.AmountPaid >= order.Amount ? "Paid"
                : "PartiallyPaid";
            order.UpdatedAtUtc = occurredAtUtc;
            return;
        }
        DiningPendingPayment? pending = await db.FindPendingPaymentAsync(orderId);
        if (pending is null)
        {
            db.Add(
                new DiningPendingPayment
                {
                    OrderId = orderId,
                    Status = status,
                    AmountPaid =
                        status == "Paid"
                            ? JsonSerializer.Deserialize<PaymentCaptured>(body.Span)?.AmountPaid
                                ?? 0
                            : 0,
                    TipPaid = status == "Paid" ? ReadCumulativeTip(body) : 0,
                    UpdatedAtUtc = occurredAtUtc,
                }
            );
        }
        else
        {
            pending.Status = status;
            if (status == "Paid")
            {
                pending.AmountPaid =
                    JsonSerializer.Deserialize<PaymentCaptured>(body.Span)?.AmountPaid ?? 0;
                PaymentCaptured? capture = JsonSerializer.Deserialize<PaymentCaptured>(body.Span);
                pending.TipPaid = capture is { TipAmountPaid: > 0 }
                    ? capture.TipAmountPaid
                    : pending.TipPaid + (capture?.TipAmount ?? 0);
            }
            else
            {
                pending.AmountPaid = 0;
                pending.TipPaid = 0;
            }
            pending.UpdatedAtUtc = occurredAtUtc;
        }
    }

    private static decimal ReadCumulativeTip(ReadOnlyMemory<byte> body)
    {
        PaymentCaptured? capture = JsonSerializer.Deserialize<PaymentCaptured>(body.Span);
        return capture is { TipAmountPaid: > 0 } ? capture.TipAmountPaid : capture?.TipAmount ?? 0;
    }
}
