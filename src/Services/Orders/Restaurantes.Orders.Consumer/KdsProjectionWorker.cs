using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Orders.Contracts.Responses;
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

            ProjectionData data = Deserialize(eventArgs.BasicProperties.Type, eventArgs.Body.Span);
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            OrderReadDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<OrderReadDbContext>();

            bool shouldNotify = false;
            if (!await dbContext.InboxMessages.AnyAsync(item => item.Id == messageId))
            {
                KitchenOrderReadModel? projection = await dbContext.KitchenOrders.FindAsync([
                    data.OrderId,
                ]);
                if (projection is null)
                {
                    projection = new KitchenOrderReadModel { Id = data.OrderId };
                    dbContext.KitchenOrders.Add(projection);
                }

                if (projection.Version < data.Version)
                {
                    projection.RestaurantId = data.RestaurantId;
                    projection.TableId = data.TableId;
                    projection.GuestCount = data.GuestCount;
                    projection.DiningSessionId = data.DiningSessionId;
                    projection.TableLabel = data.TableLabel;
                    projection.CustomerName = data.CustomerName;
                    projection.ServiceMode = data.ServiceMode;
                    projection.Source = data.Source;
                    projection.PaymentTiming = data.PaymentTiming;
                    projection.Total = data.Total;
                    projection.Status = data.Status;
                    projection.Version = data.Version;
                    projection.CreatedAtUtc = data.CreatedAtUtc;
                    projection.SubmittedAtUtc = data.SubmittedAtUtc;
                    projection.PreparationStartedAtUtc = data.PreparationStartedAtUtc;
                    projection.ReadyAtUtc = data.ReadyAtUtc;
                    projection.DeliveredAtUtc = data.DeliveredAtUtc;
                    projection.LinesJson = JsonSerializer.Serialize(data.Lines);
                    projection.StationsJson = JsonSerializer.Serialize(data.Stations);
                    shouldNotify = data.Status != "Draft";
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
            else if (data.Status != "Draft")
            {
                shouldNotify = true;
            }

            if (shouldNotify)
            {
                await PublishKdsNotificationAsync(data, eventArgs.CancellationToken);
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

    private static ProjectionData Deserialize(string? type, ReadOnlySpan<byte> body)
    {
        if (type == typeof(OrderCreated).FullName)
        {
            OrderCreated value =
                JsonSerializer.Deserialize<OrderCreated>(body)
                ?? throw new JsonException("OrderCreated body is empty.");
            return CreateProjection(
                value.OrderId,
                value.RestaurantId,
                value.TableId,
                value.GuestCount,
                value.DiningSessionId,
                value.TableLabel,
                value.CustomerName,
                value.ServiceMode,
                value.Source,
                value.PaymentTiming,
                "Draft",
                value.Version,
                value.OccurredAtUtc,
                null,
                null,
                null,
                null,
                value.Lines,
                value.Stations,
                []
            );
        }
        if (type == typeof(OrderSubmitted).FullName)
        {
            OrderSubmitted value =
                JsonSerializer.Deserialize<OrderSubmitted>(body)
                ?? throw new JsonException("OrderSubmitted body is empty.");
            return CreateProjection(
                value.OrderId,
                value.RestaurantId,
                value.TableId,
                value.GuestCount,
                null,
                value.TableLabel,
                value.CustomerName,
                value.ServiceMode,
                value.Source,
                value.PaymentTiming,
                "Submitted",
                value.Version,
                value.CreatedAtUtc,
                value.SubmittedAtUtc,
                null,
                null,
                null,
                value.Lines,
                value.Stations,
                value.Stations.Select(station => station.Code).ToList()
            );
        }
        if (type == typeof(KitchenTicketPreparationStarted).FullName)
        {
            KitchenTicketPreparationStarted value =
                JsonSerializer.Deserialize<KitchenTicketPreparationStarted>(body)
                ?? throw new JsonException("KitchenTicketPreparationStarted body is empty.");
            return CreateProjection(
                value.OrderId,
                value.RestaurantId,
                value.TableId,
                value.GuestCount,
                null,
                value.TableLabel,
                value.CustomerName,
                value.ServiceMode,
                value.Source,
                value.PaymentTiming,
                "InPreparation",
                value.Version,
                value.CreatedAtUtc,
                value.SubmittedAtUtc,
                value.PreparationStartedAtUtc,
                null,
                null,
                value.Lines,
                value.Stations,
                [value.ChangedStationCode]
            );
        }
        if (type == typeof(KitchenTicketReady).FullName)
        {
            KitchenTicketReady value =
                JsonSerializer.Deserialize<KitchenTicketReady>(body)
                ?? throw new JsonException("KitchenTicketReady body is empty.");
            return CreateProjection(
                value.OrderId,
                value.RestaurantId,
                value.TableId,
                value.GuestCount,
                null,
                value.TableLabel,
                value.CustomerName,
                value.ServiceMode,
                value.Source,
                value.PaymentTiming,
                value.ReadyAtUtc is null ? "InPreparation" : "Ready",
                value.Version,
                value.CreatedAtUtc,
                value.SubmittedAtUtc,
                value.PreparationStartedAtUtc,
                value.ReadyAtUtc,
                null,
                value.Lines,
                value.Stations,
                [value.ChangedStationCode]
            );
        }
        if (type == typeof(KitchenTicketDispatched).FullName)
        {
            KitchenTicketDispatched value =
                JsonSerializer.Deserialize<KitchenTicketDispatched>(body)
                ?? throw new JsonException("KitchenTicketDispatched body is empty.");
            OrderKitchenStationSnapshot changed = value.Stations.Single(station =>
                station.Code == value.ChangedStationCode
            );
            bool completed = value
                .Stations.Where(station => station.Status != "Cancelled")
                .All(station => station.Status == "Dispatched");
            return CreateProjection(
                value.OrderId,
                value.RestaurantId,
                value.TableId,
                value.GuestCount,
                null,
                value.TableLabel,
                value.CustomerName,
                value.ServiceMode,
                value.Source,
                value.PaymentTiming,
                completed ? "Delivered" : "Ready",
                value.Version,
                value.CreatedAtUtc,
                value.SubmittedAtUtc,
                value.PreparationStartedAtUtc,
                changed.ReadyAtUtc,
                completed ? value.DispatchedAtUtc : null,
                value.Lines,
                value.Stations,
                value.Stations.Select(station => station.Code).ToList()
            );
        }
        if (type == typeof(OrderReady).FullName)
        {
            OrderReady value =
                JsonSerializer.Deserialize<OrderReady>(body)
                ?? throw new JsonException("OrderReady body is empty.");
            return CreateProjection(
                value.OrderId,
                value.RestaurantId,
                value.TableId,
                value.GuestCount,
                null,
                value.TableLabel,
                value.CustomerName,
                value.ServiceMode,
                value.Source,
                value.PaymentTiming,
                "Ready",
                value.Version,
                value.CreatedAtUtc,
                value.SubmittedAtUtc,
                value.PreparationStartedAtUtc,
                value.ReadyAtUtc,
                null,
                value.Lines,
                value.Stations,
                value.Stations.Select(station => station.Code).ToList()
            );
        }
        if (type == typeof(OrderDelivered).FullName)
        {
            OrderDelivered value =
                JsonSerializer.Deserialize<OrderDelivered>(body)
                ?? throw new JsonException("OrderDelivered body is empty.");
            return CreateProjection(
                value.OrderId,
                value.RestaurantId,
                value.TableId,
                value.GuestCount,
                null,
                value.TableLabel,
                value.CustomerName,
                value.ServiceMode,
                value.Source,
                value.PaymentTiming,
                "Delivered",
                value.Version,
                value.CreatedAtUtc,
                value.SubmittedAtUtc,
                value.PreparationStartedAtUtc,
                value.ReadyAtUtc,
                value.DeliveredAtUtc,
                value.Lines,
                value.Stations,
                value.Stations.Select(station => station.Code).ToList()
            );
        }
        if (type == typeof(OrderCancelled).FullName)
        {
            OrderCancelled value =
                JsonSerializer.Deserialize<OrderCancelled>(body)
                ?? throw new JsonException("OrderCancelled body is empty.");
            return CreateProjection(
                value.OrderId,
                value.RestaurantId,
                value.TableId,
                value.GuestCount,
                value.DiningSessionId,
                value.TableLabel,
                value.CustomerName,
                value.ServiceMode,
                value.Source,
                value.PaymentTiming,
                "Cancelled",
                value.Version,
                value.CreatedAtUtc,
                value.SubmittedAtUtc,
                value.Stations.Select(station => station.PreparationStartedAtUtc).Min(),
                value.Stations.Select(station => station.ReadyAtUtc).Max(),
                null,
                value.Lines,
                value.Stations,
                value.Stations.Select(station => station.Code).ToList()
            );
        }
        if (type == typeof(OrderLineCancelled).FullName)
        {
            OrderLineCancelled value =
                JsonSerializer.Deserialize<OrderLineCancelled>(body)
                ?? throw new JsonException("OrderLineCancelled body is empty.");
            return CreateProjection(
                value.OrderId,
                value.RestaurantId,
                value.TableId,
                value.GuestCount,
                value.DiningSessionId,
                value.TableLabel,
                value.CustomerName,
                value.ServiceMode,
                value.Source,
                value.PaymentTiming,
                value.Status,
                value.Version,
                value.CreatedAtUtc,
                value.SubmittedAtUtc,
                value
                    .Stations.Where(station => station.PreparationStartedAtUtc.HasValue)
                    .Select(station => station.PreparationStartedAtUtc)
                    .Min(),
                value.Status == "Ready" ? value.CancelledAtUtc : null,
                null,
                value.Lines,
                value.Stations,
                [value.ChangedStationCode]
            );
        }
        throw new NotSupportedException($"Unsupported order event: {type}");
    }

    private static ProjectionData CreateProjection(
        Guid orderId,
        Guid restaurantId,
        Guid? tableId,
        int? guestCount,
        Guid? diningSessionId,
        string tableLabel,
        string customerName,
        string serviceMode,
        string source,
        string paymentTiming,
        string status,
        int version,
        DateTime createdAtUtc,
        DateTime? submittedAtUtc,
        DateTime? preparationStartedAtUtc,
        DateTime? readyAtUtc,
        DateTime? deliveredAtUtc,
        IReadOnlyList<OrderLineSnapshot> lines,
        IReadOnlyList<OrderKitchenStationSnapshot> stations,
        IReadOnlyList<string> affectedStationCodes
    )
    {
        return new ProjectionData(
            orderId,
            restaurantId,
            tableId,
            guestCount,
            diningSessionId,
            tableLabel,
            customerName,
            serviceMode,
            source,
            paymentTiming,
            status,
            lines
                .Where(line => line.Status != "Cancelled")
                .Sum(line => line.UnitPrice * line.Quantity),
            version,
            createdAtUtc,
            submittedAtUtc,
            preparationStartedAtUtc,
            readyAtUtc,
            deliveredAtUtc,
            MapLines(lines),
            MapStations(stations),
            affectedStationCodes
        );
    }

    private async Task PublishKdsNotificationAsync(
        ProjectionData data,
        CancellationToken cancellationToken
    )
    {
        if (_notificationChannel is null)
        {
            throw new InvalidOperationException("The KDS notification channel is unavailable.");
        }

        KdsOrderUpdated notification = new(
            data.OrderId,
            data.RestaurantId,
            data.Status,
            data.Version,
            timeProvider.GetUtcNow().UtcDateTime,
            data.AffectedStationCodes
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
            data.OrderId,
            data.Version
        );
    }

    private static IReadOnlyList<OrderLineResponse> MapLines(IReadOnlyList<OrderLineSnapshot> lines)
    {
        return lines
            .Select(line => new OrderLineResponse(
                line.Id,
                line.ProductId,
                line.ProductName,
                line.UnitPrice,
                line.Quantity,
                line.UnitPrice * line.Quantity,
                line.Notes,
                line.CategoryId,
                line.CategoryName,
                line.PreparationStationCode,
                line.PreparationStationName,
                line.Status,
                line.CancelledAtUtc,
                line.CancellationReason
            ))
            .ToList();
    }

    private static IReadOnlyList<OrderKitchenStationResponse> MapStations(
        IReadOnlyList<OrderKitchenStationSnapshot> stations
    )
    {
        return stations
            .Select(station => new OrderKitchenStationResponse(
                station.Code,
                station.Name,
                station.Status,
                station.RequiresPrimaryDispatch,
                station.Priority,
                station.PreparationStartedAtUtc,
                station.ReadyAtUtc,
                station.DispatchedAtUtc
            ))
            .ToList();
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

    private sealed record ProjectionData(
        Guid OrderId,
        Guid RestaurantId,
        Guid? TableId,
        int? GuestCount,
        Guid? DiningSessionId,
        string TableLabel,
        string CustomerName,
        string ServiceMode,
        string Source,
        string PaymentTiming,
        string Status,
        decimal Total,
        int Version,
        DateTime CreatedAtUtc,
        DateTime? SubmittedAtUtc,
        DateTime? PreparationStartedAtUtc,
        DateTime? ReadyAtUtc,
        DateTime? DeliveredAtUtc,
        IReadOnlyList<OrderLineResponse> Lines,
        IReadOnlyList<OrderKitchenStationResponse> Stations,
        IReadOnlyList<string> AffectedStationCodes
    );
}
