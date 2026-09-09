using Restaurantes.Orders.Contracts;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Contracts.Requests;
using Restaurantes.Orders.Contracts.Responses;
using Restaurantes.Orders.Domain;

namespace Restaurantes.Orders.Application;

public sealed partial class OrderCommandService
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
}
