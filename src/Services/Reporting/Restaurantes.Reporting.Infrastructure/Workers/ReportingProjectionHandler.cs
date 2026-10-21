using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Reporting.Contracts.Events;
using Restaurantes.Reporting.Infrastructure.Persistence;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;
using Restaurantes.Sales.Contracts.Events;

namespace Restaurantes.Reporting.Infrastructure.Workers;

public sealed class ReportingProjectionHandler(ReportingReadDbContext db, TimeProvider time)
{
    public async Task ProjectAsync(
        Guid messageId,
        string? type,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    )
    {
        if (await db.InboxMessages.AnyAsync(x => x.Id == messageId, cancellationToken))
        {
            return;
        }

        (Guid affectedOrderId, Guid affectedRestaurantId) = await DispatchProjectionAsync(
            type,
            body,
            cancellationToken
        );

        DateTime projectionUpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        db.InboxMessages.Add(
            new ReportingInboxMessage { Id = messageId, ProcessedAtUtc = projectionUpdatedAtUtc }
        );
        if (type != typeof(PaymentCaptured).FullName)
        {
            DashboardProjectionUpdated updated = new(
                affectedOrderId,
                affectedRestaurantId,
                projectionUpdatedAtUtc
            );
            db.OutboxMessages.Add(
                new ReportingOutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = typeof(DashboardProjectionUpdated).FullName!,
                    Payload = JsonSerializer.Serialize(updated),
                    OccurredAtUtc = projectionUpdatedAtUtc,
                }
            );
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(Guid OrderId, Guid RestaurantId)> DispatchProjectionAsync(
        string? type,
        ReadOnlyMemory<byte> body,
        CancellationToken ct
    )
    {
        if (IsOrderEvent(type))
        {
            return await ProjectOrderAsync(type, body, ct);
        }
        if (type == typeof(PaymentCaptured).FullName)
        {
            return await ProjectPaymentCapturedAsync(body, ct);
        }
        if (type == typeof(PaymentRefunded).FullName)
        {
            return await ProjectPaymentRefundedAsync(body, ct);
        }
        if (type == typeof(SaleCompleted).FullName)
        {
            return await ProjectSaleCompletedAsync(body, ct);
        }
        throw new NotSupportedException(type);
    }

    private async Task<(Guid OrderId, Guid RestaurantId)> ProjectOrderAsync(
        string? type,
        ReadOnlyMemory<byte> body,
        CancellationToken ct
    )
    {
        OrderData data = DeserializeOrder(type, body.Span);
        OrderReportingFact fact =
            await db.Orders.FindAsync([data.OrderId], ct)
            ?? new OrderReportingFact { OrderId = data.OrderId, PaymentStatus = "Unpaid" };
        if (db.Entry(fact).State == EntityState.Detached)
        {
            db.Orders.Add(fact);
        }

        if (fact.Version < data.Version)
        {
            fact.RestaurantId = data.RestaurantId;
            fact.TableLabel = data.TableLabel;
            fact.CustomerName = data.CustomerName;
            fact.ServiceMode = data.ServiceMode;
            fact.Source = data.Source;
            fact.PaymentTiming = data.PaymentTiming;
            fact.OrderStatus = data.Status;
            fact.Total = data
                .Lines.Where(x => x.Status != "Cancelled")
                .Sum(x => x.UnitPrice * x.Quantity);
            fact.Version = data.Version;
            fact.CreatedAtUtc = data.CreatedAtUtc;
            fact.UpdatedAtUtc = data.UpdatedAtUtc;
            fact.DeliveredAtUtc = data.DeliveredAtUtc;
            fact.LinesJson = JsonSerializer.Serialize(data.Lines);
            fact.StationsJson = JsonSerializer.Serialize(data.Stations);
            RecognizeSale(fact);
        }
        return (data.OrderId, data.RestaurantId);
    }

    private async Task<(Guid OrderId, Guid RestaurantId)> ProjectPaymentCapturedAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken ct
    )
    {
        PaymentCaptured e =
            JsonSerializer.Deserialize<PaymentCaptured>(body.Span)
            ?? throw new JsonException("Empty payment");
        OrderReportingFact fact =
            await db.Orders.FindAsync([e.OrderId], ct)
            ?? new OrderReportingFact
            {
                OrderId = e.OrderId,
                RestaurantId = e.RestaurantId,
                ServiceMode = e.ServiceMode,
                Source = e.Source,
                PaymentTiming = "Immediate",
                OrderStatus = "Draft",
                CreatedAtUtc = e.CapturedAtUtc,
                UpdatedAtUtc = e.CapturedAtUtc,
            };
        if (db.Entry(fact).State == EntityState.Detached)
        {
            db.Orders.Add(fact);
        }

        if (e.IsFullyPaid || fact.PaymentStatus != "Paid")
        {
            Guid paymentTransactionId = e.PaymentId;
            if (e.PaymentTransactionId is Guid transactionId && transactionId != Guid.Empty)
            {
                paymentTransactionId = transactionId;
            }
            if (e.SaleGroupId is Guid groupId && groupId != Guid.Empty)
            {
                paymentTransactionId = groupId;
            }
            fact.PaymentTransactionId = paymentTransactionId;
            fact.PaymentStatus = e.IsFullyPaid ? "Paid" : "PartiallyPaid";
            fact.PaymentMethod = e.Method;
            fact.PaidAtUtc = e.IsFullyPaid ? e.CapturedAtUtc : null;
            fact.UpdatedAtUtc = e.CapturedAtUtc;
            RecognizeSale(fact);
        }
        return (e.OrderId, e.RestaurantId);
    }

