using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Contracts.Events;

namespace Restaurantes.Orders.Api.Read.Realtime;

public sealed class KdsSignalRWorker(
    IHubContext<KdsHub, IKdsClient> hubContext,
    IOptions<RabbitMqOptions> options,
    ILogger<KdsSignalRWorker> logger
) : BackgroundService
{
    private IConnection? _connection;
    private IChannel? _channel;

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
                logger.LogError(
                    exception,
                    "KDS SignalR consumer disconnected. Retrying in five seconds."
                );
                await DisposeBrokerResourcesAsync();
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
        _connection = await factory.CreateConnectionAsync("orders-kds-signalr", cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await OrdersTopology.DeclareAsync(_channel, cancellationToken);
        await _channel.BasicQosAsync(0, 20, false, cancellationToken);

        AsyncEventingBasicConsumer consumer = new(_channel);
        consumer.ReceivedAsync += HandleMessageAsync;
        await _channel.BasicConsumeAsync(
            OrdersTopology.KdsSignalRQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken
        );
        logger.LogInformation(
            "KDS SignalR notifications listening on {Queue}.",
            OrdersTopology.KdsSignalRQueueName
        );
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private async Task HandleMessageAsync(object sender, BasicDeliverEventArgs eventArgs)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            if (eventArgs.BasicProperties.Type != typeof(KdsOrderUpdated).FullName)
            {
                throw new NotSupportedException(
                    $"Unsupported KDS notification: {eventArgs.BasicProperties.Type}"
                );
            }

            KdsOrderUpdated notification =
                JsonSerializer.Deserialize<KdsOrderUpdated>(eventArgs.Body.Span)
                ?? throw new JsonException("KDS notification body is empty.");
            HashSet<string> groups = [KdsHub.StationGroup(notification.RestaurantId, "CHEF")];
            foreach (string stationCode in notification.StationCodes)
            {
                groups.Add(KdsHub.StationGroup(notification.RestaurantId, stationCode));
            }
            await Task.WhenAll(
                groups.Select(group => hubContext.Clients.Group(group).OrderUpdated(notification))
            );
            await _channel.BasicAckAsync(eventArgs.DeliveryTag, false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            logger.LogError(exception, "Invalid KDS SignalR notification sent to dead-letter.");
            await _channel.BasicNackAsync(eventArgs.DeliveryTag, false, requeue: false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "KDS SignalR notification failed and will be requeued.");
            await _channel.BasicNackAsync(eventArgs.DeliveryTag, false, requeue: true);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await DisposeBrokerResourcesAsync();
    }

    private async Task DisposeBrokerResourcesAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
