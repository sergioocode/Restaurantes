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
        OrderCashRegisterSession cashSession,
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
                    "DineIn and Bar orders require a location and DiningSessionId."
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
                !products.TryGetValue(id, out OrderCatalogProduct? p)
                || (source != OrderSource.Pos && !p.IsAvailable)
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
                string format = string.IsNullOrWhiteSpace(line.Format)
                    ? p.DefaultFormat
                    : line.Format.Trim().ToUpperInvariant();
                OrderCatalogFormat? currentFormatPrice = p.Formats.FirstOrDefault(x =>
                    x.Format == format
                );
                OrderCatalogFormat? formatPrice =
                    source == OrderSource.Pos
                        ? new OrderCatalogFormat(format, currentFormatPrice?.Price ?? p.Price)
                        : currentFormatPrice;
                if (formatPrice is null)
                    throw new ArgumentException(
                        $"El formato {format} de {p.ProductName} no está disponible."
                    );
                CreateOrderExtraRequest[] requestedExtras = line.SelectedExtras?.ToArray() ?? [];
                List<string> selectedExtraNames = [];
                List<OrderLineExtra> selectedExtraDetails = [];
                decimal extrasPrice = 0;
                IReadOnlyList<OrderExtraGroup> extraGroups = p.ExtraGroups;
                foreach (var group in extraGroups)
                {
                    var selections = requestedExtras.Where(x => x.GroupId == group.Id).ToArray();
                    int selectionCount = selections.Sum(x => x.Quantity);
                    if (
                        selectionCount < group.MinSelections
                        || selectionCount > group.MaxSelections
                    )
                        throw new ArgumentException(
                            $"La selección de extras para {p.ProductName} no es válida."
                        );

                    foreach (var selection in selections)
                    {
                        var option = group.Options.FirstOrDefault(x =>
                            x.ProductId == selection.ProductId
                        );
                        if (option is null || selection.Quantity is < 1 or > 99)
                            throw new ArgumentException(
                                $"Uno de los extras de {p.ProductName} no está disponible."
                            );
                        extrasPrice += option.Price * selection.Quantity;
                        selectedExtraNames.Add($"{option.ProductName} × {selection.Quantity}");
                        selectedExtraDetails.Add(
                            new OrderLineExtra(
                                option.ProductId,
                                option.ProductName,
                                option.Price,
                                selection.Quantity
                            )
                        );
                    }
                }
                if (
                    requestedExtras.Any(selection =>
                        extraGroups.All(group => group.Id != selection.GroupId)
                    )
                )
                    throw new ArgumentException($"Los extras de {p.ProductName} no son válidos.");
                string notes = line.Notes?.Trim() ?? string.Empty;
                if (selectedExtraNames.Count > 0)
                {
                    string extrasNote = $"Extras: {string.Join(", ", selectedExtraNames)}";
                    notes = string.IsNullOrWhiteSpace(notes)
                        ? extrasNote
                        : $"{notes} · {extrasNote}";
                }
                if (notes.Length > 500)
                    throw new ArgumentException(
                        $"Las notas y extras de {p.ProductName} superan el límite permitido."
                    );
                decimal unitPrice =
                    source == OrderSource.Pos && line.ExpectedUnitPrice is decimal expectedUnitPrice
                        ? expectedUnitPrice
                        : formatPrice.Price + extrasPrice;
                decimal baseUnitPrice = unitPrice - extrasPrice;
                if (baseUnitPrice < 0)
                    throw new ArgumentException(
                        $"El precio base de {p.ProductName} no puede ser inferior al precio de sus extras."
                    );
                return new OrderLineDraft(
                    p.ProductId,
                    p.ProductName,
                    unitPrice,
                    line.Quantity,
                    notes,
                    p.CategoryId,
                    p.CategoryName,
                    p.StationCode,
                    p.StationName,
                    p.RequiresPrimaryDispatch,
                    p.Priority,
                    format,
                    baseUnitPrice,
                    selectedExtraDetails
                );
            }),
            now,
            cashSession.SessionId,
            cashSession.BusinessDate
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
