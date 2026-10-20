using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Infrastructure.Workers;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Payments.Contracts.Events;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashPaymentProjectionWorkerCoverageTests
{
    [Fact]
    public void Captured_payment_maps_totals_references_and_sale_group()
    {
        Guid messageId = Guid.NewGuid();
        Guid paymentId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid restaurantId = Guid.NewGuid();
        Guid registerSessionId = Guid.NewGuid();
        Guid saleGroupId = Guid.NewGuid();
        DateTime capturedAt = new(2026, 10, 20, 13, 45, 0, DateTimeKind.Utc);
        PaymentCaptured payment = new(
            paymentId,
            orderId,
            restaurantId,
            18m,
            "Cash",
            "DineIn",
            "Pos",
            "REF-123",
            capturedAt,
            CashRegisterSessionId: registerSessionId,
            TipAmount: 2m,
            TenderedAmount: 25m,
            ChangeAmount: 5m,
            SaleGroupId: saleGroupId
        );
        BasicDeliverEventArgs args = Delivery(
            messageId.ToString(),
            typeof(PaymentCaptured).FullName!,
            JsonSerializer.SerializeToUtf8Bytes(payment)
        );

        PaymentProjection projection = InvokeReadPayment(messageId, args);

        Assert.Equal(messageId, projection.MessageId);
        Assert.Equal(paymentId, projection.PaymentId);
        Assert.Equal(orderId, projection.OrderId);
        Assert.Equal(restaurantId, projection.RestaurantId);
        Assert.Equal(20m, projection.Amount);
        Assert.Equal("Cash", projection.Method);
        Assert.Equal("REF-123", projection.Reference);
        Assert.Equal(capturedAt, projection.OccurredAtUtc);
        Assert.Equal(registerSessionId, projection.CashRegisterSessionId);
        Assert.Equal(25m, projection.TenderedAmount);
        Assert.Equal(5m, projection.ChangeAmount);
        Assert.Equal(saleGroupId, projection.SaleId);
    }

    [Fact]
    public void Captured_payment_uses_transaction_id_when_sale_group_is_absent()
    {
        Guid transactionId = Guid.NewGuid();
        PaymentCaptured payment = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            7m,
            "Card",
            "DineIn",
            "Pos",
            "",
            DateTime.UtcNow,
            PaymentTransactionId: transactionId
        );

        PaymentProjection projection = InvokeReadPayment(
            Guid.NewGuid(),
            Delivery(
                Guid.NewGuid().ToString(),
                typeof(PaymentCaptured).FullName!,
                JsonSerializer.SerializeToUtf8Bytes(payment)
            )
        );

        Assert.Equal(transactionId, projection.SaleId);
        Assert.Equal(7m, projection.Amount);
    }

    [Theory]
    [InlineData("invalid-id", "Restaurantes.Payments.Contracts.Events.PaymentCaptured", "{}")]
    [InlineData("{guid}", "unknown.event", "{}")]
    [InlineData("{guid}", "Restaurantes.Payments.Contracts.Events.PaymentCaptured", "null")]
    public async Task Invalid_payment_deliveries_are_dead_lettered(
        string messageId,
        string eventType,
        string body
    )
    {
        IChannel channel = DispatchProxy.Create<IChannel, ChannelProxy>();
        ChannelProxy proxy = (ChannelProxy)(object)channel;
        CashPaymentProjectionWorker worker = new(
            null!,
            Options.Create(new RabbitMqOptions()),
            NullLogger<CashPaymentProjectionWorker>.Instance
        );
        typeof(CashPaymentProjectionWorker)
            .GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(worker, channel);
        BasicDeliverEventArgs args = Delivery(
            messageId == "{guid}" ? Guid.NewGuid().ToString() : messageId,
            eventType,
            System.Text.Encoding.UTF8.GetBytes(body)
        );
        MethodInfo handle = typeof(CashPaymentProjectionWorker).GetMethod(
            "Handle",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

        await (Task)handle.Invoke(worker, [channel, args])!;

        Assert.Equal(0, proxy.Acknowledgements);
        Assert.Equal(1, proxy.Rejections);
    }

    private static PaymentProjection InvokeReadPayment(
        Guid messageId,
        BasicDeliverEventArgs args
    ) =>
        (PaymentProjection)
            typeof(CashPaymentProjectionWorker)
                .GetMethod("ReadPayment", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [messageId, args])!;

    private static BasicDeliverEventArgs Delivery(
        string messageId,
        string eventType,
        byte[] body
    ) =>
        new(
            "consumer",
            1,
            false,
            "exchange",
            "routing-key",
            new BasicProperties { MessageId = messageId, Type = eventType },
            body,
            TestContext.Current.CancellationToken
        );

    public class ChannelProxy : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public int Rejections { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IChannel.BasicAckAsync))
            {
                Acknowledgements++;
                return ValueTask.CompletedTask;
            }
            if (targetMethod?.Name == nameof(IChannel.BasicNackAsync))
            {
                Rejections++;
                return ValueTask.CompletedTask;
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
