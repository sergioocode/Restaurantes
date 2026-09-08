using Restaurantes.Orders.Contracts;
using Restaurantes.Orders.Domain;

namespace Restaurantes.Orders.Application;

public sealed class OrderCommandService(
    IOrderWriteStore store,
    IOrderCatalogStore catalog,
    IOrderPaymentStore payments,
    IDiningSessionStore dining,
    TimeProvider time
)
{
    public async Task<OrderResponse> CreateAsync(
        CreateOrderRequest request,
        string customerAccessToken,
        CancellationToken ct
    )
    {
        if (
            !Enum.TryParse(request.Source, false, out OrderSource source)
            || !Enum.TryParse(request.PaymentTiming, false, out PaymentTiming timing)
            || !Enum.TryParse(request.ServiceMode, false, out ServiceMode serviceMode)
        )
        {
            throw new ArgumentException("Order source or payment timing is invalid.");
        }

        int? guestCount = null;
        if (serviceMode is ServiceMode.DineIn or ServiceMode.Bar)
        {
            if (request.TableId is null || request.DiningSessionId is null)
            {
                throw new ArgumentException(
                    "DineIn and Bar orders require a physical location and DiningSessionId."
                );
            }
            guestCount = await dining.EnsureOpenAsync(
                request.DiningSessionId.Value,
                request.RestaurantId,
                request.TableId.Value,
                request.Source,
                request.ServiceMode,
                customerAccessToken,
                ct
            );
        }
        Guid[] ids = request.Lines.Select(x => x.ProductId).Distinct().ToArray();
        IReadOnlyDictionary<Guid, OrderCatalogProduct> products = await catalog.FindAsync(
            request.RestaurantId,
            ids,
            ct
        );
        List<Guid> unavailable = ids.Where(id =>
                !products.TryGetValue(id, out OrderCatalogProduct? p) || !p.IsAvailable
            )
            .ToList();
        if (unavailable.Count > 0)
        {
            throw new InvalidOperationException(
                $"Products are unavailable for this restaurant: {string.Join(", ", unavailable)}"
            );
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        Order order = Order.Create(
            request.RestaurantId,
            request.TableId,
            guestCount,
            request.DiningSessionId,
            request.TableLabel,
            request.CustomerName,
            serviceMode,
            source,
            timing,
            request.Lines.Select(line =>
            {
                OrderCatalogProduct p = products[line.ProductId];
                return new OrderLineDraft(
                    p.ProductId,
                    p.ProductName,
                    p.Price,
                    line.Quantity,
                    line.Notes,
                    p.CategoryId,
                    p.CategoryName,
                    p.StationCode,
                    p.StationName,
                    p.RequiresPrimaryDispatch,
                    p.Priority
                );
            }),
            now
        );
        await store.SaveWithEventAsync(
            order,
            new OrderCreated(
                order.Id,
                order.RestaurantId,
                order.TableId,
                order.GuestCount,
                order.DiningSessionId,
                order.TableLabel,
                order.CustomerName,
                order.ServiceMode.ToString(),
                order.Source.ToString(),
                order.PaymentTiming.ToString(),
                order.Version,
                now,
                Lines(order),
                Stations(order)
            ),
            ct
        );
        return Map(order);
    }

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

    public async Task<OrderResponse?> CancelTakeawayAsync(
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
        if (await payments.IsPaidAsync(id, ct) && !await payments.IsRefundedAsync(id, ct))
        {
            throw new InvalidOperationException(
                "Paid takeaway orders must be refunded before they can be cancelled."
            );
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        o.CancelTakeaway(reason, now);
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

    private static IReadOnlyList<OrderLineSnapshot> Lines(Order o)
    {
        return o
            .Lines.Select(x => new OrderLineSnapshot(
                x.Id,
                x.ProductId,
                x.ProductName,
                x.UnitPrice,
                x.Quantity,
                x.Notes,
                x.CategoryId,
                x.CategoryName,
                x.PreparationStationCode,
                x.PreparationStationName,
                x.Status.ToString(),
                x.CancelledAtUtc,
                x.CancellationReason
            ))
            .ToList();
    }

    private static OrderDelivered DeliveredEvent(Order o, DateTime deliveredAtUtc)
    {
        return new OrderDelivered(
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
            o.SubmittedAtUtc!.Value,
            o.PreparationStartedAtUtc!.Value,
            o.ReadyAtUtc!.Value,
            deliveredAtUtc,
            Lines(o),
            Stations(o)
        );
    }

    private static OrderReady ReadyEvent(Order o, DateTime readyAtUtc)
    {
        return new OrderReady(
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
            o.SubmittedAtUtc!.Value,
            o.PreparationStartedAtUtc!.Value,
            readyAtUtc,
            Lines(o),
            Stations(o)
        );
    }

    private static IReadOnlyList<OrderKitchenStationSnapshot> Stations(Order o)
    {
        return o
            .Stations.OrderBy(x => x.Code)
            .Select(x => new OrderKitchenStationSnapshot(
                x.Code,
                x.Name,
                x.Status.ToString(),
                x.RequiresPrimaryDispatch,
                x.Priority,
                x.PreparationStartedAtUtc,
                x.ReadyAtUtc,
                x.DispatchedAtUtc
            ))
            .ToList();
    }

    private static OrderResponse Map(Order o)
    {
        return new(
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
            o.Status.ToString(),
            Total(o),
            o.Version,
            o.CreatedAtUtc,
            o.SubmittedAtUtc,
            o.PreparationStartedAtUtc,
            o.ReadyAtUtc,
            o.DeliveredAtUtc,
            o.Lines.Select(x => new OrderLineResponse(
                    x.Id,
                    x.ProductId,
                    x.ProductName,
                    x.UnitPrice,
                    x.Quantity,
                    x.UnitPrice * x.Quantity,
                    x.Notes,
                    x.CategoryId,
                    x.CategoryName,
                    x.PreparationStationCode,
                    x.PreparationStationName,
                    x.Status.ToString(),
                    x.CancelledAtUtc,
                    x.CancellationReason
                ))
                .ToList(),
            o.Stations.OrderBy(x => x.Code)
                .Select(x => new OrderKitchenStationResponse(
                    x.Code,
                    x.Name,
                    x.Status.ToString(),
                    x.RequiresPrimaryDispatch,
                    x.Priority,
                    x.PreparationStartedAtUtc,
                    x.ReadyAtUtc,
                    x.DispatchedAtUtc
                ))
                .ToList()
        );
    }

    private static decimal Total(Order order)
    {
        return order
            .Lines.Where(line => line.Status == OrderLineStatus.Active)
            .Sum(line => line.UnitPrice * line.Quantity);
    }
}
