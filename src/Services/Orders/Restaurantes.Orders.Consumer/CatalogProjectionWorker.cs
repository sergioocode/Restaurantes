using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Catalog.Contracts;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Infrastructure.Persistence.Write;

namespace Restaurantes.Orders.Consumer;

public sealed class CatalogProjectionWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    ILogger<CatalogProjectionWorker> log
) : BackgroundService
{
    private IConnection? connection;
    private IChannel? channel;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await using (AsyncServiceScope s = scopes.CreateAsyncScope())
        {
            await s
                .ServiceProvider.GetRequiredService<OrderWriteDbContext>()
                .Database.MigrateAsync(ct);
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                ConnectionFactory f = RabbitMqConnectionFactory.Create(options.Value);
                connection = await f.CreateConnectionAsync("orders-catalog-projection", ct);
                channel = await connection.CreateChannelAsync(cancellationToken: ct);
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
                await Dispose();
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
            if (ea.BasicProperties.Type == typeof(CatalogItemChanged).FullName)
            {
                CatalogItemChanged e =
                    JsonSerializer.Deserialize<CatalogItemChanged>(ea.Body.Span)
                    ?? throw new JsonException("Empty catalog item");
                e = e with { OccurredAtUtc = EnsureUtc(e.OccurredAtUtc) };
                OrderCatalogItem? x = await db.CatalogItems.SingleOrDefaultAsync(x =>
                    x.RestaurantId == e.RestaurantId && x.ProductId == e.ProductId
                );
                if (x is null)
                {
                    db.CatalogItems.Add(
                        new OrderCatalogItem
                        {
                            Id = Guid.NewGuid(),
                            RestaurantId = e.RestaurantId,
                            ProductId = e.ProductId,
                            ProductName = e.ProductName,
                            Price = e.Price,
                            CategoryId = e.CategoryId,
                            CategoryName = e.CategoryName,
                            StationCode = e.PreparationStationCode,
                            StationName = e.PreparationStationName,
                            IsAvailable = e.IsAvailable,
                            Version = e.Version,
                            UpdatedAtUtc = e.OccurredAtUtc,
                        }
                    );
                }
                else if (x.Version < e.Version)
                {
                    x.ProductName = e.ProductName;
                    x.Price = e.Price;
                    x.CategoryId = e.CategoryId;
                    x.CategoryName = e.CategoryName;
                    x.StationCode = e.PreparationStationCode;
                    x.StationName = e.PreparationStationName;
                    x.IsAvailable = e.IsAvailable;
                    x.Version = e.Version;
                    x.UpdatedAtUtc = e.OccurredAtUtc;
                }
            }
            else if (ea.BasicProperties.Type == typeof(KitchenStationChanged).FullName)
            {
                KitchenStationChanged e =
                    JsonSerializer.Deserialize<KitchenStationChanged>(ea.Body.Span)
                    ?? throw new JsonException("Empty kitchen station");
                e = e with { OccurredAtUtc = EnsureUtc(e.OccurredAtUtc) };
                OrderKitchenStationConfiguration? x =
                    await db.KitchenStationConfigurations.SingleOrDefaultAsync(x =>
                        x.RestaurantId == e.RestaurantId && x.Code == e.Code
                    );
                if (x is null)
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
                else if (x.Version < e.Version)
                {
                    x.Name = e.Name;
                    x.IsPrimary = e.IsPrimary;
                    x.RequiresPrimaryDispatch = e.RequiresPrimaryDispatch;
                    x.Priority = e.Priority;
                    x.IsActive = e.IsActive;
                    x.Version = e.Version;
                    x.UpdatedAtUtc = e.OccurredAtUtc;
                }
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
            log.LogError(e, "Catalog integration event will be retried.");
            await channel.BasicNackAsync(ea.DeliveryTag, false, true);
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        await Dispose();
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    private new async Task Dispose()
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
