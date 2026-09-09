using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Sales.Application;
using Restaurantes.Sales.Infrastructure.Persistence.Write;

namespace Restaurantes.Sales.Consumer;

public sealed class CheckoutProcessWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    ILogger<CheckoutProcessWorker> log
) : BackgroundService
{
    private IConnection? connection;
    private IChannel? channel;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await using (AsyncServiceScope s = scopes.CreateAsyncScope())
        {
            await s
                .ServiceProvider.GetRequiredService<SaleWriteDbContext>()
                .Database.MigrateAsync(ct);
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                connection = await RabbitMqConnectionFactory
                    .Create(options.Value)
                    .CreateConnectionAsync("sales-checkout", ct);
                channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await SalesTopology.DeclareAsync(channel, ct);
                await channel.BasicQosAsync(0, 20, false, ct);
                AsyncEventingBasicConsumer consumer = new(channel);
                consumer.ReceivedAsync += Handle;
                await channel.BasicConsumeAsync(
                    SalesTopology.CheckoutQueueName,
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
                log.LogError(e, "Sales checkout consumer disconnected; retrying.");
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
            if (!Guid.TryParse(ea.BasicProperties.MessageId, out Guid messageId))
            {
                throw new JsonException("Invalid MessageId");
            }

            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            SaleCommandService service =
                scope.ServiceProvider.GetRequiredService<SaleCommandService>();
            if (ea.BasicProperties.Type == typeof(PaymentCaptured).FullName)
            {
                PaymentCaptured integrationEvent =
                    JsonSerializer.Deserialize<PaymentCaptured>(ea.Body.Span)
                    ?? throw new JsonException("Empty payment");
                await service.ProcessAsync(messageId, integrationEvent, CancellationToken.None);
            }
            else if (ea.BasicProperties.Type == typeof(PaymentRefunded).FullName)
            {
                PaymentRefunded integrationEvent =
                    JsonSerializer.Deserialize<PaymentRefunded>(ea.Body.Span)
                    ?? throw new JsonException("Empty refund");
                await service.ProcessAsync(messageId, integrationEvent, CancellationToken.None);
            }
            else if (ea.BasicProperties.Type == typeof(OrderDelivered).FullName)
            {
                OrderDelivered integrationEvent =
                    JsonSerializer.Deserialize<OrderDelivered>(ea.Body.Span)
                    ?? throw new JsonException("Empty order");
                await service.ProcessAsync(messageId, integrationEvent, CancellationToken.None);
            }
            else
            {
                throw new NotSupportedException(ea.BasicProperties.Type);
            }

            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            log.LogError(e, "Invalid Sales event");
            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (Exception e)
        {
            log.LogError(e, "Sales event will retry");
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
