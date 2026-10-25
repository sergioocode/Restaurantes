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
        bool hasDiningSession = hasDiningLocation || serviceMode == ServiceMode.Takeaway;

        Order order = Order.Create(
            Guid.NewGuid(),
            hasDiningLocation ? Guid.NewGuid() : null,
            2,
            hasDiningSession ? Guid.NewGuid() : null,
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
        Assert.Throws<InvalidOperationException>(() =>
            order.CancelLine(lines[0].Id, "Sin stock", Timestamp.AddMinutes(2))
        );

        order.CancelLine(lines[1].Id, "Sin stock", Timestamp.AddMinutes(2));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Kitchen_transitions_reject_wrong_order_state_unknown_lines_and_cancelled_lines()
    {
        Order draft = CreateOrder();
        Guid unknownLineId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() =>
            draft.StartLinePreparation(unknownLineId, Timestamp)
        );
        Assert.Throws<InvalidOperationException>(() =>
            draft.MarkLineReady(unknownLineId, Timestamp)
        );
        Assert.Throws<InvalidOperationException>(() =>
            draft.DispatchLine(unknownLineId, Timestamp)
        );

        Order submitted = CreateOrder(lines: [Draft(), Draft("BEBIDAS")]);
        OrderLine[] lines = submitted.Lines.ToArray();
        submitted.Submit(Timestamp);
        submitted.CancelLine(lines[0].Id, "Sin stock", Timestamp.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            submitted.StartLinePreparation(lines[0].Id, Timestamp.AddMinutes(2))
        );
        Assert.Throws<InvalidOperationException>(() =>
            submitted.StartLinePreparation(unknownLineId, Timestamp.AddMinutes(2))
        );
        Assert.Throws<InvalidOperationException>(() =>
            submitted.MarkLineReady(lines[1].Id, Timestamp.AddMinutes(2))
        );
    }

    [Fact]
    public void Kitchen_dispatch_and_cancellation_reject_invalid_line_lifecycle_transitions()
    {
        Order order = CreateOrder(lines: [Draft(requiresPrimaryDispatch: true)]);
        OrderLine line = Assert.Single(order.Lines);
        order.Submit(Timestamp);

        Assert.Throws<InvalidOperationException>(() =>
            order.DispatchLine(line.Id, Timestamp.AddMinutes(1))
        );
        order.StartLinePreparation(line.Id, Timestamp.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() =>
            order.StartLinePreparation(line.Id, Timestamp.AddMinutes(2))
        );
        Assert.Throws<InvalidOperationException>(() =>
            order.DispatchLine(line.Id, Timestamp.AddMinutes(3))
        );
        Assert.Throws<InvalidOperationException>(() =>
            order.CancelLine(line.Id, "Sin stock", Timestamp.AddMinutes(3))
        );
        Assert.Throws<InvalidOperationException>(() => order.Cancel("Pedido duplicado", Timestamp));
    }

    [Fact]
    public void Automatic_station_dispatches_when_ready_and_rejects_manual_dispatch()
    {
        Order order = CreateOrder(lines: [Draft(requiresPrimaryDispatch: false)]);
        OrderLine line = Assert.Single(order.Lines);
        order.Submit(Timestamp);
        order.StartLinePreparation(line.Id, Timestamp.AddMinutes(1));
        order.MarkLineReady(line.Id, Timestamp.AddMinutes(2));

        Assert.Equal(KitchenLineStatus.Dispatched, line.KitchenStatus);
        Assert.Equal(KitchenTicketStatus.Dispatched, Assert.Single(order.Stations).Status);
        Assert.Equal(OrderStatus.Ready, order.Status);
        Assert.Throws<InvalidOperationException>(() =>
            order.DispatchLine(line.Id, Timestamp.AddMinutes(3))
        );
    }

    [Fact]
    public void Submit_rejects_repeated_submission_and_keeps_utc_timestamps()
    {
        Order order = CreateOrder(lines: [Draft(requiresPrimaryDispatch: true)]);

        order.Submit(Timestamp);

        Assert.Equal(DateTimeKind.Utc, order.SubmittedAtUtc?.Kind);
        Assert.Throws<InvalidOperationException>(() => order.Submit(Timestamp.AddMinutes(1)));
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
    public void Cancel_line_rejects_short_reasons_unknown_lines_and_terminal_orders()
    {
        Order order = CreateOrder(lines: [Draft("COCINA"), Draft("BEBIDAS")]);
        OrderLine line = order.Lines.First();

        Assert.Throws<ArgumentException>(() => order.CancelLine(line.Id, "No", Timestamp));
        Assert.Throws<InvalidOperationException>(() =>
            order.CancelLine(Guid.NewGuid(), "Sin stock", Timestamp)
        );

        Order cancelled = CreateOrder(lines: [Draft("COCINA")]);
        cancelled.Cancel("Pedido duplicado", Timestamp);
        Assert.Throws<InvalidOperationException>(() =>
            cancelled.CancelLine(cancelled.Lines.Single().Id, "Sin stock", Timestamp)
        );

        Order delivered = CreateOrder(lines: [Draft() with { RequiresKds = false }]);
        delivered.Submit(Timestamp);
        Assert.Throws<InvalidOperationException>(() =>
            delivered.CancelLine(delivered.Lines.Single().Id, "Sin stock", Timestamp)
        );
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

    [Fact]
    public void Order_without_kds_is_delivered_on_submission()
    {
        Order order = CreateOrder(lines: [Draft() with { RequiresKds = false }]);
        OrderLine line = Assert.Single(order.Lines);

        order.Submit(Timestamp);

        Assert.Empty(order.Stations);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(KitchenLineStatus.Dispatched, line.KitchenStatus);
        Assert.Equal(Timestamp, order.ReadyAtUtc);
        Assert.Equal(Timestamp, order.DeliveredAtUtc);
        Assert.Throws<InvalidOperationException>(() => order.Deliver(Timestamp));
    }

    [Fact]
    public void Direct_lines_are_fulfilled_while_kds_lines_remain_in_preparation()
    {
        Order order = CreateOrder(lines: [Draft() with { RequiresKds = false }, Draft("COCINA")]);
        OrderLine direct = order.Lines.First();
        OrderLine kitchen = order.Lines.Last();

        order.Submit(Timestamp);
        Assert.Equal(KitchenLineStatus.Dispatched, direct.KitchenStatus);
        Assert.Equal(KitchenLineStatus.Pending, kitchen.KitchenStatus);
        Assert.Equal(OrderStatus.Submitted, order.Status);

        order.StartLinePreparation(kitchen.Id, Timestamp.AddMinutes(1));
        order.MarkLineReady(kitchen.Id, Timestamp.AddMinutes(2));

        Assert.Equal(OrderStatus.Ready, order.Status);
        Assert.Equal(KitchenTicketStatus.Dispatched, Assert.Single(order.Stations).Status);
    }

    [Fact]
    public void Cancelling_an_earlier_station_allows_the_next_primary_station_to_dispatch()
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
        order.CancelLine(lines[0].Id, "Sin stock", Timestamp.AddMinutes(1));
        order.StartLinePreparation(lines[1].Id, Timestamp.AddMinutes(2));
        order.MarkLineReady(lines[1].Id, Timestamp.AddMinutes(3));
        order.DispatchLine(lines[1].Id, Timestamp.AddMinutes(4));

        Assert.Equal(OrderStatus.Ready, order.Status);
        Assert.Equal(
            KitchenTicketStatus.Cancelled,
            order.Stations.Single(s => s.Code == "COCINA").Status
        );
        Assert.Equal(
            KitchenTicketStatus.Dispatched,
            order.Stations.Single(s => s.Code == "BEBIDAS").Status
        );
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
