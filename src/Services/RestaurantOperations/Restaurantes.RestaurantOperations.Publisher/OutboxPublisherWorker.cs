using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Write;

namespace Restaurantes.RestaurantOperations.Publisher;

public sealed class OutboxPublisherWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxPublisherWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeDatabaseAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishUntilDisconnectedAsync(options.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "RabbitMQ publisher disconnected. Retrying in five seconds."
                );
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task InitializeDatabaseAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<RestaurantWriteDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }

    private async Task PublishUntilDisconnectedAsync(
        RabbitMqOptions rabbitMqOptions,
        CancellationToken cancellationToken
    )
    {
        ConnectionFactory factory = RabbitMqConnectionFactory.Create(rabbitMqOptions);
        await using IConnection connection = await factory.CreateConnectionAsync(
            "restaurant-operations-outbox-publisher",
            cancellationToken
        );
        await using IChannel channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            ),
            cancellationToken
        );

        await RestaurantOperationsTopology.DeclareAsync(channel, cancellationToken);
        logger.LogInformation(
            "Outbox publisher connected to RabbitMQ exchange {Exchange}.",
            RestaurantOperationsTopology.ExchangeName
        );

        while (!cancellationToken.IsCancellationRequested && connection.IsOpen && channel.IsOpen)
        {
            bool publishedAny = await PublishBatchAsync(channel, cancellationToken);
            await Task.Delay(
                publishedAny ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(1),
                cancellationToken
            );
        }
    }

    private async Task<bool> PublishBatchAsync(
        IChannel channel,
        CancellationToken cancellationToken
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        RestaurantWriteDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<RestaurantWriteDbContext>();
        List<OutboxMessage> messages = await dbContext
            .OutboxMessages.Where(item => item.ProcessedAtUtc == null)
            .OrderBy(item => item.OccurredAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (OutboxMessage? message in messages)
        {
            try
            {
                BasicProperties properties = new()
                {
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent,
                    MessageId = message.Id.ToString(),
                    Type = message.Type,
                };

                await channel.BasicPublishAsync(
                    exchange: RestaurantOperationsTopology.ExchangeName,
                    routingKey: RestaurantOperationsTopology.RestaurantChangedRoutingKey,
                    mandatory: true,
                    basicProperties: properties,
                    body: Encoding.UTF8.GetBytes(message.Payload),
                    cancellationToken: cancellationToken
                );

                message.ProcessedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                message.Error = null;
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Published outbox message {MessageId}.", message.Id);
            }
            catch (Exception exception)
            {
                message.Error = exception.Message[..Math.Min(exception.Message.Length, 2000)];
                await dbContext.SaveChangesAsync(cancellationToken);
                throw;
            }
        }

        return messages.Count > 0;
    }
}
