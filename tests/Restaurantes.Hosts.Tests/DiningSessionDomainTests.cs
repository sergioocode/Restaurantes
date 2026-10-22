using Restaurantes.Dining.Domain;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class DiningSessionDomainTests
{
    [Fact]
    public void Checkout_uses_open_status_discount_and_non_cancelled_unpaid_orders()
    {
        DiningSession session = new()
        {
            Status = "Open",
            DiscountPercentage = 12.5m,
            Orders =
            [
                new()
                {
                    Amount = 10.05m,
                    AmountPaid = 0m,
                    OrderStatus = "Submitted",
                },
                new()
                {
                    Amount = 20m,
                    AmountPaid = 20m,
                    OrderStatus = "Delivered",
                },
                new()
                {
                    Amount = 30m,
                    AmountPaid = 0m,
                    OrderStatus = "Cancelled",
                },
            ],
        };

        Assert.Equal(8.79m, session.ChargeableAmount(session.Orders[0]));
        Assert.Equal(17.5m, session.ChargeableAmount(session.Orders[1]));
        Assert.True(session.CanCheckout);

        session.Orders[0].AmountPaid = session.ChargeableAmount(session.Orders[0]);
        Assert.False(session.CanCheckout);
        session.Status = "Closed";
        session.Orders[0].AmountPaid = 0m;
        Assert.False(session.CanCheckout);
    }

    [Fact]
    public void Release_requires_orders_delivered_or_cancelled_and_fully_paid()
    {
        DiningSession session = new() { Status = "Open" };

        Assert.False(session.CanRelease);
        session.Orders.Add(
            new()
            {
                Amount = 10m,
                AmountPaid = 10m,
                OrderStatus = "Submitted",
            }
        );
        Assert.False(session.CanRelease);

        session.Orders[0].OrderStatus = "Delivered";
        Assert.True(session.CanRelease);

        session.Orders.Add(new() { Amount = 15m, OrderStatus = "Cancelled" });
        Assert.True(session.CanRelease);

        session.Orders[0].AmountPaid = 9m;
        Assert.False(session.CanRelease);
        session.Orders[0].AmountPaid = 10m;
        session.Status = "Closed";
        Assert.False(session.CanRelease);
    }

    [Fact]
    public void Session_mutations_record_payment_lifecycle_and_increment_version()
    {
        Guid userId = Guid.NewGuid();
        Guid paymentKey = Guid.NewGuid();
        DateTime occurredAt = new(2026, 10, 22, 14, 30, 0, DateTimeKind.Utc);
        DiningSession session = new();

        session.RegisterGuests(3);
        session.ApplyDiscount(10m);
        session.RecordPayment(paymentKey, "Card", occurredAt);
        Assert.Equal(4, session.Version);
        Assert.Equal(3, session.GuestCount);
        Assert.Equal(10m, session.DiscountPercentage);
        Assert.Equal(paymentKey, session.CheckoutIdempotencyKey);
        Assert.Equal("Card", session.PaymentMethod);
        Assert.Equal(occurredAt, session.PaidAtUtc);

        session.Close(occurredAt.AddMinutes(1));
        Assert.Equal("Closed", session.Status);
        Assert.Equal(occurredAt.AddMinutes(1), session.ClosedAtUtc);
        Assert.Equal(5, session.Version);

        session.Cancel(userId, occurredAt.AddMinutes(2));
        Assert.Equal("Cancelled", session.Status);
        Assert.Equal(userId, session.CancelledByUserId);
        Assert.Equal(occurredAt.AddMinutes(2), session.CancelledAtUtc);
        Assert.Equal(session.CancelledAtUtc, session.ClosedAtUtc);
        Assert.Equal(6, session.Version);
    }
}
