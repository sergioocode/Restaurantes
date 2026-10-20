using Restaurantes.Orders.Domain;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrderOverallCoverageTests
{
    [Theory]
    [InlineData(OrderSource.CustomerQr, ServiceMode.DineIn, PaymentTiming.Immediate)]
    [InlineData(OrderSource.Comandero, ServiceMode.DineIn, PaymentTiming.OnAccount)]
    [InlineData(OrderSource.Pos, ServiceMode.Bar, PaymentTiming.OnAccount)]
    [InlineData(OrderSource.Pos, ServiceMode.Takeaway, PaymentTiming.OnAccount)]
    public void Create_accepts_supported_source_service_and_payment_combinations(
        OrderSource source,
        ServiceMode serviceMode,
        PaymentTiming paymentTiming
    )
    {
        bool hasDiningLocation = serviceMode is ServiceMode.DineIn or ServiceMode.Bar;

        Order order = Order.Create(
            Guid.NewGuid(),
            hasDiningLocation ? Guid.NewGuid() : null,
            2,
            hasDiningLocation ? Guid.NewGuid() : null,
            " Mesa 1 ",
            serviceMode == ServiceMode.Takeaway ? " Ana " : string.Empty,
            serviceMode,
            source,
            paymentTiming,
            [Draft()],
            Timestamp
        );

        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Equal("Mesa 1", order.TableLabel);
        Assert.Equal(
            serviceMode == ServiceMode.Takeaway ? "Ana" : string.Empty,
            order.CustomerName
        );
    }

    [Fact]
    public void Create_rejects_missing_restaurant_location_customer_and_lines()
    {
        Assert.Throws<ArgumentException>(() => CreateOrder(restaurantId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateOrder(tableLabel: " "));
        Assert.Throws<ArgumentException>(() =>
            Order.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                null,
                "Mesa 1",
                string.Empty,
                ServiceMode.DineIn,
                OrderSource.Pos,
                PaymentTiming.OnAccount,
                [Draft()],
                Timestamp
            )
        );
        Assert.Throws<ArgumentException>(() =>
            Order.Create(
                Guid.NewGuid(),
                null,
                null,
                null,
                "Mesa 1",
                string.Empty,
                ServiceMode.Takeaway,
                OrderSource.Pos,
                PaymentTiming.OnAccount,
                [Draft()],
                Timestamp
            )
        );
        Assert.Throws<ArgumentException>(() => CreateOrder(lines: []));
        Assert.Throws<ArgumentException>(() => CreateOrder(customerName: "Ana"));
    }

    [Fact]
    public void Create_rejects_unsupported_payment_flow()
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                Guid.NewGuid(),
                "Mesa 1",
                string.Empty,
                ServiceMode.DineIn,
                OrderSource.CustomerQr,
                PaymentTiming.OnAccount,
                [Draft()],
                Timestamp
            )
        );
    }

    [Fact]
    public void Cancelling_the_last_draft_line_cancels_the_order_and_its_station()
    {
        Order order = CreateOrder(lines: [Draft("COCINA")]);
        OrderLine line = Assert.Single(order.Lines);

        order.CancelLine(line.Id, "Sin stock", Timestamp);

        Assert.Equal(OrderLineStatus.Cancelled, line.Status);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(KitchenTicketStatus.Cancelled, Assert.Single(order.Stations).Status);
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void Cancelling_one_draft_line_keeps_other_lines_and_the_order_active()
    {
        Order order = CreateOrder(lines: [Draft("COCINA"), Draft("BEBIDAS")]);
        OrderLine line = order.Lines.First();

        order.CancelLine(line.Id, "Sin stock", Timestamp);

        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Equal(OrderLineStatus.Cancelled, line.Status);
        Assert.Single(order.Lines, item => item.Status == OrderLineStatus.Active);
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void Cancelling_lines_after_submission_recalculates_order_status()
    {
        Order order = CreateOrder(lines: [Draft("COCINA"), Draft("BEBIDAS")]);
        OrderLine[] lines = order.Lines.ToArray();
        order.Submit(Timestamp);

        order.CancelLine(lines[0].Id, "Sin stock", Timestamp.AddMinutes(1));
        Assert.Equal(OrderStatus.Submitted, order.Status);

        order.CancelLine(lines[1].Id, "Sin stock", Timestamp.AddMinutes(2));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Ready_order_can_be_delivered_but_cannot_be_cancelled()
    {
        Order order = CreateOrder(lines: [Draft("COCINA", requiresPrimaryDispatch: true)]);
        OrderLine line = Assert.Single(order.Lines);
        order.Submit(Timestamp);
        order.StartLinePreparation(line.Id, Timestamp.AddMinutes(1));
        order.MarkLineReady(line.Id, Timestamp.AddMinutes(2));
        order.DispatchLine(line.Id, Timestamp.AddMinutes(3));

        Assert.Equal(OrderStatus.Ready, order.Status);
        order.Deliver(Timestamp.AddMinutes(4));

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(DateTimeKind.Utc, order.DeliveredAtUtc?.Kind);
        Assert.Throws<InvalidOperationException>(() => order.Cancel("No cancelar", Timestamp));
        Assert.Throws<InvalidOperationException>(() => order.Deliver(Timestamp));
    }

    [Fact]
    public void Draft_order_requires_a_valid_cancellation_reason_and_can_then_be_cancelled()
    {
        Order order = CreateOrder(lines: [Draft("COCINA", requiresPrimaryDispatch: true)]);

        Assert.Throws<ArgumentException>(() => order.Cancel("No", Timestamp));
        order.Cancel("Pedido duplicado", Timestamp);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(KitchenTicketStatus.Cancelled, Assert.Single(order.Stations).Status);
        Assert.Throws<InvalidOperationException>(() => order.Cancel("Segundo intento", Timestamp));
    }

    [Fact]
    public void Primary_dispatch_respects_station_priority()
    {
        Order order = CreateOrder(
            lines:
            [
                Draft("COCINA", requiresPrimaryDispatch: true, priority: 1),
                Draft("BEBIDAS", requiresPrimaryDispatch: true, priority: 2),
            ]
        );
        OrderLine[] lines = order.Lines.ToArray();
        order.Submit(Timestamp);
        foreach (OrderLine line in lines)
        {
            order.StartLinePreparation(line.Id, Timestamp.AddMinutes(1));
            order.MarkLineReady(line.Id, Timestamp.AddMinutes(2));
        }

        Assert.Throws<InvalidOperationException>(() =>
            order.DispatchLine(lines[1].Id, Timestamp.AddMinutes(3))
        );
        order.DispatchLine(lines[0].Id, Timestamp.AddMinutes(3));
        order.DispatchLine(lines[1].Id, Timestamp.AddMinutes(4));

        Assert.Equal(OrderStatus.Ready, order.Status);
    }

    private static DateTime Timestamp => DateTime.Parse("2026-10-20T10:00:00Z");

    private static Order CreateOrder(
        Guid? restaurantId = null,
        string tableLabel = "Mesa 1",
        Guid? diningSessionId = null,
        string customerName = "",
        IReadOnlyList<OrderLineDraft>? lines = null
    ) =>
        Order.Create(
            restaurantId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            diningSessionId ?? Guid.NewGuid(),
            tableLabel,
            customerName,
            ServiceMode.DineIn,
            OrderSource.Pos,
            PaymentTiming.OnAccount,
            lines ?? [Draft()],
            Timestamp
        );

    private static OrderLineDraft Draft(
        string stationCode = "COCINA",
        bool requiresPrimaryDispatch = false,
        int priority = 1
    ) =>
        new(
            Guid.NewGuid(),
            "Producto",
            10m,
            1,
            string.Empty,
            null,
            "General",
            stationCode,
            stationCode,
            requiresPrimaryDispatch,
            priority
        );
}
