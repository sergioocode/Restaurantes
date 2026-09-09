using Restaurantes.Orders.Contracts;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Contracts.Requests;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;

namespace Restaurantes.Orders.Application;

public sealed partial class OrderCommandService
{
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
