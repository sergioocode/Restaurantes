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

        if (type == typeof(OrderCreated).FullName)
        {
            await ApplyOrderCreated(db, body);
        }
        else if (type == typeof(OrderSubmitted).FullName)
        {
            await ApplyOrderSubmitted(db, body);
        }
        else if (type == typeof(OrderDelivered).FullName)
        {
            await ApplyOrderDelivered(db, body);
        }
        else if (type == typeof(OrderReady).FullName)
        {
            await ApplyOrderReady(db, body);
        }
        else if (type == typeof(OrderLineCancelled).FullName)
        {
            await ApplyOrderLineCancelled(db, body);
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

        db.MarkMessageProcessed(messageId, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
    }

    private static async Task ApplyOrderCreated(IDiningStore db, ReadOnlyMemory<byte> body)
    {
        OrderCreated e =
            JsonSerializer.Deserialize<OrderCreated>(body.Span)
            ?? throw new JsonException("OrderCreated is empty.");
        if (e.ServiceMode == "Takeaway" || e.DiningSessionId is null)
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
            PaymentStatus = pending?.Status ?? "Unpaid",
            OrderStatus = "Draft",
            Amount = e.Lines.Sum(x => x.UnitPrice * x.Quantity),
            AddedAtUtc = e.OccurredAtUtc,
            UpdatedAtUtc = pending?.UpdatedAtUtc ?? e.OccurredAtUtc,
        };
        db.Add(projectedOrder);
        if (pending is not null)
        {
            db.RemovePendingPayment(pending);
        }
    }

    private static async Task ApplyOrderDelivered(IDiningStore db, ReadOnlyMemory<byte> body)
    {
        OrderDelivered e =
            JsonSerializer.Deserialize<OrderDelivered>(body.Span)
            ?? throw new JsonException("OrderDelivered is empty.");
        if (e.ServiceMode == "Takeaway")
        {
            return;
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
    }

    private static async Task ApplyOrderReady(IDiningStore db, ReadOnlyMemory<byte> body)
    {
        OrderReady e =
            JsonSerializer.Deserialize<OrderReady>(body.Span)
            ?? throw new JsonException("OrderReady is empty.");
        if (e.ServiceMode == "Takeaway")
        {
            return;
        }
        DiningSessionOrder? order = await db.FindOrderAsync(e.OrderId);
        if (order is null)
        {
            throw new JsonException($"Dining order '{e.OrderId}' does not exist.");
        }
        order.OrderStatus = "Ready";
        order.Amount = e
            .Lines.Where(x => x.Status != "Cancelled")
            .Sum(x => x.UnitPrice * x.Quantity);
        order.UpdatedAtUtc = e.ReadyAtUtc;
    }

    private static async Task ApplyOrderSubmitted(IDiningStore db, ReadOnlyMemory<byte> body)
    {
        OrderSubmitted e =
            JsonSerializer.Deserialize<OrderSubmitted>(body.Span)
            ?? throw new JsonException("OrderSubmitted is empty.");
        if (e.ServiceMode == "Takeaway")
        {
            return;
        }

        DiningSessionOrder order =
            await db.FindOrderAsync(e.OrderId)
            ?? throw new JsonException($"Dining order ''{e.OrderId}'' does not exist.");
        order.OrderStatus = "Submitted";
        order.Amount = e
            .Lines.Where(x => x.Status != "Cancelled")
            .Sum(x => x.UnitPrice * x.Quantity);
        order.UpdatedAtUtc = e.SubmittedAtUtc;
    }

    private static async Task ApplyOrderLineCancelled(IDiningStore db, ReadOnlyMemory<byte> body)
    {
        OrderLineCancelled e =
            JsonSerializer.Deserialize<OrderLineCancelled>(body.Span)
            ?? throw new JsonException("OrderLineCancelled is empty.");
        if (e.ServiceMode == "Takeaway" || e.DiningSessionId is null)
        {
            return;
        }
        DiningSessionOrder order =
            await db.FindOrderAsync(e.OrderId)
            ?? throw new JsonException($"Dining order ''{e.OrderId}'' does not exist.");
        order.OrderStatus = e.Status;
        order.Amount = e.Total;
        order.UpdatedAtUtc = e.CancelledAtUtc;
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
            order.PaymentStatus = status;
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
                    UpdatedAtUtc = occurredAtUtc,
                }
            );
        }
        else
        {
            pending.Status = status;
            pending.UpdatedAtUtc = occurredAtUtc;
        }
    }
}
