using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Infrastructure.Persistence;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Payments.Contracts.Events;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.CashRegister.Infrastructure.Workers;

public sealed class CashPaymentProjectionWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    ILogger<CashPaymentProjectionWorker> log
) : AsyncDisposingBackgroundService
{
    private IConnection? connection;
    private IChannel? channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CancellationToken ct = stoppingToken;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                connection = await RabbitMqConnectionFactory
                    .Create(options.Value)
                    .CreateConnectionAsync("cash-register", ct);
                channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(true, true),
                    ct
                );
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
                await DisposeResourcesAsync();
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
            if (!Guid.TryParse(ea.BasicProperties.MessageId, out Guid messageId))
            {
                throw new JsonException("Invalid payment event id.");
            }

            PaymentProjection payment = ReadPayment(messageId, ea);
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            CashRegisterDbContext db =
                scope.ServiceProvider.GetRequiredService<CashRegisterDbContext>();
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(
                ea.CancellationToken
            );
            await scope
                .ServiceProvider.GetRequiredService<CashPaymentProjectionService>()
                .Project(payment, CancellationToken.None);
            await transaction.CommitAsync(ea.CancellationToken);
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            log.LogError(ex, "Invalid cash-register event.");
            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (Exception ex)
        {
            RabbitMqRetryResult retry = await RabbitMqRetry.ScheduleOrDeadLetterAsync(
                channel,
                ea,
                PaymentsTopology.CashRegisterQueueName,
                ex,
                ea.CancellationToken
            );
            log.LogError(
                ex,
                "Cash-register event failed. Retry scheduled: {RetryScheduled}; attempt {RetryAttempt}/{MaximumAttempts}; delay {RetryDelay}.",
                retry.Scheduled,
                retry.Attempt,
                RabbitMqRetry.MaximumAttempts,
                retry.Delay
            );
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
                e.Amount + e.TipAmount,
                e.Method,
                e.ExternalReference,
                e.CapturedAtUtc,
                e.CashRegisterSessionId,
                e.TenderedAmount,
                e.ChangeAmount,
                GetSaleId(e)
            );
        }
        throw new NotSupportedException(ea.BasicProperties.Type);
    }

    private static Guid GetSaleId(PaymentCaptured payment)
    {
        if (payment.SaleGroupId is Guid groupId && groupId != Guid.Empty)
        {
            return groupId;
        }

        if (payment.PaymentTransactionId is Guid transactionId && transactionId != Guid.Empty)
        {
            return transactionId;
        }

        return payment.PaymentId;
    }

    protected override async Task DisposeResourcesAsync()
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
