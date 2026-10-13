using Restaurantes.Orders.Domain;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrderKitchenLineLifecycleTests
{
    [Fact]
    public void Controlled_station_tracks_and_dispatches_each_line_independently()
    {
        DateTime submittedAt = DateTime.Parse("2026-10-09T10:00:00Z");
        Order order = CreateOrder(requiresPrimaryDispatch: true, lineCount: 2, submittedAt);
        OrderLine[] lines = order.Lines.ToArray();

        order.StartLinePreparation(lines[0].Id, submittedAt.AddMinutes(1));
        order.MarkLineReady(lines[0].Id, submittedAt.AddMinutes(4));

        Assert.Equal(KitchenLineStatus.Ready, lines[0].KitchenStatus);
        Assert.Equal(KitchenLineStatus.Pending, lines[1].KitchenStatus);
        Assert.Equal(KitchenTicketStatus.InPreparation, order.Stations.Single().Status);
        Assert.Equal(OrderStatus.InPreparation, order.Status);

        order.DispatchLine(lines[0].Id, submittedAt.AddMinutes(5));
        order.StartLinePreparation(lines[1].Id, submittedAt.AddMinutes(6));
        order.MarkLineReady(lines[1].Id, submittedAt.AddMinutes(9));
        order.DispatchLine(lines[1].Id, submittedAt.AddMinutes(10));

        Assert.Equal(KitchenTicketStatus.Dispatched, order.Stations.Single().Status);
        Assert.Equal(OrderStatus.Ready, order.Status);
        Assert.Equal(submittedAt.AddMinutes(4), lines[0].ReadyAtUtc);
        Assert.Equal(submittedAt.AddMinutes(9), lines[1].ReadyAtUtc);
    }

    [Fact]
    public void Automatic_station_dispatches_a_line_when_that_line_becomes_ready()
    {
        DateTime submittedAt = DateTime.Parse("2026-10-09T10:00:00Z");
        Order order = CreateOrder(requiresPrimaryDispatch: false, lineCount: 2, submittedAt);
        OrderLine[] lines = order.Lines.ToArray();

        order.StartLinePreparation(lines[0].Id, submittedAt.AddMinutes(1));
        order.MarkLineReady(lines[0].Id, submittedAt.AddMinutes(3));

        Assert.Equal(KitchenLineStatus.Dispatched, lines[0].KitchenStatus);
        Assert.Equal(KitchenLineStatus.Pending, lines[1].KitchenStatus);
        Assert.Equal(KitchenTicketStatus.InPreparation, order.Stations.Single().Status);
        Assert.Equal(OrderStatus.InPreparation, order.Status);
    }

    [Fact]
    public void Order_line_preserves_extra_prices_separately_from_its_total_price()
    {
        OrderLineDraft draft = new(
            Guid.NewGuid(),
            "Tagliatelle boloñesa",
            18m,
            1,
            string.Empty,
            null,
            "Pastas",
            "PASTAS",
            "Pastas",
            false,
            1,
            "PLATO",
            14m,
            [new OrderLineExtra(Guid.NewGuid(), "Alcaparras", 4m, 1)]
        );

        Order order = Order.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            2,
            Guid.NewGuid(),
            "Mesa 01",
            string.Empty,
            ServiceMode.DineIn,
            OrderSource.Pos,
            PaymentTiming.OnAccount,
            [draft],
            DateTime.Parse("2026-10-13T10:00:00Z")
        );

        OrderLine line = Assert.Single(order.Lines);
        Assert.Equal(18m, line.UnitPrice);
        Assert.Equal(14m, line.BaseUnitPrice);
        Assert.Equal("Alcaparras", Assert.Single(line.Extras).ProductName);
        Assert.Equal(4m, Assert.Single(line.Extras).UnitPrice);
    }

    private static Order CreateOrder(bool requiresPrimaryDispatch, int lineCount, DateTime now)
    {
        List<OrderLineDraft> lines = Enumerable
            .Range(1, lineCount)
            .Select(index => new OrderLineDraft(
                Guid.NewGuid(),
                $"Producto {index}",
                5m,
                1,
                string.Empty,
                null,
                "General",
                "PASTAS",
                "Pastas",
                requiresPrimaryDispatch,
                1
            ))
            .ToList();
        Order order = Order.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            "Mesa 01",
            string.Empty,
            ServiceMode.DineIn,
            OrderSource.Pos,
            PaymentTiming.OnAccount,
            lines,
            now
        );
        order.Submit(now);
        return order;
    }
}
