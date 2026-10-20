using System.Text.Json;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.Sales.Contracts.Responses;
using Restaurantes.Sales.Domain;

namespace Restaurantes.Sales.Application;

public sealed class SaleTicketQueryService(ISaleWriteStore store)
{
    public async Task<SaleResponse?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        Sale? sale = await store.FindAsync(id, cancellationToken);
        if (
            sale is null
            || sale.Status != "Completed"
            || sale.CompletedAtUtc is not DateTime completedAtUtc
        )
        {
            return null;
        }

        List<SaleLineSnapshot> lines = sale
            .Orders.SelectMany(order =>
                JsonSerializer.Deserialize<List<SaleLineSnapshot>>(order.LinesJson) ?? []
            )
            .ToList();
        List<SalePaymentBreakdown> paymentMethods = sale
            .Payments.GroupBy(payment => payment.Method)
            .Select(group => new SalePaymentBreakdown(
                group.Key,
                group.Select(payment => payment.TransactionId).Distinct().Count(),
                group.Sum(payment => payment.Amount)
            ))
            .ToList();

        return new(
            sale.Id,
            sale.RestaurantId,
            sale.Source,
            sale.PaymentMethod,
            sale.Total,
            sale.Status,
            sale.Orders.Count,
            completedAtUtc,
            sale.Orders.Select(order => order.OrderId).ToList(),
            lines,
            sale.Payments.Sum(payment => payment.TipAmount),
            paymentMethods,
            sale.DiscountName,
            sale.DiscountPercentage,
            sale.DiscountAmount
        );
    }
}
