using Restaurantes.Payments.Application;
using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Payments.Contracts.Requests;
using Restaurantes.Payments.Domain;
using Xunit;

namespace Restaurantes.Payments.Api.Tests;

public sealed class PaymentCommandServiceCoverageTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AdjustAmount_returns_false_for_missing_order_and_does_not_save()
    {
        PaymentStore store = new();
        PaymentCommandService service = CreateService(store);

        bool found = await service.AdjustAmountAsync(Guid.NewGuid(), 5m, CancellationToken.None);

        Assert.False(found);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task AdjustAmount_skips_save_when_amount_is_unchanged_and_saves_a_discount()
    {
        PayableOrder order = CreateOrder(20m);
        PaymentStore store = new(order);
        PaymentCommandService service = CreateService(store);

        Assert.True(await service.AdjustAmountAsync(order.OrderId, 20m, CancellationToken.None));
        Assert.Equal(0, store.SaveCount);

        Assert.True(await service.AdjustAmountAsync(order.OrderId, 15m, CancellationToken.None));

        Assert.Equal(15m, order.Amount);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task AdjustAmount_rejects_nonpositive_amount_and_orders_with_payments()
    {
        PayableOrder unpaid = CreateOrder(20m);
        PaymentCommandService unpaidService = CreateService(new PaymentStore(unpaid));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unpaidService.AdjustAmountAsync(unpaid.OrderId, 0m, CancellationToken.None)
        );

        PayableOrder partiallyPaid = CreateOrder(20m);
        partiallyPaid.Capture(5m, 0m, "Card", "", Guid.NewGuid(), Now);
        PaymentCommandService partiallyPaidService = CreateService(new PaymentStore(partiallyPaid));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            partiallyPaidService.AdjustAmountAsync(
                partiallyPaid.OrderId,
                10m,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task Capture_returns_null_for_missing_order()
    {
        PaymentCommandService service = CreateService(new PaymentStore());

        var result = await service.CaptureAsync(
            Guid.NewGuid(),
            new CapturePaymentRequest { IdempotencyKey = Guid.NewGuid(), Method = "Card" },
            CashRegister,
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task Capture_rejects_insufficient_tender_before_saving()
    {
        PayableOrder order = CreateOrder(20m);
        PaymentStore store = new(order);
        PaymentCommandService service = CreateService(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CaptureAsync(
                order.OrderId,
                new CapturePaymentRequest
                {
                    IdempotencyKey = Guid.NewGuid(),
                    Method = "Card",
                    Amount = 10m,
                    TipAmount = 2m,
                    TenderedAmount = 11m,
                },
                CashRegister,
                CancellationToken.None
            )
        );

        Assert.Equal(0, store.SaveCount);
        Assert.Empty(order.Captures);
    }

    [Fact]
    public async Task Capture_saves_event_and_returns_captured_amount_and_tip()
    {
        PayableOrder order = CreateOrder(20m);
        PaymentStore store = new(order);
        PaymentCommandService service = CreateService(store);

        var result = await service.CaptureAsync(
            order.OrderId,
            new CapturePaymentRequest
            {
                IdempotencyKey = Guid.NewGuid(),
                Method = "Card",
                Amount = 10m,
                TipAmount = 2m,
                TenderedAmount = 15m,
                ExternalReference = "  tx-1  ",
            },
            CashRegister,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal(10m, result.Amount);
        Assert.Equal(2m, result.TipAmount);
        Assert.Equal(10m, result.Outstanding);
        Assert.Equal(1, store.SaveCount);
        Assert.IsType<PaymentCaptured>(store.IntegrationEvent);
        Assert.Equal("tx-1", order.ExternalReference);
    }

    [Fact]
    public async Task Capture_reuses_previous_idempotent_capture_without_saving_again()
    {
        PayableOrder order = CreateOrder(20m);
        PayableOrderCapture capture = order.Capture(20m, 1m, "Card", "first", Guid.NewGuid(), Now);
        PaymentStore store = new(order) { ExistingCapture = capture };
        PaymentCommandService service = CreateService(store);

        var result = await service.CaptureAsync(
            order.OrderId,
            new CapturePaymentRequest { IdempotencyKey = capture.IdempotencyKey, Method = "Card" },
            CashRegister,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal(capture.PaymentId, result.PaymentId);
        Assert.Equal(1m, result.TipAmount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Refund_returns_null_for_missing_order_and_saves_refund_for_paid_order()
    {
        PaymentCommandService missingService = CreateService(new PaymentStore());
        Assert.Null(
            await missingService.RefundAsync(
                Guid.NewGuid(),
                new RefundPaymentRequest { Reason = "Customer request" },
                CancellationToken.None
            )
        );

        PayableOrder order = CreateOrder(20m);
        order.Capture(20m, 0m, "Card", "", Guid.NewGuid(), Now);
        PaymentStore store = new(order);
        PaymentCommandService service = CreateService(store);

        var result = await service.RefundAsync(
            order.OrderId,
            new RefundPaymentRequest { Reason = "Customer request" },
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal("Refunded", result.Status);
        Assert.Equal(1, store.SaveCount);
        Assert.IsType<PaymentRefunded>(store.IntegrationEvent);
    }

    private static readonly CashRegisterSessionDetails CashRegister = new(
        Guid.NewGuid(),
        new DateOnly(2026, 10, 20)
    );

    private static PayableOrder CreateOrder(decimal amount) =>
        PayableOrder.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            "DineIn",
            "Pos",
            amount,
            Now
        );

    private static PaymentCommandService CreateService(PaymentStore store) =>
        new(store, new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class PaymentStore(PayableOrder? order = null) : IPaymentWriteStore
    {
        public PayableOrderCapture? ExistingCapture { get; init; }
        public object? IntegrationEvent { get; private set; }
        public int SaveCount { get; private set; }

        public Task<PayableOrder?> FindAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(order?.OrderId == orderId ? order : null);

        public Task<PayableOrderCapture?> FindCaptureAsync(
            Guid orderId,
            Guid idempotencyKey,
            CancellationToken cancellationToken
        ) => Task.FromResult(ExistingCapture);

        public Task SaveWithEventAsync(
            PayableOrder payableOrder,
            PayableOrderCapture capture,
            object integrationEvent,
            CancellationToken cancellationToken
        )
        {
            SaveCount++;
            IntegrationEvent = integrationEvent;
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task SaveWithEventAsync(
            PayableOrder payableOrder,
            object integrationEvent,
            CancellationToken cancellationToken
        )
        {
            SaveCount++;
            IntegrationEvent = integrationEvent;
            return Task.CompletedTask;
        }
    }
}
