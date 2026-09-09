using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.Payments.Contracts.Events;

namespace Restaurantes.Orders.Consumer;

public sealed class PaymentProjectionWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    ILogger<PaymentProjectionWorker> log
) : BackgroundService
{
    private IConnection? connection;
    private IChannel? channel;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                ConnectionFactory f = RabbitMqConnectionFactory.Create(options.Value);
                connection = await f.CreateConnectionAsync("orders-payment-projection", ct);
                channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await PaymentsTopology.DeclareAsync(channel, ct);
                AsyncEventingBasicConsumer consumer = new(channel);
                consumer.ReceivedAsync += Handle;
                await channel.BasicConsumeAsync(
                    PaymentsTopology.OrdersIntegrationQueueName,
                    false,
                    consumer,
                    ct
                );
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                log.LogError(e, "Orders payment projection disconnected; retrying.");
                await DisposeBroker();
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task Handle(object sender, BasicDeliverEventArgs ea)
    {
        if (channel is null)
        {
            return;
        }

        try
        {
            Guid orderId,
                paymentId;
            decimal amount;
            string status;
            DateTime occurred;
            if (ea.BasicProperties.Type == typeof(PaymentCaptured).FullName)
            {
                PaymentCaptured e =
                    JsonSerializer.Deserialize<PaymentCaptured>(ea.Body.Span)
                    ?? throw new JsonException("Empty payment");
                orderId = e.OrderId;
                paymentId = e.PaymentId;
                amount = e.Amount;
                status = "Paid";
                occurred = e.CapturedAtUtc;
            }
            else if (ea.BasicProperties.Type == typeof(PaymentRefunded).FullName)
            {
                PaymentRefunded e =
                    JsonSerializer.Deserialize<PaymentRefunded>(ea.Body.Span)
                    ?? throw new JsonException("Empty refund");
                orderId = e.OrderId;
                paymentId = e.PaymentId;
                amount = e.Amount;
                status = "Refunded";
                occurred = e.RefundedAtUtc;
            }
            else
            {
                throw new NotSupportedException(ea.BasicProperties.Type);
            }

            await using AsyncServiceScope s = scopes.CreateAsyncScope();
            OrderWriteDbContext db = s.ServiceProvider.GetRequiredService<OrderWriteDbContext>();
            OrderPaymentProjection? x = await db.OrderPayments.FindAsync([orderId]);
            if (x is null)
            {
                db.OrderPayments.Add(
                    new OrderPaymentProjection
                    {
                        OrderId = orderId,
                        PaymentId = paymentId,
                        Amount = amount,
                        Status = status,
                        UpdatedAtUtc = occurred,
                    }
                );
            }
            else
            {
                x.PaymentId = paymentId;
                x.Amount = amount;
                x.Status = status;
                x.UpdatedAtUtc = occurred;
            }
            await db.SaveChangesAsync();
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            log.LogError(e, "Invalid payment integration event");
            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (Exception e)
        {
            log.LogError(e, "Payment integration event will retry");
            await channel.BasicNackAsync(ea.DeliveryTag, false, true);
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        await DisposeBroker();
    }

    private async Task DisposeBroker()
    {
        if (channel is not null)
        {
            await channel.DisposeAsync();
            channel = null;
        }
        if (connection is not null)
        {
            await connection.DisposeAsync();
            connection = null;
        }
    }
}
