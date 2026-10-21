using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.Orders.Infrastructure.Workers;

public sealed class CatalogProjectionWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    ILogger<CatalogProjectionWorker> log
) : AsyncDisposingBackgroundService
{
    private IConnection? connection;
    private IChannel? channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CancellationToken ct = stoppingToken;
        bool databaseInitialized = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!databaseInitialized)
                {
                    await using AsyncServiceScope s = scopes.CreateAsyncScope();
                    await s
                        .ServiceProvider.GetRequiredService<OrderWriteDbContext>()
                        .Database.MigrateAsync(ct);
                    databaseInitialized = true;
                }

                ConnectionFactory f = RabbitMqConnectionFactory.Create(options.Value);
                connection = await f.CreateConnectionAsync("orders-catalog-projection", ct);
                channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(true, true),
                    ct
                );
                await CatalogTopology.DeclareAsync(channel, ct);
                await channel.BasicQosAsync(0, 10, false, ct);
                AsyncEventingBasicConsumer consumer = new(channel);
                consumer.ReceivedAsync += Handle;
                await channel.BasicConsumeAsync(
                    CatalogTopology.OrdersIntegrationQueueName,
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
                log.LogError(e, "Orders catalog projection disconnected; retrying.");
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
            await using AsyncServiceScope s = scopes.CreateAsyncScope();
            OrderWriteDbContext db = s.ServiceProvider.GetRequiredService<OrderWriteDbContext>();
            if (ea.BasicProperties.Type == typeof(CategoryChanged).FullName)
            {
                await ProjectCategoryAsync(db, ea.Body);
            }
            else if (ea.BasicProperties.Type == typeof(CatalogItemChanged).FullName)
            {
                await ProjectCatalogItemAsync(db, ea.Body);
            }
            else if (ea.BasicProperties.Type == typeof(KitchenStationChanged).FullName)
            {
                await ProjectKitchenStationAsync(db, ea.Body);
            }
            else
            {
                throw new NotSupportedException(ea.BasicProperties.Type);
            }
            await db.SaveChangesAsync();
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            log.LogError(e, "Invalid Catalog integration event.");
            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (Exception e)
        {
            RabbitMqRetryResult retry = await RabbitMqRetry.ScheduleOrDeadLetterAsync(
                channel,
                ea,
                CatalogTopology.OrdersIntegrationQueueName,
                e,
                ea.CancellationToken
            );
            log.LogError(
                e,
                "Catalog integration event failed. Retry scheduled: {RetryScheduled}; attempt {RetryAttempt}/{MaximumAttempts}; delay {RetryDelay}.",
                retry.Scheduled,
                retry.Attempt,
                RabbitMqRetry.MaximumAttempts,
                retry.Delay
            );
        }
    }

    private static async Task ProjectCategoryAsync(
        OrderWriteDbContext db,
        ReadOnlyMemory<byte> body
    )
    {
        CategoryChanged e =
            JsonSerializer.Deserialize<CategoryChanged>(body.Span)
            ?? throw new JsonException("Empty category");
        List<OrderCatalogItem> products = await db
            .CatalogItems.Where(item => item.CategoryId == e.CategoryId)
            .ToListAsync();
        foreach (OrderCatalogItem product in products)
        {
            product.RequiresKds = e.RequiresKds;
            if (e.RequiresKds && string.IsNullOrWhiteSpace(product.StationCode))
            {
                product.StationCode = e.DefaultStationCode;
                product.StationName = e.DefaultStationName;
            }
        }
    }

    private static async Task ProjectCatalogItemAsync(
        OrderWriteDbContext db,
        ReadOnlyMemory<byte> body
    )
    {
        CatalogItemChanged e =
            JsonSerializer.Deserialize<CatalogItemChanged>(body.Span)
            ?? throw new JsonException("Empty catalog item");
        e = e with { OccurredAtUtc = EnsureUtc(e.OccurredAtUtc) };
        OrderCatalogItem? item = await db.CatalogItems.SingleOrDefaultAsync(x =>
            x.RestaurantId == e.RestaurantId && x.ProductId == e.ProductId
        );
        if (item is null)
        {
            db.CatalogItems.Add(
                new OrderCatalogItem
                {
                    Id = Guid.NewGuid(),
                    RestaurantId = e.RestaurantId,
                    ProductId = e.ProductId,
                    ProductName = e.ProductName,
                    Price = e.Price,
                    TaxRate = e.TaxRate,
                    DefaultFormat = e.DefaultFormat,
                    FormatsJson = JsonSerializer.Serialize(e.Formats ?? []),
                    CategoryId = e.CategoryId,
                    CategoryName = e.CategoryName,
                    StationCode = e.PreparationStationCode,
                    StationName = e.PreparationStationName,
                    RequiresKds = e.RequiresKds,
                    IsAvailable = e.IsAvailable,
                    ExtraGroupsJson = JsonSerializer.Serialize(e.ExtraGroups ?? []),
                    Version = e.Version,
                    UpdatedAtUtc = e.OccurredAtUtc,
                }
            );
        }
        else if (item.Version < e.Version)
        {
            item.ProductName = e.ProductName;
            item.Price = e.Price;
            item.TaxRate = e.TaxRate;
            item.DefaultFormat = e.DefaultFormat;
            item.FormatsJson = JsonSerializer.Serialize(e.Formats ?? []);
            item.CategoryId = e.CategoryId;
            item.CategoryName = e.CategoryName;
            item.StationCode = e.PreparationStationCode;
            item.StationName = e.PreparationStationName;
            item.RequiresKds = e.RequiresKds;
            item.IsAvailable = e.IsAvailable;
            item.ExtraGroupsJson = JsonSerializer.Serialize(e.ExtraGroups ?? []);
            item.Version = e.Version;
            item.UpdatedAtUtc = e.OccurredAtUtc;
        }
    }

    private static async Task ProjectKitchenStationAsync(
        OrderWriteDbContext db,
        ReadOnlyMemory<byte> body
    )
    {
        KitchenStationChanged e =
            JsonSerializer.Deserialize<KitchenStationChanged>(body.Span)
            ?? throw new JsonException("Empty kitchen station");
        e = e with { OccurredAtUtc = EnsureUtc(e.OccurredAtUtc) };
        OrderKitchenStationConfiguration? station =
            await db.KitchenStationConfigurations.SingleOrDefaultAsync(x =>
                x.RestaurantId == e.RestaurantId && x.Code == e.Code
            );
        if (e.IsDeleted)
        {
            if (station is not null && station.Id == e.StationId && station.Version < e.Version)
            {
                db.KitchenStationConfigurations.Remove(station);
            }
        }
        else if (station is null)
        {
            db.KitchenStationConfigurations.Add(
                new OrderKitchenStationConfiguration
                {
                    Id = e.StationId,
                    RestaurantId = e.RestaurantId,
                    Code = e.Code,
                    Name = e.Name,
                    IsPrimary = e.IsPrimary,
                    RequiresPrimaryDispatch = e.RequiresPrimaryDispatch,
                    Priority = e.Priority,
                    IsActive = e.IsActive,
                    Version = e.Version,
                    UpdatedAtUtc = e.OccurredAtUtc,
                }
            );
        }
        else if (station.Version < e.Version)
        {
            station.Name = e.Name;
            station.IsPrimary = e.IsPrimary;
            station.RequiresPrimaryDispatch = e.RequiresPrimaryDispatch;
            station.Priority = e.Priority;
            station.IsActive = e.IsActive;
            station.Version = e.Version;
            station.UpdatedAtUtc = e.OccurredAtUtc;
        }
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
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
