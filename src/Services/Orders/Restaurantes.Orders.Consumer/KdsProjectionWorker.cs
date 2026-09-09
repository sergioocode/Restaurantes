using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Consumer.Handlers;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Infrastructure.Persistence;
using Restaurantes.Orders.Infrastructure.Persistence.Read;

namespace Restaurantes.Orders.Consumer;

public sealed class KdsProjectionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<KdsProjectionWorker> logger
) : BackgroundService
{
    private IConnection? _connection;
    private IChannel? _channel;
    private IChannel? _notificationChannel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeDatabaseAsync(stoppingToken);
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
                    "Orders consumer disconnected. Retrying in five seconds."
                );
                await DisposeBrokerResourcesAsync();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task InitializeDatabaseAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<OrderReadDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }

    private async Task ConsumeAsync(
        RabbitMqOptions rabbitMqOptions,
        CancellationToken cancellationToken
    )
    {
        ConnectionFactory factory = RabbitMqConnectionFactory.Create(rabbitMqOptions);
        _connection = await factory.CreateConnectionAsync("orders-kds-consumer", cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        _notificationChannel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(true, true),
            cancellationToken
        );
        await OrdersTopology.DeclareAsync(_channel, cancellationToken);
        await _channel.BasicQosAsync(0, 10, false, cancellationToken);
        AsyncEventingBasicConsumer consumer = new(_channel);
        consumer.ReceivedAsync += HandleMessageAsync;
        await _channel.BasicConsumeAsync(
            OrdersTopology.KdsQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken
        );
        logger.LogInformation("KDS projection listening on {Queue}.", OrdersTopology.KdsQueueName);
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
            if (!Guid.TryParse(eventArgs.BasicProperties.MessageId, out Guid messageId))
            {
                throw new JsonException("MessageId is invalid.");
            }

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            KdsProjectionHandler handler =
                scope.ServiceProvider.GetRequiredService<KdsProjectionHandler>();
            KdsProjectionNotification? notification = await handler.ProjectAsync(
                messageId,
                eventArgs.BasicProperties.Type,
                eventArgs.Body,
                eventArgs.CancellationToken
            );
            if (notification is not null)
            {
                await PublishKdsNotificationAsync(notification, eventArgs.CancellationToken);
            }

            await _channel.BasicAckAsync(eventArgs.DeliveryTag, false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            logger.LogError(exception, "Invalid Orders message sent to dead-letter queue.");
            await _channel.BasicNackAsync(eventArgs.DeliveryTag, false, requeue: false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Orders message failed and will be requeued.");
            await _channel.BasicNackAsync(eventArgs.DeliveryTag, false, requeue: true);
        }
    }

    private async Task PublishKdsNotificationAsync(
        KdsProjectionNotification projection,
        CancellationToken cancellationToken
    )
    {
        if (_notificationChannel is null)
        {
            throw new InvalidOperationException("The KDS notification channel is unavailable.");
        }

        KdsOrderUpdated notification = new(
            projection.OrderId,
            projection.RestaurantId,
            projection.Status,
            projection.Version,
            timeProvider.GetUtcNow().UtcDateTime,
            projection.AffectedStationCodes
        );
        BasicProperties properties = new()
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = Guid.NewGuid().ToString(),
            Type = typeof(KdsOrderUpdated).FullName,
        };
        await _notificationChannel.BasicPublishAsync(
            OrdersTopology.ExchangeName,
            OrdersTopology.KdsOrderUpdatedRoutingKey,
            mandatory: true,
            basicProperties: properties,
            body: JsonSerializer.SerializeToUtf8Bytes(notification),
            cancellationToken: cancellationToken
        );
        logger.LogInformation(
            "KDS SignalR notification published for order {OrderId}, version {Version}.",
            notification.OrderId,
            notification.Version
        );
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await DisposeBrokerResourcesAsync();
    }

    private async Task DisposeBrokerResourcesAsync()
    {
        if (_notificationChannel is not null)
        {
            await _notificationChannel.DisposeAsync();
            _notificationChannel = null;
        }
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
