using Restaurantes.Orders.Contracts;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Contracts.Requests;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;

namespace Restaurantes.Orders.Application;

public sealed partial class OrderCommandService
{
    public async Task<OrderResponse?> SubmitAsync(Guid id, CancellationToken ct)
    {
        Order? o = await store.FindAsync(id, ct);
        if (o is null)
        {
            return null;
        }

        if (o.PaymentTiming == PaymentTiming.Immediate && !await payments.IsPaidAsync(id, ct))
        {
            throw new InvalidOperationException(
                "Immediate orders must be paid before they can be submitted to the kitchen."
            );
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        o.Submit(now);
        await store.SaveWithEventAsync(
            o,
            new OrderSubmitted(
                o.Id,
                o.RestaurantId,
                o.TableId,
                o.GuestCount,
                o.TableLabel,
                o.CustomerName,
                o.ServiceMode.ToString(),
                o.Source.ToString(),
                o.PaymentTiming.ToString(),
                o.Version,
                o.CreatedAtUtc,
                now,
                Lines(o),
                Stations(o)
            ),
            ct
        );
        return Map(o);
    }

    public async Task<OrderResponse?> DeliverAsync(Guid id, CancellationToken ct)
    {
        Order? o = await store.FindAsync(id, ct);
        if (o is null)
        {
            return null;
        }

        if (o.ServiceMode == ServiceMode.Takeaway && !await payments.IsPaidAsync(id, ct))
        {
            throw new InvalidOperationException(
                "Takeaway orders must be paid before they can be handed to the customer."
            );
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        o.Deliver(now);
        await store.SaveWithEventAsync(o, DeliveredEvent(o, now), ct);
        return Map(o);
    }
}
