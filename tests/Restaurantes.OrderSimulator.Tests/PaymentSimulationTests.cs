using Restaurantes.OrderSimulator;
using Xunit;
using Simulator = Restaurantes.OrderSimulator.Program;

namespace Restaurantes.OrderSimulator.Tests;

public sealed class PaymentSimulationTests
{
    [Fact]
    public void Session_is_not_ready_to_close_when_any_active_order_is_unpaid()
    {
        BillResponse bill = new(
            Guid.NewGuid(),
            20m,
            2m,
            0m,
            true,
            [
                new BillOrderInfo(
                    Guid.NewGuid(),
                    "Delivered",
                    "Paid",
                    10m,
                    10m,
                    1m,
                    DateTime.UtcNow
                ),
                new BillOrderInfo(
                    Guid.NewGuid(),
                    "Delivered",
                    "PartiallyPaid",
                    10m,
                    9m,
                    1m,
                    DateTime.UtcNow
                ),
            ]
        );

        Assert.False(Simulator.IsReadyToClose(bill));
    }

    [Fact]
    public void Session_is_ready_to_close_when_every_active_order_is_paid()
    {
        BillResponse bill = new(
            Guid.NewGuid(),
            20m,
            2m,
            0m,
            true,
            [
                new BillOrderInfo(
                    Guid.NewGuid(),
                    "Delivered",
                    "Paid",
                    10m,
                    10m,
                    1m,
                    DateTime.UtcNow
                ),
                new BillOrderInfo(
                    Guid.NewGuid(),
                    "Cancelled",
                    "Unpaid",
                    10m,
                    0m,
                    0m,
                    DateTime.UtcNow
                ),
            ]
        );

        Assert.True(Simulator.IsReadyToClose(bill));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(42)]
    public void Draws_stay_within_the_requested_ranges(int? seed)
    {
        Simulator.SimulatorRandom random = new(seed);

        for (int index = 0; index < 100; index++)
        {
            Assert.InRange(random.NextInteger(-5, 7), -5, 6);
            double fraction = random.NextFraction();
            Assert.True(fraction >= 0 && fraction < 1);
        }
        Assert.Equal(3, random.NextInteger(3, 3));
        Assert.Equal(5, random.NextInteger(5, 4));
    }

    [Fact]
    public void The_same_seed_reproduces_a_mixed_sequence_of_draws()
    {
        Simulator.SimulatorRandom first = new(123);
        Simulator.SimulatorRandom second = new(123);

        for (int index = 0; index < 100; index++)
        {
            Assert.Equal(first.NextInteger(0, 1000), second.NextInteger(0, 1000));
            Assert.Equal(first.NextFraction(), second.NextFraction());
        }
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(1.00)]
    [InlineData(29.99)]
    public void Split_payments_preserve_the_total_without_zero_or_repeated_parts(decimal amount)
    {
        PaymentOptions options = new() { SplitProbability = 1 };
        using SemaphoreSlim gate = new(1, 1);
        Simulator.SimulatorRandom random = new(42);

        for (int index = 0; index < 100; index++)
        {
            List<PaymentPart> parts = Simulator.CreatePaymentParts(options, amount, random, gate);

            Assert.Equal(amount, parts.Sum(part => part.Amount));
            Assert.Equal(parts.Count, parts.Select(part => part.Method).Distinct().Count());
            Assert.All(
                parts,
                part =>
                {
                    Assert.True(part.Amount > 0);
                    Assert.Equal(decimal.Round(part.Amount, 2), part.Amount);
                    Assert.Contains(options.Methods, method => method.Name == part.Method);
                }
            );
            Assert.Equal(1, gate.CurrentCount);
        }
    }

    [Fact]
    public void Payments_without_splitting_use_one_configured_method()
    {
        PaymentOptions options = new() { SplitProbability = 0 };

        PaymentPart part = Assert.Single(
            Simulator.CreatePaymentParts(options, 18.35m, new Simulator.SimulatorRandom(null))
        );

        Assert.Equal(18.35m, part.Amount);
        Assert.Contains(options.Methods, method => method.Name == part.Method);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.001)]
    public void Invalid_payment_amounts_are_rejected(decimal amount)
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Simulator.CreatePaymentParts(
                new PaymentOptions(),
                amount,
                new Simulator.SimulatorRandom(1)
            )
        );

        Assert.Equal("amount", error.ParamName);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Guest_payments_cover_the_full_amount_with_three_methods(int guestCount)
    {
        using SemaphoreSlim randomLock = new(1, 1);

        List<PaymentPart> parts = Simulator.CreateGuestPaymentParts(
            new PaymentOptions(),
            14.50m,
            guestCount,
            new Simulator.SimulatorRandom(17),
            randomLock
        );

        Assert.Equal(14.50m, parts.Sum(part => part.Amount));
        Assert.Equal(guestCount, parts.Select(part => part.GuestNumber).Distinct().Count());
        Assert.Equal(3, parts.Select(part => part.Method).Distinct().Count());
        Assert.All(parts, part => Assert.True(part.Amount > 0));
    }

    [Fact]
    public void Guest_payments_reject_an_amount_too_small_to_split()
    {
        using SemaphoreSlim randomLock = new(1, 1);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            Simulator.CreateGuestPaymentParts(
                new PaymentOptions(),
                0.01m,
                2,
                new Simulator.SimulatorRandom(17),
                randomLock
            )
        );

        Assert.Contains("demasiado bajo", error.Message);
    }

    [Fact]
    public void Weighted_allocation_preserves_the_total_and_rejects_invalid_weights()
    {
        List<decimal> shares = Simulator.Allocate(10.01m, [1m, 1m, 1m]);

        Assert.Equal([3.34m, 3.34m, 3.33m], shares);
        Assert.Equal(10.01m, shares.Sum());
        Assert.Equal(
            "weights",
            Assert.Throws<ArgumentException>(() => Simulator.Allocate(1m, [])).ParamName
        );
        Assert.Equal(
            "weights",
            Assert.Throws<ArgumentException>(() => Simulator.Allocate(1m, [0m])).ParamName
        );
    }

    [Fact]
    public void Tip_allocation_never_creates_negative_payment_tips_for_small_splits()
    {
        List<decimal> tips = Simulator.AllocateMoney(0.25m, Enumerable.Repeat(0.25m, 10).ToList());

        Assert.Equal(0.25m, tips.Sum());
        Assert.All(tips, tip => Assert.True(tip >= 0));
        Assert.Equal(5, tips.Count(tip => tip == 0.03m));
        Assert.Equal(5, tips.Count(tip => tip == 0.02m));
    }
}
