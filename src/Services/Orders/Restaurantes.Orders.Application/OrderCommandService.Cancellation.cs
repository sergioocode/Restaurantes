using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;

namespace Restaurantes.Orders.Application;

public sealed partial class OrderCommandService
{
    public async Task<OrderResponse?> CancelAsync(
        Guid id,
        string reason,
        string cancelledBy,
        CancellationToken ct
    )
    {
        Order? o = await store.FindAsync(id, ct);
        if (o is null)
        {
            return null;
        }
        if (await payments.IsPaidAsync(id, ct))
        {
            throw new InvalidOperationException(
                "Paid orders cannot be cancelled here. Any refund must be handled by Accounting."
            );
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        o.Cancel(reason, now);
        await store.SaveWithEventAsync(
            o,
            new OrderCancelled(
                o.Id,
                o.RestaurantId,
                o.TableId,
                o.GuestCount,
                o.DiningSessionId,
                o.TableLabel,
                o.CustomerName,
                o.ServiceMode.ToString(),
                o.Source.ToString(),
                o.PaymentTiming.ToString(),
                reason.Trim(),
                cancelledBy.Trim(),
                o.Version,
                o.CreatedAtUtc,
                o.SubmittedAtUtc,
                now,
                Lines(o),
                Stations(o)
            ),
            ct
        );
        return Map(o);
    }

    public async Task<OrderResponse?> CancelLineAsync(
        Guid id,
        Guid lineId,
        string reason,
        CancellationToken ct
    )
    {
        Order? o = await store.FindAsync(id, ct);
        if (o is null)
        {
            return null;
        }
        if (await payments.IsPaidAsync(id, ct))
        {
            throw new InvalidOperationException(
                "Paid order lines require the Backoffice refund workflow and cannot be cancelled here."
            );
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        o.CancelLine(lineId, reason, now);
        OrderLine changedLine = o.Lines.Single(item => item.Id == lineId);
        await store.SaveWithEventAsync(
            o,
            new OrderLineCancelled(
                o.Id,
                o.RestaurantId,
                o.TableId,
                o.GuestCount,
                o.DiningSessionId,
                o.TableLabel,
                o.CustomerName,
                o.ServiceMode.ToString(),
                o.Source.ToString(),
                o.PaymentTiming.ToString(),
                changedLine.Id,
                changedLine.PreparationStationCode,
                changedLine.CancellationReason,
                o.Status.ToString(),
                Total(o),
                o.Version,
                o.CreatedAtUtc,
                o.SubmittedAtUtc,
                now,
                Lines(o),
                Stations(o)
            ),
            ct
        );
        return Map(o);
    }
}
