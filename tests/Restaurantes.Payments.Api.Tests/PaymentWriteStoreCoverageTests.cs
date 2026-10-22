using Microsoft.EntityFrameworkCore;
using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Payments.Domain;
using Restaurantes.Payments.Infrastructure.Persistence;
using Restaurantes.Payments.Infrastructure.Persistence.Write;
using Restaurantes.Payments.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Payments.Api.Tests;

public sealed class PaymentWriteStoreCoverageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Save_and_lookups_find_order_and_capture_by_their_keys()
    {
        await using PaymentWriteDbContext db = CreateDatabase();
        PaymentWriteStore store = new(db);
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid idempotencyKey = Guid.NewGuid();
        DateTime createdAt = DateTime.UtcNow;
        PayableOrder order = PayableOrder.Create(
            orderId,
            restaurantId,
            null,
            null,
            "TakeAway",
            "Pos",
            12m,
            createdAt
        );
        db.PayableOrders.Add(order);
        PayableOrderCapture capture = order.Capture(
            5m,
            1m,
            " Cash ",
            " REF-1 ",
            idempotencyKey,
            createdAt.AddMinutes(1)
        );
        await store.SaveAsync(Ct);

        Assert.Equal(orderId, (await store.FindAsync(orderId, Ct))?.OrderId);
        Assert.Equal(orderId, (await store.FindCaptureAsync(orderId, idempotencyKey, Ct))?.OrderId);
        Assert.Null(await store.FindAsync(Guid.NewGuid(), Ct));
        Assert.Null(await store.FindCaptureAsync(orderId, Guid.NewGuid(), Ct));
        Assert.Equal(
            capture.PaymentId,
            (await store.FindCaptureAsync(orderId, idempotencyKey, Ct))?.PaymentId
        );
    }

    [Fact]
    public async Task Save_with_capture_event_persists_capture_and_serialized_outbox_message()
    {
        await using PaymentWriteDbContext db = CreateDatabase();
        PaymentWriteStore store = new(db);
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        DateTime capturedAt = DateTime.UtcNow;
        PayableOrder order = PayableOrder.Create(
            orderId,
            restaurantId,
            null,
            null,
            "DineIn",
            "Pos",
            20m,
            capturedAt.AddMinutes(-1)
        );
        db.PayableOrders.Add(order);
        PayableOrderCapture capture = order.Capture(
            20m,
            2m,
            "Card",
            "POS-42",
            Guid.NewGuid(),
            capturedAt
        );
        PaymentCaptured integrationEvent = new(
            capture.PaymentId,
            orderId,
            restaurantId,
            20m,
            "Card",
            "DineIn",
            "Pos",
            "POS-42",
            capturedAt,
            AmountPaid: 20m,
            TipAmountPaid: 2m
        );

        await store.SaveWithEventAsync(order, capture, integrationEvent, Ct);

        Assert.Equal(1, await db.Captures.CountAsync(Ct));
        OutboxMessage message = await db.OutboxMessages.SingleAsync(Ct);
        Assert.Equal(typeof(PaymentCaptured).FullName, message.Type);
        Assert.Equal(capturedAt, message.OccurredAtUtc);
        Assert.Contains("POS-42", message.Payload);
    }

    [Fact]
    public async Task Refund_outbox_uses_refund_timestamp_for_both_store_overloads()
    {
        await using PaymentWriteDbContext db = CreateDatabase();
        PaymentWriteStore store = new(db);
        Guid orderId = Guid.NewGuid();
        DateTime refundedAt = DateTime.UtcNow;
        PaymentRefunded integrationEvent = new(
            Guid.NewGuid(),
            orderId,
            Guid.NewGuid(),
            8m,
            "Card",
            "TakeAway",
            "Pos",
            "Duplicate charge",
            refundedAt
        );
        PayableOrder order = PayableOrder.Create(
            orderId,
            integrationEvent.RestaurantId,
            null,
            null,
            "TakeAway",
            "Pos",
            8m,
            refundedAt.AddHours(-1)
        );
        PayableOrderCapture capture = new()
        {
            PaymentId = integrationEvent.PaymentId,
            OrderId = orderId,
            IdempotencyKey = Guid.NewGuid(),
            Amount = 8m,
            Method = "Card",
            CapturedAtUtc = refundedAt.AddHours(-1),
        };
        db.PayableOrders.Add(order);

        await store.SaveWithEventAsync(order, capture, integrationEvent, Ct);
        await store.SaveWithEventAsync(order, integrationEvent, Ct);

        OutboxMessage[] messages = await db
            .OutboxMessages.OrderBy(item => item.Id)
            .ToArrayAsync(Ct);
        Assert.Equal(2, messages.Length);
        Assert.All(
            messages,
            message =>
            {
                Assert.Equal(typeof(PaymentRefunded).FullName, message.Type);
                Assert.Equal(refundedAt, message.OccurredAtUtc);
                Assert.Contains("Duplicate charge", message.Payload);
            }
        );
        Assert.Equal(1, await db.Captures.CountAsync(Ct));
    }

    private static PaymentWriteDbContext CreateDatabase() =>
        new(
            new DbContextOptionsBuilder<PaymentWriteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
}
