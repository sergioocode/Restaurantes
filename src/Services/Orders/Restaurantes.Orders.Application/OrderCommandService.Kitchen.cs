using Restaurantes.Orders.Contracts;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Contracts.Requests;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;

namespace Restaurantes.Orders.Application;

public sealed partial class OrderCommandService
{
    public async Task<OrderResponse?> StartPreparationAsync(
        Guid id,
        string station,
        CancellationToken ct
    )
    {
        Order? o = await store.FindAsync(id, ct);
        if (o is null)
        {
            return null;
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        o.StartStationPreparation(station, now);
        await store.SaveWithEventAsync(
            o,
            new KitchenTicketPreparationStarted(
                o.Id,
                o.RestaurantId,
                o.TableId,
                o.GuestCount,
                o.TableLabel,
                o.CustomerName,
                o.ServiceMode.ToString(),
                o.Source.ToString(),
                o.PaymentTiming.ToString(),
                station.Trim().ToUpperInvariant(),
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

    public async Task<OrderResponse?> MarkReadyAsync(Guid id, string station, CancellationToken ct)
    {
        Order? o = await store.FindAsync(id, ct);
        if (o is null)
        {
            return null;
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        o.MarkStationReady(station, now);
        object integrationEvent =
            o.Status == OrderStatus.Ready
                ? ReadyEvent(o, now)
                : new KitchenTicketReady(
                    o.Id,
                    o.RestaurantId,
                    o.TableId,
                    o.GuestCount,
                    o.TableLabel,
                    o.CustomerName,
                    o.ServiceMode.ToString(),
                    o.Source.ToString(),
                    o.PaymentTiming.ToString(),
                    station.Trim().ToUpperInvariant(),
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

    public async Task<OrderResponse?> DispatchStationAsync(
        Guid id,
        string station,
        CancellationToken ct
    )
    {
        Order? o = await store.FindAsync(id, ct);
        if (o is null)
        {
            return null;
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        o.DispatchStation(station, now);
        object integrationEvent =
            o.Status == OrderStatus.Ready
                ? ReadyEvent(o, now)
                : new KitchenTicketDispatched(
                    o.Id,
                    o.RestaurantId,
                    o.TableId,
                    o.GuestCount,
                    o.TableLabel,
                    o.CustomerName,
                    o.ServiceMode.ToString(),
                    o.Source.ToString(),
                    o.PaymentTiming.ToString(),
                    station.Trim().ToUpperInvariant(),
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
