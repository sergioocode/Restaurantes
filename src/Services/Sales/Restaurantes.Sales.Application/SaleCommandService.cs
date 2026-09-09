using System.Text.Json;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Sales.Contracts;
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
            integrationEvent.PaymentTransactionId is Guid transactionId
            && transactionId != Guid.Empty
                ? transactionId
                : integrationEvent.PaymentId;
        Sale? sale = await store.FindAsync(saleId, cancellationToken);
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
            };
            store.Add(sale);
        }
        else
        {
            sale.ExpectedOrderCount = Math.Max(
                sale.ExpectedOrderCount,
                Math.Max(1, integrationEvent.TransactionOrderCount)
            );
        }

        if (sale.Orders.All(item => item.OrderId != integrationEvent.OrderId))
        {
            DeliveredOrderSnapshot? delivery = await store.FindDeliveredOrderAsync(
                integrationEvent.OrderId,
                cancellationToken
            );
            sale.Orders.Add(
                new SaleOrder
                {
                    SaleId = saleId,
                    OrderId = integrationEvent.OrderId,
                    Amount = integrationEvent.Amount,
                    IsPaid = true,
                    PaidAtUtc = integrationEvent.CapturedAtUtc,
                    IsDelivered = delivery is not null,
                    DeliveredAtUtc = delivery?.DeliveredAtUtc,
                    LinesJson = delivery?.LinesJson ?? "[]",
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
                    item.PreparationStationCode
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
                    lines
                )
            );
        }

        store.MarkProcessed(messageId, now);
        await store.SaveAsync(cancellationToken);
    }
}
