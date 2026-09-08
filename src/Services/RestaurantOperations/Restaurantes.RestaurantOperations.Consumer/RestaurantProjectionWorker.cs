using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.RestaurantOperations.Contracts;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read;

namespace Restaurantes.RestaurantOperations.Consumer;

public sealed class RestaurantProjectionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<RestaurantProjectionWorker> logger
) : BackgroundService
{
    private IConnection? _connection;
    private IChannel? _channel;

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
                    "RabbitMQ consumer disconnected. Retrying in five seconds."
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
            .ServiceProvider.GetRequiredService<RestaurantReadDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }

    private async Task ConsumeAsync(
        RabbitMqOptions rabbitMqOptions,
        CancellationToken cancellationToken
    )
    {
        ConnectionFactory factory = RabbitMqConnectionFactory.Create(rabbitMqOptions);
        _connection = await factory.CreateConnectionAsync(
            "restaurant-operations-read-model-consumer",
            cancellationToken
        );
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await RestaurantOperationsTopology.DeclareAsync(_channel, cancellationToken);
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 10,
            global: false,
            cancellationToken: cancellationToken
        );

        AsyncEventingBasicConsumer consumer = new(_channel);
        consumer.ReceivedAsync += HandleMessageAsync;
        await _channel.BasicConsumeAsync(
            queue: RestaurantOperationsTopology.ReadModelQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken
        );

        logger.LogInformation(
            "Restaurant projection consumer listening on queue {Queue}.",
            RestaurantOperationsTopology.ReadModelQueueName
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
            if (!Guid.TryParse(eventArgs.BasicProperties.MessageId, out Guid messageId))
            {
                throw new JsonException("RabbitMQ message does not contain a valid MessageId.");
            }

            if (eventArgs.BasicProperties.Type != typeof(RestaurantChanged).FullName)
            {
                throw new NotSupportedException(
                    $"Unsupported event type: {eventArgs.BasicProperties.Type}"
                );
            }

            RestaurantChanged integrationEvent =
                JsonSerializer.Deserialize<RestaurantChanged>(eventArgs.Body.Span)
                ?? throw new JsonException("RabbitMQ message body is empty.");
            integrationEvent = integrationEvent with
            {
                OccurredAtUtc = EnsureUtc(integrationEvent.OccurredAtUtc),
            };

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            RestaurantReadDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<RestaurantReadDbContext>();

            if (!await dbContext.InboxMessages.AnyAsync(item => item.Id == messageId))
            {
                RestaurantReadModel? projection = await dbContext.Restaurants.FindAsync([
                    integrationEvent.RestaurantId,
                ]);
                if (projection is null)
                {
                    dbContext.Restaurants.Add(
                        new RestaurantReadModel
                        {
                            Id = integrationEvent.RestaurantId,
                            Code = integrationEvent.Code,
                            Name = integrationEvent.Name,
                            Address = integrationEvent.Address,
                            IsActive = integrationEvent.IsActive,
                            Version = integrationEvent.Version,
                            UpdatedAtUtc = integrationEvent.OccurredAtUtc,
                        }
                    );
                }
                else if (projection.Version < integrationEvent.Version)
                {
                    projection.Code = integrationEvent.Code;
                    projection.Name = integrationEvent.Name;
                    projection.Address = integrationEvent.Address;
                    projection.IsActive = integrationEvent.IsActive;
                    projection.Version = integrationEvent.Version;
                    projection.UpdatedAtUtc = integrationEvent.OccurredAtUtc;
                }

                dbContext.InboxMessages.Add(
                    new InboxMessage
                    {
                        Id = messageId,
                        Type = eventArgs.BasicProperties.Type!,
                        ProcessedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                    }
                );
                await dbContext.SaveChangesAsync();
            }

            await _channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false);
            logger.LogInformation(
                "Projected RabbitMQ message {MessageId} and sent ACK.",
                messageId
            );
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            logger.LogError(exception, "Invalid RabbitMQ message sent to dead-letter queue.");
            await _channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not project RabbitMQ message. It will be retried.");
            await _channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await DisposeBrokerResourcesAsync();
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
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
