using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;

namespace Restaurantes.Orders.Application;

public sealed partial class OrderCommandService
{
    public async Task<OrderResponse?> StartLinePreparationAsync(
        Guid id,
        Guid lineId,
        CancellationToken ct
    )
    {
        Order? o = await store.FindAsync(id, ct);
        if (o is null)
        {
            return null;
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        OrderLine line = o.Lines.Single(item => item.Id == lineId);
        o.StartLinePreparation(lineId, now);
        await store.SaveWithEventAsync(
            o,
            new KitchenTicketPreparationStarted(
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
                line.PreparationStationCode,
                o.Version,
                o.CreatedAtUtc,
                o.SubmittedAtUtc!.Value,
                o.PreparationStartedAtUtc!.Value,
                Lines(o),
                Stations(o)
            ),
            ct
        );
        return Map(o);
    }

    public async Task<OrderResponse?> MarkLineReadyAsync(Guid id, Guid lineId, CancellationToken ct)
    {
        Order? o = await store.FindAsync(id, ct);
        if (o is null)
        {
            return null;
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        OrderLine line = o.Lines.Single(item => item.Id == lineId);
        o.MarkLineReady(lineId, now);
        object integrationEvent =
            o.Status == OrderStatus.Ready
                ? ReadyEvent(o, now)
                : new KitchenTicketReady(
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
                    line.PreparationStationCode,
                    o.Version,
                    o.CreatedAtUtc,
                    o.SubmittedAtUtc!.Value,
                    o.PreparationStartedAtUtc!.Value,
                    o.ReadyAtUtc,
                    Lines(o),
                    Stations(o)
                );
        await store.SaveWithEventAsync(o, integrationEvent, ct);
        return Map(o);
    }

    public async Task<OrderResponse?> DispatchLineAsync(Guid id, Guid lineId, CancellationToken ct)
    {
        Order? o = await store.FindAsync(id, ct);
        if (o is null)
        {
            return null;
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        OrderLine line = o.Lines.Single(item => item.Id == lineId);
        o.DispatchLine(lineId, now);
        object integrationEvent =
            o.Status == OrderStatus.Ready
                ? ReadyEvent(o, now)
                : new KitchenTicketDispatched(
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
                    line.PreparationStationCode,
                    o.Version,
                    o.CreatedAtUtc,
                    o.SubmittedAtUtc!.Value,
                    o.PreparationStartedAtUtc!.Value,
                    now,
                    Lines(o),
                    Stations(o)
                );
        await store.SaveWithEventAsync(o, integrationEvent, ct);
        return Map(o);
    }
}
