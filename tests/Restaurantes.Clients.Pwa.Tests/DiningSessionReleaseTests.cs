using Restaurantes.Dining.Domain;
using Xunit;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class DiningSessionReleaseTests
{
    [Theory]
    [InlineData("Submitted", "Paid", false)]
    [InlineData("Ready", "Paid", false)]
    [InlineData("Delivered", "Unpaid", false)]
    [InlineData("Delivered", "Paid", true)]
    [InlineData("Cancelled", "Unpaid", true)]
    public void Release_requires_paid_and_kitchen_finished_orders(
        string orderStatus,
        string paymentStatus,
        bool expected
    )
    {
        DiningSession session = new()
        {
            Status = "Open",
            Orders =
            [
                new DiningSessionOrder
                {
                    OrderStatus = orderStatus,
                    PaymentStatus = paymentStatus,
                    Amount = orderStatus == "Cancelled" ? 0 : 10m,
                    AmountPaid = paymentStatus == "Paid" ? 10m : 0,
                },
            ],
        };

        Assert.Equal(expected, session.CanRelease);
    }

    [Fact]
    public void Empty_or_closed_sessions_cannot_be_released_from_the_bill()
    {
        DiningSession empty = new() { Status = "Open" };
        DiningSession closed = new()
        {
            Status = "Closed",
            Orders = [new DiningSessionOrder { OrderStatus = "Delivered", PaymentStatus = "Paid" }],
        };

        Assert.False(empty.CanRelease);
        Assert.False(closed.CanRelease);
    }

    [Fact]
    public void Recording_payment_keeps_the_session_open_until_release()
    {
        DiningSession session = new()
        {
            Status = "Open",
            Orders = [new DiningSessionOrder { OrderStatus = "Submitted", PaymentStatus = "Paid" }],
        };

        session.RecordPayment(Guid.NewGuid(), "Card", DateTime.UtcNow);

        Assert.Equal("Open", session.Status);
        Assert.False(session.CanRelease);
    }

    [Fact]
    public void Partially_paid_order_cannot_release_a_delivered_table()
    {
        DiningSession session = new()
        {
            Status = "Open",
            Orders =
            [
                new DiningSessionOrder
                {
                    OrderStatus = "Delivered",
                    PaymentStatus = "PartiallyPaid",
                    Amount = 20m,
                    AmountPaid = 10m,
                },
            ],
        };

        Assert.False(session.CanRelease);
    }

    [Fact]
    public void Discounted_order_can_release_when_the_discounted_amount_is_paid()
    {
        DiningSession session = new()
        {
            Status = "Open",
            DiscountPercentage = 10m,
            Orders =
            [
                new DiningSessionOrder
                {
                    OrderStatus = "Delivered",
                    PaymentStatus = "PartiallyPaid",
                    Amount = 7.50m,
                    AmountPaid = 6.75m,
                },
            ],
        };

        Assert.Equal(6.75m, session.ChargeableAmount(session.Orders[0]));
        Assert.True(session.CanRelease);
    }
}
