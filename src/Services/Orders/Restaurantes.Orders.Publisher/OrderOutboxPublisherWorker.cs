using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Contracts;
using Restaurantes.Orders.Infrastructure.Persistence;
using Restaurantes.Orders.Infrastructure.Persistence.Write;

namespace Restaurantes.Orders.Publisher;

public sealed class OrderOutboxPublisherWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<OrderOutboxPublisherWorker> logger
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
                    "Orders publisher disconnected. Retrying in five seconds."
                );
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task InitializeDatabaseAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<OrderWriteDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }

    private async Task PublishUntilDisconnectedAsync(
        RabbitMqOptions rabbitMqOptions,
        CancellationToken cancellationToken
    )
    {
        ConnectionFactory factory = RabbitMqConnectionFactory.Create(rabbitMqOptions);
        await using IConnection connection = await factory.CreateConnectionAsync(
            "orders-outbox-publisher",
            cancellationToken
        );
        await using IChannel channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(true, true),
            cancellationToken
        );
        await OrdersTopology.DeclareAsync(channel, cancellationToken);
        logger.LogInformation(
            "Orders outbox publisher connected to {Exchange}.",
            OrdersTopology.ExchangeName
        );

        while (!cancellationToken.IsCancellationRequested && connection.IsOpen && channel.IsOpen)
        {
            bool published = await PublishBatchAsync(channel, cancellationToken);
            await Task.Delay(
                published ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(1),
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
        OrderWriteDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<OrderWriteDbContext>();
        List<OutboxMessage> messages = await dbContext
            .OutboxMessages.Where(item => item.ProcessedAtUtc == null)
            .OrderBy(item => item.OccurredAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (OutboxMessage? message in messages)
        {
            string routingKey = message.Type switch
            {
                var type when type == typeof(OrderCreated).FullName =>
                    OrdersTopology.OrderCreatedRoutingKey,
                var type when type == typeof(OrderSubmitted).FullName =>
                    OrdersTopology.OrderSubmittedRoutingKey,
                var type when type == typeof(OrderPreparationStarted).FullName =>
                    OrdersTopology.OrderPreparationStartedRoutingKey,
                var type when type == typeof(KitchenTicketPreparationStarted).FullName =>
                    OrdersTopology.OrderPreparationStartedRoutingKey,
                var type when type == typeof(OrderReady).FullName =>
                    OrdersTopology.OrderReadyRoutingKey,
                var type when type == typeof(KitchenTicketReady).FullName =>
                    OrdersTopology.KitchenTicketUpdatedRoutingKey,
                var type when type == typeof(KitchenTicketDispatched).FullName =>
                    OrdersTopology.KitchenTicketUpdatedRoutingKey,
                var type when type == typeof(OrderDelivered).FullName =>
                    OrdersTopology.OrderDeliveredRoutingKey,
                var type when type == typeof(OrderLineCancelled).FullName =>
                    OrdersTopology.OrderLineCancelledRoutingKey,
                var type when type == typeof(OrderCancelled).FullName =>
                    OrdersTopology.OrderCancelledRoutingKey,
                _ => throw new NotSupportedException($"Unsupported order event: {message.Type}"),
            };

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
                    OrdersTopology.ExchangeName,
                    routingKey,
                    mandatory: true,
                    basicProperties: properties,
                    body: Encoding.UTF8.GetBytes(message.Payload),
                    cancellationToken: cancellationToken
                );
                message.ProcessedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                message.Error = null;
                await dbContext.SaveChangesAsync(cancellationToken);
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
