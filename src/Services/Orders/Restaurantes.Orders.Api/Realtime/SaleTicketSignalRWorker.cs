using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Sales.Contracts.Events;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.Orders.Api.Realtime;

public sealed class SaleTicketSignalRWorker(
    IHubContext<OrdersHub, IKdsClient> hub,
    IOptions<RabbitMqOptions> options,
    ILogger<SaleTicketSignalRWorker> logger
) : AsyncDisposingBackgroundService
{
    private IConnection? connection;
    private IChannel? channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(options.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Sale ticket notifications disconnected; retrying.");
                await DisposeResourcesAsync();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(
        RabbitMqOptions rabbitMqOptions,
        CancellationToken cancellationToken
    )
    {
        ConnectionFactory factory = RabbitMqConnectionFactory.Create(rabbitMqOptions);
        connection = await factory.CreateConnectionAsync(
            "orders-sale-ticket-signalr",
            cancellationToken
        );
        channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(true, true),
            cancellationToken
        );
        await SalesTopology.DeclareAsync(channel, cancellationToken);
        await channel.BasicQosAsync(0, 20, false, cancellationToken);

        AsyncEventingBasicConsumer consumer = new(channel);
        consumer.ReceivedAsync += HandleMessageAsync;
        await channel.BasicConsumeAsync(
            SalesTopology.TicketNotificationQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken
        );
        logger.LogInformation("Sale ticket notifications listening.");
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private async Task HandleMessageAsync(object sender, BasicDeliverEventArgs eventArgs)
    {
        if (channel is null)
        {
            return;
        }

        try
        {
            if (eventArgs.BasicProperties.Type != typeof(SaleCompleted).FullName)
            {
                throw new NotSupportedException(eventArgs.BasicProperties.Type);
            }

            SaleCompleted sale =
                JsonSerializer.Deserialize<SaleCompleted>(eventArgs.Body.Span)
                ?? throw new JsonException("SaleCompleted body is empty.");
            await Task.WhenAll(
                hub.Clients.Group(OrdersHub.PosGroup(sale.RestaurantId)).SaleCompleted(sale.SaleId),
                hub.Clients.Group(OrdersHub.CommanderGroup(sale.RestaurantId))
                    .SaleCompleted(sale.SaleId)
            );
            await channel.BasicAckAsync(eventArgs.DeliveryTag, false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            logger.LogError(exception, "Invalid sale ticket notification sent to dead-letter.");
            await channel.BasicNackAsync(eventArgs.DeliveryTag, false, requeue: false);
        }
        catch (Exception exception)
        {
            RabbitMqRetryResult retry = await RabbitMqRetry.ScheduleOrDeadLetterAsync(
                channel,
                eventArgs,
                SalesTopology.TicketNotificationQueueName,
                exception,
                eventArgs.CancellationToken
            );
            logger.LogError(
                exception,
                "Sale ticket notification failed. Retry scheduled: {RetryScheduled}; attempt {RetryAttempt}/{MaximumAttempts}; delay {RetryDelay}.",
                retry.Scheduled,
                retry.Attempt,
                RabbitMqRetry.MaximumAttempts,
                retry.Delay
            );
        }
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
