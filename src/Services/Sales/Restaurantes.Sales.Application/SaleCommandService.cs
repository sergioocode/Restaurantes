using System.Text.Json;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Sales.Domain;

namespace Restaurantes.Sales.Application;

public sealed class SaleCommandService(ISaleWriteStore store, TimeProvider timeProvider)
{
    public async Task ProcessAsync(
        Guid messageId,
        PaymentCaptured integrationEvent,
        CancellationToken cancellationToken
    )
    {
        if (await store.WasProcessedAsync(messageId, cancellationToken))
        {
            return;
        }

        Guid saleId =
            integrationEvent.SaleGroupId is Guid groupId && groupId != Guid.Empty ? groupId
            : integrationEvent.PaymentTransactionId is Guid transactionId
            && transactionId != Guid.Empty
                ? transactionId
            : integrationEvent.PaymentId;
        Sale? sale = await store.FindAsync(saleId, cancellationToken);
        sale ??= await store.FindByOrderIdAsync(integrationEvent.OrderId, cancellationToken);
        if (sale is null)
        {
            sale = new Sale
            {
                Id = saleId,
                RestaurantId = integrationEvent.RestaurantId,
                Source = integrationEvent.Source,
                PaymentMethod = integrationEvent.Method,
                ExpectedOrderCount = Math.Max(1, integrationEvent.TransactionOrderCount),
                CreatedAtUtc = integrationEvent.CapturedAtUtc,
                BusinessDate = integrationEvent.BusinessDate,
            };
            store.Add(sale);
        }
        else
        {
            if (sale.PaymentMethod != integrationEvent.Method)
            {
                sale.PaymentMethod = "Mixed";
            }
            if (
                sale.BusinessDate is not null
                && integrationEvent.BusinessDate is not null
                && sale.BusinessDate != integrationEvent.BusinessDate
            )
            {
                throw new InvalidOperationException(
                    "Payments in one sale have different business dates."
                );
            }
            sale.BusinessDate ??= integrationEvent.BusinessDate;
            sale.ExpectedOrderCount = Math.Max(
                sale.ExpectedOrderCount,
                Math.Max(1, integrationEvent.TransactionOrderCount)
            );
        }

        SaleOrder? saleOrder = sale.Orders.SingleOrDefault(item =>
            item.OrderId == integrationEvent.OrderId
        );
        if (saleOrder is null)
        {
            DeliveredOrderSnapshot? delivery = await store.FindDeliveredOrderAsync(
                integrationEvent.OrderId,
                cancellationToken
            );
            sale.Orders.Add(
                new SaleOrder
                {
                    SaleId = sale.Id,
                    OrderId = integrationEvent.OrderId,
                    Amount =
                        integrationEvent.AmountPaid > 0
                            ? integrationEvent.AmountPaid
                            : integrationEvent.Amount,
                    IsPaid = integrationEvent.IsFullyPaid,
                    PaidAtUtc = integrationEvent.IsFullyPaid
                        ? integrationEvent.CapturedAtUtc
                        : null,
                    IsDelivered = delivery is not null,
                    DeliveredAtUtc = delivery?.DeliveredAtUtc,
                    LinesJson = delivery?.LinesJson ?? "[]",
                }
            );
        }
        else
        {
            saleOrder.Amount = Math.Max(
                saleOrder.Amount,
                integrationEvent.AmountPaid > 0
                    ? integrationEvent.AmountPaid
                    : saleOrder.Amount + integrationEvent.Amount
            );
            if (integrationEvent.IsFullyPaid)
            {
                saleOrder.IsPaid = true;
                saleOrder.PaidAtUtc = integrationEvent.CapturedAtUtc;
            }
        }

        if (sale.Payments.All(item => item.PaymentId != integrationEvent.PaymentId))
        {
            sale.Payments.Add(
                new SalePayment
                {
                    PaymentId = integrationEvent.PaymentId,
                    SaleId = sale.Id,
                    TransactionId =
                        integrationEvent.PaymentTransactionId is Guid captureTransactionId
                        && captureTransactionId != Guid.Empty
                            ? captureTransactionId
                            : integrationEvent.PaymentId,
                    Method = integrationEvent.Method,
                    Amount = integrationEvent.Amount,
                    TipAmount = integrationEvent.TipAmount,
                }
            );
        }

        await CompleteAndSaveAsync(messageId, sale, cancellationToken);
    }

    public async Task ProcessAsync(
        Guid messageId,
        PaymentRefunded integrationEvent,
        CancellationToken cancellationToken
    )
    {
        if (await store.WasProcessedAsync(messageId, cancellationToken))
        {
            return;
        }

        Sale? sale = await store.FindByOrderIdAsync(integrationEvent.OrderId, cancellationToken);
        SaleOrder? order = sale?.Orders.Single(item => item.OrderId == integrationEvent.OrderId);
        order?.IsPaid = false;

        await CompleteAndSaveAsync(messageId, sale, cancellationToken);
    }

    public async Task ProcessAsync(
        Guid messageId,
        OrderDelivered integrationEvent,
        CancellationToken cancellationToken
    )
    {
        if (await store.WasProcessedAsync(messageId, cancellationToken))
        {
            return;
        }

        string linesJson = JsonSerializer.Serialize(
            integrationEvent
                .Lines.Where(item => item.Status != "Cancelled")
                .Select(item => new SaleLineSnapshot(
                    item.ProductId,
                    item.ProductName,
                    item.UnitPrice,
                    item.Quantity,
                    item.CategoryName,
                    item.PreparationStationCode,
                    item.Format
                ))
        );
        await store.UpsertDeliveredOrderAsync(
            new DeliveredOrderSnapshot(
                integrationEvent.OrderId,
                integrationEvent.RestaurantId,
                integrationEvent.DeliveredAtUtc,
                linesJson
            ),
            cancellationToken
        );

        Sale? sale = await store.FindByOrderIdAsync(integrationEvent.OrderId, cancellationToken);
        SaleOrder? order = sale?.Orders.Single(item => item.OrderId == integrationEvent.OrderId);
        if (order is not null)
        {
            order.IsDelivered = true;
            order.DeliveredAtUtc = integrationEvent.DeliveredAtUtc;
            order.LinesJson = linesJson;
        }

        await CompleteAndSaveAsync(messageId, sale, cancellationToken);
    }

    private async Task CompleteAndSaveAsync(
        Guid messageId,
        Sale? sale,
        CancellationToken cancellationToken
    )
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (sale is not null && sale.TryComplete(now))
        {
            List<SaleLineSnapshot> lines = sale
                .Orders.SelectMany(item =>
                    JsonSerializer.Deserialize<List<SaleLineSnapshot>>(item.LinesJson) ?? []
                )
                .ToList();
            store.AddIntegrationEvent(
                new SaleCompleted(
                    sale.Id,
                    sale.RestaurantId,
                    sale.Source,
                    sale.PaymentMethod,
                    sale.Total,
                    sale.CompletedAtUtc!.Value,
                    sale.Orders.Select(item => item.OrderId).ToList(),
                    lines,
                    sale.BusinessDate,
                    sale.Payments.GroupBy(item => item.Method)
                        .Select(group => new SalePaymentBreakdown(
                            group.Key,
                            group.Select(item => item.TransactionId).Distinct().Count(),
                            group.Sum(item => item.Amount)
                        ))
                        .ToList(),
                    sale.Payments.Sum(item => item.TipAmount)
                )
            );
        }

        store.MarkProcessed(messageId, now);
        await store.SaveAsync(cancellationToken);
    }
}