    private async Task<(Guid OrderId, Guid RestaurantId)> ProjectPaymentRefundedAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken ct
    )
    {
        PaymentRefunded e =
            JsonSerializer.Deserialize<PaymentRefunded>(body.Span)
            ?? throw new JsonException("Empty refund");
        OrderReportingFact fact =
            await db.Orders.FindAsync([e.OrderId], ct)
            ?? throw new JsonException("Refunded order is missing");
        fact.PaymentStatus = "Refunded";
        fact.RefundedAtUtc = e.RefundedAtUtc;
        fact.UpdatedAtUtc = e.RefundedAtUtc;
        fact.SaleRecognizedAtUtc = null;
        return (e.OrderId, e.RestaurantId);
    }

    private async Task<(Guid OrderId, Guid RestaurantId)> ProjectSaleCompletedAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken ct
    )
    {
        SaleCompleted e =
            JsonSerializer.Deserialize<SaleCompleted>(body.Span)
            ?? throw new JsonException("Empty sale");
        List<OrderReportingFact> saleOrders = await db
            .Orders.Where(item => e.OrderIds.Contains(item.OrderId))
            .ToListAsync(ct);
        if (saleOrders.Count != e.OrderIds.Distinct().Count())
        {
            throw new InvalidOperationException(
                "The order projection is not ready for the completed sale."
            );
        }
        List<SaleLineSnapshot> saleLines = saleOrders
            .SelectMany(item =>
                JsonSerializer.Deserialize<List<OrderLineSnapshot>>(item.LinesJson) ?? []
            )
            .Where(item => item.Status != "Cancelled")
            .Select(item => new SaleLineSnapshot(
                item.ProductId,
                item.ProductName,
                item.UnitPrice,
                item.Quantity,
                item.CategoryName,
                item.PreparationStationCode,
                item.Format,
                item.BaseUnitPrice,
                item.Extras?.Select(extra => new SaleLineExtraSnapshot(
                        extra.ProductId,
                        extra.ProductName,
                        extra.UnitPrice,
                        extra.Quantity
                    ))
                    .ToList()
            ))
            .ToList();
        CompletedSaleFact fact =
            await db.Sales.FindAsync([e.SaleId], ct) ?? new CompletedSaleFact { SaleId = e.SaleId };
        if (db.Entry(fact).State == EntityState.Detached)
        {
            db.Sales.Add(fact);
        }

        fact.RestaurantId = e.RestaurantId;
        fact.Source = e.Source;
        fact.PaymentMethod = e.PaymentMethod;
        fact.Total = e.Total;
        fact.CompletedAtUtc = e.CompletedAtUtc;
        fact.BusinessDate = e.BusinessDate;
        fact.OrderIdsJson = JsonSerializer.Serialize(e.OrderIds);
        fact.LinesJson = JsonSerializer.Serialize(saleLines);
        fact.PaymentMethodsJson = JsonSerializer.Serialize(e.PaymentMethods ?? []);
        Guid firstOrderId = e.OrderIds.Count == 0 ? Guid.Empty : e.OrderIds[0];
        return (firstOrderId, e.RestaurantId);
    }

    private static bool IsOrderEvent(string? type)
    {
        return type == typeof(OrderCreated).FullName
            || type == typeof(OrderSubmitted).FullName
            || type == typeof(KitchenTicketPreparationStarted).FullName
            || type == typeof(KitchenTicketReady).FullName
            || type == typeof(KitchenTicketDispatched).FullName
            || type == typeof(OrderDelivered).FullName
            || type == typeof(OrderPreparationStarted).FullName
            || type == typeof(OrderReady).FullName
            || type == typeof(OrderCancelled).FullName
            || type == typeof(OrderLineCancelled).FullName;
    }

    private static OrderData DeserializeOrder(string? type, ReadOnlySpan<byte> body)
    {
        if (type == typeof(OrderCreated).FullName)
        {
            OrderCreated e = DeserializeRequired<OrderCreated>(body);
            return new(
                e.OrderId,
                e.RestaurantId,
                e.TableLabel,
                e.CustomerName,
                e.ServiceMode,
                e.Source,
                e.PaymentTiming,
                "Draft",
                e.Version,
                e.OccurredAtUtc,
                e.OccurredAtUtc,
                null,
                e.Lines,
                e.Stations
            );
        }
        if (type == typeof(OrderLineCancelled).FullName)
        {
            OrderLineCancelled e = DeserializeRequired<OrderLineCancelled>(body);
            return new(
                e.OrderId,
                e.RestaurantId,
                e.TableLabel,
                e.CustomerName,
                e.ServiceMode,
                e.Source,
                e.PaymentTiming,
                e.Status,
                e.Version,
                e.CreatedAtUtc,
                e.CancelledAtUtc,
                null,
                e.Lines,
                e.Stations
            );
        }
        if (type == typeof(OrderCancelled).FullName)
        {
            OrderCancelled e = DeserializeRequired<OrderCancelled>(body);
            return new(
                e.OrderId,
                e.RestaurantId,
                e.TableLabel,
                e.CustomerName,
                e.ServiceMode,
                e.Source,
                e.PaymentTiming,
                "Cancelled",
                e.Version,
                e.CreatedAtUtc,
                e.CancelledAtUtc,
                null,
                e.Lines,
                e.Stations
            );
        }
        if (type == typeof(OrderSubmitted).FullName)
        {
            OrderSubmitted e = DeserializeRequired<OrderSubmitted>(body);
            return new(
                e.OrderId,
                e.RestaurantId,
                e.TableLabel,
                e.CustomerName,
                e.ServiceMode,
                e.Source,
                e.PaymentTiming,
                e.Status,
                e.Version,
                e.CreatedAtUtc,
                e.SubmittedAtUtc,
                e.DeliveredAtUtc,
                e.Lines,
                e.Stations
            );
        }
        if (type == typeof(KitchenTicketPreparationStarted).FullName)
        {
            KitchenTicketPreparationStarted e =
                DeserializeRequired<KitchenTicketPreparationStarted>(body);
            return new(
                e.OrderId,
                e.RestaurantId,
                e.TableLabel,
                e.CustomerName,
                e.ServiceMode,
                e.Source,
                e.PaymentTiming,
                "InPreparation",
                e.Version,
                e.CreatedAtUtc,
                e.PreparationStartedAtUtc,
                null,
                e.Lines,
                e.Stations
            );
        }
        if (type == typeof(KitchenTicketReady).FullName)
        {
            KitchenTicketReady e = DeserializeRequired<KitchenTicketReady>(body);
            return new(
                e.OrderId,
                e.RestaurantId,
                e.TableLabel,
                e.CustomerName,
                e.ServiceMode,
                e.Source,
                e.PaymentTiming,
                e.ReadyAtUtc.HasValue ? "Ready" : "InPreparation",
                e.Version,
                e.CreatedAtUtc,
                e.Lines.Where(x => x.PreparationStationCode == e.ChangedStationCode)
                    .Select(x => x.ReadyAtUtc)
                    .Where(x => x.HasValue)
                    .Max()
                    ?? throw new InvalidOperationException(
                        $"Kitchen station '{e.ChangedStationCode}' has no ready line timestamp."
                    ),
                null,
                e.Lines,
                e.Stations
            );
        }
        if (type == typeof(KitchenTicketDispatched).FullName)
        {
            KitchenTicketDispatched e = DeserializeRequired<KitchenTicketDispatched>(body);
            bool completed = e
                .Stations.Where(x => x.Status != "Cancelled")
                .All(x => x.Status == "Dispatched");
            return new(
                e.OrderId,
                e.RestaurantId,
                e.TableLabel,
                e.CustomerName,
                e.ServiceMode,
                e.Source,
                e.PaymentTiming,
                completed ? "Ready" : "InPreparation",
                e.Version,
                e.CreatedAtUtc,
                e.DispatchedAtUtc,
                null,
                e.Lines,
                e.Stations
            );
        }
        if (type == typeof(OrderDelivered).FullName)
        {
            OrderDelivered e = DeserializeRequired<OrderDelivered>(body);
            return new(
                e.OrderId,
                e.RestaurantId,
                e.TableLabel,
                e.CustomerName,
                e.ServiceMode,
                e.Source,
                e.PaymentTiming,
                "Delivered",
                e.Version,
                e.CreatedAtUtc,
                e.DeliveredAtUtc,
                e.DeliveredAtUtc,
                e.Lines,
                e.Stations
            );
        }
        if (type == typeof(OrderPreparationStarted).FullName)
        {
            OrderPreparationStarted e = DeserializeRequired<OrderPreparationStarted>(body);
            return new(
                e.OrderId,
                e.RestaurantId,
                e.TableLabel,
                e.CustomerName,
                e.ServiceMode,
                e.Source,
                e.PaymentTiming,
                "InPreparation",
                e.Version,
                e.CreatedAtUtc,
                e.PreparationStartedAtUtc,
                null,
                e.Lines,
                e.Stations
            );
        }
        if (type == typeof(OrderReady).FullName)
        {
            OrderReady e = DeserializeRequired<OrderReady>(body);
            return new(
                e.OrderId,
                e.RestaurantId,
                e.TableLabel,
                e.CustomerName,
                e.ServiceMode,
                e.Source,
                e.PaymentTiming,
                "Ready",
                e.Version,
                e.CreatedAtUtc,
                e.ReadyAtUtc,
                null,
                e.Lines,
                e.Stations
            );
        }
        throw new NotSupportedException(type);
    }

    private static TEvent DeserializeRequired<TEvent>(ReadOnlySpan<byte> body)
        where TEvent : class =>
        JsonSerializer.Deserialize<TEvent>(body) ?? throw new JsonException("Empty order event.");

    private static void RecognizeSale(OrderReportingFact fact)
    {
        fact.SaleRecognizedAtUtc = fact.PaymentStatus == "Paid" ? fact.PaidAtUtc : null;
    }

    private sealed record OrderData(
        Guid OrderId,
        Guid RestaurantId,
        string TableLabel,
        string CustomerName,
        string ServiceMode,
        string Source,
        string PaymentTiming,
        string Status,
        int Version,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        DateTime? DeliveredAtUtc,
        IReadOnlyList<OrderLineSnapshot> Lines,
        IReadOnlyList<OrderKitchenStationSnapshot> Stations
    );
}
