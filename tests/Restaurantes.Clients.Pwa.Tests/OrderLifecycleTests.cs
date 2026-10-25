using Restaurantes.Orders.Domain;
using Xunit;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class OrderLifecycleTests
{
    [Fact]
    public void Customer_qr_orders_cannot_be_created_on_account()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateOrder(OrderSource.CustomerQr, ServiceMode.DineIn, PaymentTiming.OnAccount)
        );
    }

    [Fact]
    public void Takeaway_pos_order_uses_a_virtual_session_without_a_physical_table()
    {
        Order order = CreateOrder(OrderSource.Pos, ServiceMode.Takeaway, PaymentTiming.OnAccount);

        Assert.Equal(PaymentTiming.OnAccount, order.PaymentTiming);
        Assert.Equal(ServiceMode.Takeaway, order.ServiceMode);
        Assert.Null(order.TableId);
        Assert.NotNull(order.DiningSessionId);
        Assert.Equal("PARA LLEVAR", order.TableLabel);
    }

    [Fact]
    public void Takeaway_order_rejects_a_physical_table()
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                Guid.NewGuid(),
                "Mesa 01",
                "Cliente",
                ServiceMode.Takeaway,
                OrderSource.Pos,
                PaymentTiming.OnAccount,
                [
                    new OrderLineDraft(
                        Guid.NewGuid(),
                        "Agua mineral",
                        2.20m,
                        1,
                        string.Empty,
                        null,
                        "Bebidas",
                        "BEVERAGES",
                        "Bebidas",
                        false,
                        1
                    ),
                ],
                DateTime.UtcNow
            )
        );
    }

    [Fact]
    public void Whole_order_can_be_cancelled_before_any_station_starts()
    {
        Order order = CreateOrder(OrderSource.Pos, ServiceMode.Takeaway, PaymentTiming.OnAccount);

        order.Submit(DateTime.UtcNow);
        order.Cancel("Cliente cambió de idea", DateTime.UtcNow);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.All(
            order.Stations,
            station => Assert.Equal(KitchenTicketStatus.Cancelled, station.Status)
        );
    }

    [Fact]
    public void Whole_order_cannot_be_cancelled_after_station_preparation_starts()
    {
        Order order = CreateOrder(OrderSource.Pos, ServiceMode.Takeaway, PaymentTiming.OnAccount);
        order.Submit(DateTime.UtcNow);
        order.StartLinePreparation(order.Lines.Single().Id, DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            order.Cancel("Motivo válido", DateTime.UtcNow)
        );
    }

    private static Order CreateOrder(
        OrderSource source,
        ServiceMode serviceMode,
        PaymentTiming paymentTiming
    )
    {
        return Order.Create(
            Guid.NewGuid(),
            serviceMode == ServiceMode.Takeaway ? null : Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            serviceMode == ServiceMode.Takeaway ? "PARA LLEVAR" : "Ubicación de prueba",
            serviceMode == ServiceMode.Takeaway ? "Cliente" : string.Empty,
            serviceMode,
            source,
            paymentTiming,
            [
                new OrderLineDraft(
                    Guid.NewGuid(),
                    "Agua mineral",
                    2.20m,
                    1,
                    string.Empty,
                    null,
                    "Bebidas",
                    "BEVERAGES",
                    "Bebidas",
                    false,
                    1
                ),
            ],
            DateTime.UtcNow
        );
    }
}
