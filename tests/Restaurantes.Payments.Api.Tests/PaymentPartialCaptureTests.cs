using Restaurantes.Payments.Domain;
using Xunit;

namespace Restaurantes.Payments.Api.Tests;

public sealed class PaymentPartialCaptureTests
{
    [Fact]
    public void CapturesAccumulateAndRejectAmountAboveOutstandingBalance()
    {
        PayableOrder order = PayableOrder.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "DineIn",
            "Pos",
            30m,
            DateTime.UtcNow
        );

        order.Capture(10m, 1m, "Card", "CARD-1", Guid.NewGuid(), DateTime.UtcNow);

        Assert.Equal(10m, order.AmountPaid);
        Assert.Equal(1m, order.TipAmountPaid);
        Assert.Equal(20m, order.OutstandingAmount);
        Assert.Equal(PaymentStatus.Open, order.Status);

        Assert.Throws<InvalidOperationException>(() =>
            order.Capture(21m, 0m, "Cash", "CASH-1", Guid.NewGuid(), DateTime.UtcNow)
        );
    }
}
