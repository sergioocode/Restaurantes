using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.CashRegister.Application;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Payments.Contracts.Events;

namespace Restaurantes.CashRegister.Consumer;

public sealed class CashPaymentProjectionWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    ILogger<CashPaymentProjectionWorker> log
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
                connection = await RabbitMqConnectionFactory
                    .Create(options.Value)
                    .CreateConnectionAsync("cash-register", ct);
                channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await PaymentsTopology.DeclareAsync(channel, ct);
                AsyncEventingBasicConsumer consumer = new(channel);
                consumer.ReceivedAsync += Handle;
                await channel.BasicConsumeAsync(
                    PaymentsTopology.CashRegisterQueueName,
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
            catch (Exception ex)
            {
                log.LogError(ex, "CashRegister consumer disconnected; retrying.");
                await DisposeBroker();
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task Handle(object sender, BasicDeliverEventArgs ea)
    {
        if (channel is null)
            return;
        try
        {
            if (!Guid.TryParse(ea.BasicProperties.MessageId, out Guid messageId))
                throw new JsonException("Invalid payment event id.");
            PaymentProjection payment = ReadPayment(messageId, ea);
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope
                .ServiceProvider.GetRequiredService<CashPaymentProjectionService>()
                .Project(payment, CancellationToken.None);
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            log.LogError(ex, "Invalid cash-register event.");
            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Cash-register event will retry.");
            await channel.BasicNackAsync(ea.DeliveryTag, false, true);
        }
    }

    private static PaymentProjection ReadPayment(Guid messageId, BasicDeliverEventArgs ea)
    {
        if (ea.BasicProperties.Type == typeof(PaymentCaptured).FullName)
        {
            PaymentCaptured e =
                JsonSerializer.Deserialize<PaymentCaptured>(ea.Body.Span)
                ?? throw new JsonException("Empty payment.");
            return new(
                messageId,
                e.PaymentId,
                e.OrderId,
                e.RestaurantId,
                e.Amount,
                e.Method,
                e.ExternalReference,
                e.CapturedAtUtc,
                false
            );
        }
        if (ea.BasicProperties.Type == typeof(PaymentRefunded).FullName)
        {
            PaymentRefunded e =
                JsonSerializer.Deserialize<PaymentRefunded>(ea.Body.Span)
                ?? throw new JsonException("Empty refund.");
            return new(
                messageId,
                e.PaymentId,
                e.OrderId,
                e.RestaurantId,
                e.Amount,
                e.Method,
                e.Reason,
                e.RefundedAtUtc,
                true
            );
        }
        throw new NotSupportedException(ea.BasicProperties.Type);
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
