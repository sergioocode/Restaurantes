using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Reporting.Contracts.Events;
using Restaurantes.Reporting.Infrastructure.Persistence;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;
using Restaurantes.Sales.Contracts.Events;

namespace Restaurantes.Reporting.Consumer.Handlers;

public sealed class ReportingProjectionHandler(ReportingReadDbContext db, TimeProvider time)
{
    public async Task ProjectAsync(
        Guid messageId,
        string? type,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    )
    {
        if (!await db.InboxMessages.AnyAsync(x => x.Id == messageId))
        {
            Guid affectedOrderId;
            Guid affectedRestaurantId;
            if (IsOrderEvent(type))
            {
                OrderData data = DeserializeOrder(type, body.Span);
                affectedOrderId = data.OrderId;
                affectedRestaurantId = data.RestaurantId;
                OrderReportingFact fact =
                    await db.Orders.FindAsync([data.OrderId])
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
            }
            else if (type == typeof(PaymentCaptured).FullName)
            {
                PaymentCaptured e =
                    JsonSerializer.Deserialize<PaymentCaptured>(body.Span)
                    ?? throw new JsonException("Empty payment");
                affectedOrderId = e.OrderId;
                affectedRestaurantId = e.RestaurantId;
                OrderReportingFact fact =
                    await db.Orders.FindAsync([e.OrderId])
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

                fact.PaymentTransactionId =
                    e.PaymentTransactionId is Guid transactionId && transactionId != Guid.Empty
                        ? transactionId
                        : e.PaymentId;
                fact.PaymentStatus = "Paid";
                fact.PaymentMethod = e.Method;
                fact.PaidAtUtc = e.CapturedAtUtc;
                fact.UpdatedAtUtc = e.CapturedAtUtc;
                RecognizeSale(fact);
            }
            else if (type == typeof(PaymentRefunded).FullName)
            {
                PaymentRefunded e =
                    JsonSerializer.Deserialize<PaymentRefunded>(body.Span)
                    ?? throw new JsonException("Empty refund");
                affectedOrderId = e.OrderId;
                affectedRestaurantId = e.RestaurantId;
                OrderReportingFact fact =
                    await db.Orders.FindAsync([e.OrderId])
                    ?? throw new JsonException("Refunded order is missing");
                fact.PaymentStatus = "Refunded";
                fact.RefundedAtUtc = e.RefundedAtUtc;
                fact.UpdatedAtUtc = e.RefundedAtUtc;
                fact.SaleRecognizedAtUtc = null;
            }
            else if (type == typeof(SaleCompleted).FullName)
            {
                SaleCompleted e =
                    JsonSerializer.Deserialize<SaleCompleted>(body.Span)
                    ?? throw new JsonException("Empty sale");
                affectedOrderId = e.OrderIds.FirstOrDefault();
                affectedRestaurantId = e.RestaurantId;
                List<OrderReportingFact> saleOrders = await db
                    .Orders.Where(item => e.OrderIds.Contains(item.OrderId))
                    .ToListAsync();
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
                        item.PreparationStationCode
                    ))
                    .ToList();
                CompletedSaleFact fact =
                    await db.Sales.FindAsync([e.SaleId])
                    ?? new CompletedSaleFact { SaleId = e.SaleId };
                if (db.Entry(fact).State == EntityState.Detached)
                {
                    db.Sales.Add(fact);
                }

                fact.RestaurantId = e.RestaurantId;
                fact.Source = e.Source;
                fact.PaymentMethod = e.PaymentMethod;
                fact.Total = e.Total;
                fact.CompletedAtUtc = e.CompletedAtUtc;
                fact.OrderIdsJson = JsonSerializer.Serialize(e.OrderIds);
                fact.LinesJson = JsonSerializer.Serialize(saleLines);
            }
            else
            {
                throw new NotSupportedException(type);
            }

            DateTime projectionUpdatedAtUtc = time.GetUtcNow().UtcDateTime;
            db.InboxMessages.Add(
                new ReportingInboxMessage
                {
                    Id = messageId,
                    ProcessedAtUtc = projectionUpdatedAtUtc,
                }
            );
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
            await db.SaveChangesAsync(cancellationToken);
        }
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
            OrderCreated e = JsonSerializer.Deserialize<OrderCreated>(body)!;
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
            OrderLineCancelled e = JsonSerializer.Deserialize<OrderLineCancelled>(body)!;
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
            OrderCancelled e = JsonSerializer.Deserialize<OrderCancelled>(body)!;
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
            OrderSubmitted e = JsonSerializer.Deserialize<OrderSubmitted>(body)!;
            return new(
                e.OrderId,
                e.RestaurantId,
                e.TableLabel,
                e.CustomerName,
                e.ServiceMode,
                e.Source,
                e.PaymentTiming,
                "Submitted",
                e.Version,
                e.CreatedAtUtc,
                e.SubmittedAtUtc,
                null,
                e.Lines,
                e.Stations
            );
        }
        if (type == typeof(KitchenTicketPreparationStarted).FullName)
        {
            KitchenTicketPreparationStarted e =
                JsonSerializer.Deserialize<KitchenTicketPreparationStarted>(body)!;
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
            KitchenTicketReady e = JsonSerializer.Deserialize<KitchenTicketReady>(body)!;
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
                e.ReadyAtUtc
                    ?? e.Stations.Single(x => x.Code == e.ChangedStationCode).ReadyAtUtc!.Value,
                null,
                e.Lines,
                e.Stations
            );
        }
        if (type == typeof(KitchenTicketDispatched).FullName)
        {
            KitchenTicketDispatched e = JsonSerializer.Deserialize<KitchenTicketDispatched>(body)!;
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
            OrderDelivered e = JsonSerializer.Deserialize<OrderDelivered>(body)!;
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
            OrderPreparationStarted e = JsonSerializer.Deserialize<OrderPreparationStarted>(body)!;
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
            OrderReady e = JsonSerializer.Deserialize<OrderReady>(body)!;
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
