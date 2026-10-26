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
        CancellationToken ct,
        Guid? createdByUserId = null
    )
    {
        (OrderSource source, PaymentTiming timing, ServiceMode serviceMode) = ParseOrderTypes(
            request
        );
        (int? guestCount, Guid? zoneId) = await ValidateDiningSessionAsync(
            request,
            serviceMode,
            customerAccessToken,
            ct
        );
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
            request.Lines.Select(line => BuildLineDraft(line, products[line.ProductId], source)),
            now,
            cashSession.SessionId,
            cashSession.BusinessDate,
            zoneId
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
                Stations(order),
                createdByUserId,
                zoneId
            ),
            ct
        );
        return Map(order);
    }

    private static (
        OrderSource Source,
        PaymentTiming Timing,
        ServiceMode ServiceMode
    ) ParseOrderTypes(CreateOrderRequest request)
    {
        if (
            !Enum.TryParse(request.Source, false, out OrderSource source)
            || !Enum.TryParse(request.PaymentTiming, false, out PaymentTiming timing)
            || !Enum.TryParse(request.ServiceMode, false, out ServiceMode serviceMode)
        )
        {
            throw new ArgumentException("Order source or payment timing is invalid.");
        }

        return (source, timing, serviceMode);
    }

    private async Task<(int? GuestCount, Guid? ZoneId)> ValidateDiningSessionAsync(
        CreateOrderRequest request,
        ServiceMode serviceMode,
        string customerAccessToken,
        CancellationToken ct
    )
    {
        if (request.DiningSessionId is null)
        {
            if (serviceMode != ServiceMode.Takeaway || request.TableId is not null)
                throw new ArgumentException("Orders require an open dining session.");

            return (null, null);
        }

        if (
            (serviceMode is ServiceMode.DineIn or ServiceMode.Bar) != (request.TableId is not null)
            || (serviceMode == ServiceMode.Takeaway && request.Source != "Pos")
        )
        {
            throw new ArgumentException("The order location does not match its service mode.");
        }

        DiningSessionValidation validation = await dining.EnsureOpenAsync(
            request.DiningSessionId.Value,
            request.RestaurantId,
            request.TableId,
            request.Source,
            request.ServiceMode,
            customerAccessToken,
            ct
        );
        if (
            request.TableId is not null
            && (validation.ZoneId is null || validation.ZoneId == Guid.Empty)
        )
        {
            throw new InvalidOperationException("The dining table zone could not be resolved.");
        }

        return (validation.GuestCount, validation.ZoneId);
    }

    private static OrderLineDraft BuildLineDraft(
        CreateOrderLineRequest line,
        OrderCatalogProduct product,
        OrderSource source
    )
    {
        string format = string.IsNullOrWhiteSpace(line.Format)
            ? product.DefaultFormat
            : line.Format.Trim().ToUpperInvariant();
        OrderCatalogFormat? currentFormatPrice = product.Formats.FirstOrDefault(x =>
            x.Format == format
        );
        OrderCatalogFormat? formatPrice =
            source == OrderSource.Pos
                ? new OrderCatalogFormat(format, currentFormatPrice?.Price ?? product.Price)
                : currentFormatPrice;
        if (formatPrice is null)
            throw new ArgumentException(
                $"El formato {format} de {product.ProductName} no está disponible."
            );

        CreateOrderExtraRequest[] requestedExtras = line.SelectedExtras?.ToArray() ?? [];
        (List<string> names, List<OrderLineExtra> details, decimal amount) = BuildSelectedExtras(
            product,
            requestedExtras
        );
        if (
            requestedExtras.Any(selection =>
                product.ExtraGroups.All(group => group.Id != selection.GroupId)
            )
        )
            throw new ArgumentException($"Los extras de {product.ProductName} no son válidos.");

        string notes = BuildLineNotes(line.Notes, names, product.ProductName);
        decimal unitPrice =
            source == OrderSource.Pos && line.ExpectedUnitPrice is decimal expectedUnitPrice
                ? expectedUnitPrice
                : formatPrice.Price + amount;
        decimal baseUnitPrice = unitPrice - amount;
        if (baseUnitPrice < 0)
            throw new ArgumentException(
                $"El precio base de {product.ProductName} no puede ser inferior al precio de sus extras."
            );

        return new OrderLineDraft(
            product.ProductId,
            product.ProductName,
            unitPrice,
            line.Quantity,
            notes,
            product.CategoryId,
            product.CategoryName,
            product.StationCode,
            product.StationName,
            product.RequiresPrimaryDispatch,
            product.Priority,
            format,
            baseUnitPrice,
            details,
            product.RequiresKds,
            product.TaxRate
        );
    }

    private static (
        List<string> Names,
        List<OrderLineExtra> Details,
        decimal Amount
    ) BuildSelectedExtras(
        OrderCatalogProduct product,
        IReadOnlyCollection<CreateOrderExtraRequest> requestedExtras
    )
    {
        List<string> names = [];
        List<OrderLineExtra> details = [];
        decimal amount = 0;
        foreach (OrderExtraGroup group in product.ExtraGroups)
        {
            CreateOrderExtraRequest[] selections = requestedExtras
                .Where(extra => extra.GroupId == group.Id)
                .ToArray();
            int selectionCount = selections.Sum(extra => extra.Quantity);
            if (selectionCount < group.MinSelections || selectionCount > group.MaxSelections)
                throw new ArgumentException(
                    $"La selección de extras para {product.ProductName} no es válida."
                );

            foreach (CreateOrderExtraRequest selection in selections)
            {
                OrderExtraOption? option = group.Options.FirstOrDefault(extra =>
                    extra.ProductId == selection.ProductId
                );
                if (option is null || selection.Quantity is < 1 or > 99)
                    throw new ArgumentException(
                        $"Uno de los extras de {product.ProductName} no está disponible."
                    );
                amount += option.Price * selection.Quantity;
                names.Add($"{option.ProductName} × {selection.Quantity}");
                details.Add(
                    new OrderLineExtra(
                        option.ProductId,
                        option.ProductName,
                        option.Price,
                        selection.Quantity,
                        option.TaxRate
                    )
                );
            }
        }
        return (names, details, amount);
    }

    private static string BuildLineNotes(
        string? originalNotes,
        List<string> extraNames,
        string productName
    )
    {
        string notes = originalNotes?.Trim() ?? string.Empty;
        if (extraNames.Count > 0)
        {
            string extrasNote = $"Extras: {string.Join(", ", extraNames)}";
            notes = string.IsNullOrWhiteSpace(notes) ? extrasNote : $"{notes} · {extrasNote}";
        }
        if (notes.Length > 500)
            throw new ArgumentException(
                $"Las notas y extras de {productName} superan el límite permitido."
            );
        return notes;
    }
}
