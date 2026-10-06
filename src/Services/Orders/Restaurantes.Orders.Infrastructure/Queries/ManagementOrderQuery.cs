using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;
using Restaurantes.Orders.Infrastructure.Persistence.Write;

namespace Restaurantes.Orders.Infrastructure.Queries;

public sealed class ManagementOrderQuery(OrderWriteDbContext db, TimeZoneInfo timeZone)
    : IManagementOrderQuery
{
    public async Task<IReadOnlyList<ManagementOrderResponse>> ListAsync(
        Guid restaurantId,
        DateOnly date,
        int offset,
        CancellationToken ct
    )
    {
        DateTime localStart = DateTime.SpecifyKind(
            date.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified
        );
        DateTime start = TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone);
        DateTime end = TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), timeZone);
        List<Order> orders = await db
            .Orders.AsNoTracking()
            .Where(order =>
                order.RestaurantId == restaurantId
                && (
                    order.BusinessDate == date
                    || (
                        order.BusinessDate == null
                        && order.CreatedAtUtc >= start
                        && order.CreatedAtUtc < end
                    )
                )
            )
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .Skip(offset)
            .Take(100)
            .Include(order => order.Lines)
            .ToListAsync(ct);
        Dictionary<Guid, OrderPaymentProjection> payments = await db
            .OrderPayments.AsNoTracking()
            .Where(payment => orders.Select(order => order.Id).Contains(payment.OrderId))
            .ToDictionaryAsync(payment => payment.OrderId, ct);
        return orders
            .Select(order => new ManagementOrderResponse(
                order.Id,
                order.RestaurantId,
                order.TableLabel,
                order.CustomerName,
                order.Source.ToString(),
                order.Status.ToString(),
                order
                    .Lines.Where(line => line.Status == OrderLineStatus.Active)
                    .Sum(line => line.UnitPrice * line.Quantity),
                order.CreatedAtUtc,
                TimeZoneInfo.ConvertTime(
                    new DateTimeOffset(DateTime.SpecifyKind(order.CreatedAtUtc, DateTimeKind.Utc)),
                    timeZone
                ),
                payments.GetValueOrDefault(order.Id)?.Method,
                payments.GetValueOrDefault(order.Id)?.CapturedAtUtc is { } capturedAtUtc
                    ? TimeZoneInfo.ConvertTime(
                        new DateTimeOffset(DateTime.SpecifyKind(capturedAtUtc, DateTimeKind.Utc)),
                        timeZone
                    )
                    : null,
                order
                    .Lines.Select(line => new ManagementOrderLineResponse(
                        line.ProductName,
                        line.Quantity,
                        line.UnitPrice,
                        line.Status.ToString()
                    ))
                    .ToList()
            ))
            .ToList();
    }
}
