using Restaurantes.Orders.Domain;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrderTests
{
    private static readonly DateTime OccurredAtUtc = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MarkStationReady_AutomaticallyDispatchesNonPrimaryStationAndMarksOrderReady()
    {
        Order order = CreateDineInOrder(
            CreateDraft("GRILL", requiresPrimaryDispatch: false, priority: 1)
        );

        order.Submit(OccurredAtUtc);
        order.StartStationPreparation("GRILL", OccurredAtUtc.AddMinutes(1));
        order.MarkStationReady("GRILL", OccurredAtUtc.AddMinutes(5));

        OrderKitchenStation station = Assert.Single(order.Stations);
        Assert.Equal(KitchenTicketStatus.Dispatched, station.Status);
        Assert.Equal(OrderStatus.Ready, order.Status);
        Assert.Equal(OccurredAtUtc.AddMinutes(5), order.ReadyAtUtc);
    }

    [Fact]
    public void DispatchStation_RequiresPrimaryStationsToFollowPriority()
    {
        Order order = CreateDineInOrder(
            CreateDraft("GRILL", requiresPrimaryDispatch: true, priority: 1),
            CreateDraft("BAR", requiresPrimaryDispatch: true, priority: 2)
        );

        order.Submit(OccurredAtUtc);
        order.StartStationPreparation("GRILL", OccurredAtUtc.AddMinutes(1));
        order.MarkStationReady("GRILL", OccurredAtUtc.AddMinutes(2));
        order.StartStationPreparation("BAR", OccurredAtUtc.AddMinutes(3));
        order.MarkStationReady("BAR", OccurredAtUtc.AddMinutes(4));

        Assert.Throws<InvalidOperationException>(() =>
            order.DispatchStation("BAR", OccurredAtUtc.AddMinutes(5))
        );

        order.DispatchStation("GRILL", OccurredAtUtc.AddMinutes(5));
        order.DispatchStation("BAR", OccurredAtUtc.AddMinutes(6));

        Assert.Equal(OrderStatus.Ready, order.Status);
        Assert.All(
            order.Stations,
            station => Assert.Equal(KitchenTicketStatus.Dispatched, station.Status)
        );
    }

    [Fact]
    public void Create_RejectsTakeawayOrderWithPhysicalLocation()
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                guestCount: null,
                Guid.NewGuid(),
                "Mesa 4",
                "María",
                ServiceMode.Takeaway,
                OrderSource.Pos,
                PaymentTiming.Immediate,
                [CreateDraft("GRILL", requiresPrimaryDispatch: false, priority: 1)],
                OccurredAtUtc
            )
        );
    }

    [Fact]
    public void CancelLine_CancelsOrderWhenItsOnlyActiveLineIsCancelled()
    {
        Order order = CreateDineInOrder(
            CreateDraft("GRILL", requiresPrimaryDispatch: false, priority: 1)
        );
        Guid lineId = Assert.Single(order.Lines).Id;

        order.CancelLine(lineId, "Producto no disponible", OccurredAtUtc.AddMinutes(1));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(KitchenTicketStatus.Cancelled, Assert.Single(order.Stations).Status);
    }

    private static Order CreateDineInOrder(params OrderLineDraft[] drafts)
    {
        return Order.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            guestCount: 2,
            Guid.NewGuid(),
            "Mesa 4",
            string.Empty,
            ServiceMode.DineIn,
            OrderSource.Pos,
            PaymentTiming.OnAccount,
            drafts,
            OccurredAtUtc
        );
    }

    private static OrderLineDraft CreateDraft(
        string stationCode,
        bool requiresPrimaryDispatch,
        int priority
    )
    {
        return new OrderLineDraft(
            Guid.NewGuid(),
            "Hamburguesa",
            12.50m,
            1,
            string.Empty,
            Guid.NewGuid(),
            "Principales",
            stationCode,
            stationCode,
            requiresPrimaryDispatch,
            priority
        );
    }
}
