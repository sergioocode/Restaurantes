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

        Sale sale = await FindOrCreateSaleAsync(integrationEvent, cancellationToken);
        await AddOrUpdateSaleOrderAsync(sale, integrationEvent, cancellationToken);
        AddPaymentIfNew(sale, integrationEvent);

        await CompleteAndSaveAsync(messageId, sale, cancellationToken);
    }

    private async Task<Sale> FindOrCreateSaleAsync(
        PaymentCaptured integrationEvent,
        CancellationToken cancellationToken
    )
    {
        Guid saleId = GetSaleId(integrationEvent);
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
            return sale;
        }

        UpdateSaleDetails(sale, integrationEvent);
        return sale;
    }

    private static Guid GetSaleId(PaymentCaptured integrationEvent)
    {
        if (integrationEvent.SaleGroupId is Guid groupId && groupId != Guid.Empty)
        {
            return groupId;
        }

        if (
            integrationEvent.PaymentTransactionId is Guid transactionId
            && transactionId != Guid.Empty
        )
        {
            return transactionId;
        }

        return integrationEvent.PaymentId;
    }

    private static void UpdateSaleDetails(Sale sale, PaymentCaptured integrationEvent)
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

    private async Task AddOrUpdateSaleOrderAsync(
        Sale sale,
        PaymentCaptured integrationEvent,
        CancellationToken cancellationToken
    )
    {
        SaleOrder? saleOrder = sale.Orders.SingleOrDefault(item =>
            item.OrderId == integrationEvent.OrderId
        );
        if (saleOrder is null)
        {
            DeliveredOrderSnapshot? delivery = await store.FindDeliveredOrderAsync(
                integrationEvent.OrderId,
                cancellationToken
            );
            sale.Orders.Add(CreateSaleOrder(sale.Id, integrationEvent, delivery));
            return;
        }

        UpdateSaleOrder(saleOrder, integrationEvent);
    }

    private static SaleOrder CreateSaleOrder(
        Guid saleId,
        PaymentCaptured integrationEvent,
        DeliveredOrderSnapshot? delivery
    ) =>
        new()
        {
            SaleId = saleId,
            OrderId = integrationEvent.OrderId,
            Amount =
                integrationEvent.AmountPaid > 0
                    ? integrationEvent.AmountPaid
                    : integrationEvent.Amount,
            IsPaid = integrationEvent.IsFullyPaid,
            PaidAtUtc = integrationEvent.IsFullyPaid ? integrationEvent.CapturedAtUtc : null,
            IsDelivered = delivery is not null,
            DeliveredAtUtc = delivery?.DeliveredAtUtc,
            LinesJson = delivery?.LinesJson ?? "[]",
            DiscountName = integrationEvent.DiscountName,
            DiscountPercentage = integrationEvent.DiscountPercentage,
            DiscountAmount = integrationEvent.DiscountAmount,
        };

    private static void UpdateSaleOrder(SaleOrder saleOrder, PaymentCaptured integrationEvent)
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

        saleOrder.DiscountName = integrationEvent.DiscountName;
        saleOrder.DiscountPercentage = integrationEvent.DiscountPercentage;
        saleOrder.DiscountAmount = integrationEvent.DiscountAmount;
    }

    private static void AddPaymentIfNew(Sale sale, PaymentCaptured integrationEvent)
    {
        if (sale.Payments.All(item => item.PaymentId != integrationEvent.PaymentId))
        {
            Guid transactionId =
                integrationEvent.PaymentTransactionId is Guid captureTransactionId
                && captureTransactionId != Guid.Empty
                    ? captureTransactionId
                    : integrationEvent.PaymentId;
            sale.Payments.Add(
                new SalePayment
                {
                    PaymentId = integrationEvent.PaymentId,
                    SaleId = sale.Id,
                    TransactionId = transactionId,
                    Method = integrationEvent.Method,
                    Amount = integrationEvent.Amount,
                    TipAmount = integrationEvent.TipAmount,
                }
            );
        }
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
                    item.Format,
                    item.BaseUnitPrice,
                    item.Extras?.Select(extra => new SaleLineExtraSnapshot(
                            extra.ProductId,
                            extra.ProductName,
                            extra.UnitPrice,
                            extra.Quantity,
                            extra.TaxRate
                        ))
                        .ToList(),
                    item.TaxRate
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
                    sale.Payments.Sum(item => item.TipAmount),
                    sale.DiscountName,
                    sale.DiscountPercentage,
                    sale.DiscountAmount
                )
            );
        }

        store.MarkProcessed(messageId, now);
        await store.SaveAsync(cancellationToken);
    }
}
