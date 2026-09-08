using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Catalog.Contracts;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;
using Restaurantes.Messaging.RabbitMq;

namespace Restaurantes.Catalog.Consumer;

public sealed class CatalogProjectionWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
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
                .ServiceProvider.GetRequiredService<CatalogReadDbContext>()
                .Database.MigrateAsync(ct);
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                ConnectionFactory f = RabbitMqConnectionFactory.Create(options.Value);
                connection = await f.CreateConnectionAsync("catalog-read-model", ct);
                channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await CatalogTopology.DeclareAsync(channel, ct);
                await channel.BasicQosAsync(0, 10, false, ct);
                AsyncEventingBasicConsumer consumer = new(channel);
                consumer.ReceivedAsync += Handle;
                await channel.BasicConsumeAsync(
                    CatalogTopology.ReadModelQueueName,
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
                log.LogError(e, "Catalog consumer disconnected; retrying.");
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
            if (!Guid.TryParse(ea.BasicProperties.MessageId, out Guid messageId))
            {
                throw new JsonException("Invalid MessageId");
            }

            await using AsyncServiceScope s = scopes.CreateAsyncScope();
            CatalogReadDbContext db = s.ServiceProvider.GetRequiredService<CatalogReadDbContext>();
            if (!await db.InboxMessages.AnyAsync(x => x.Id == messageId))
            {
                string type = ea.BasicProperties.Type ?? "";
                if (type == typeof(CategoryChanged).FullName)
                {
                    CategoryChanged e = JsonSerializer.Deserialize<CategoryChanged>(ea.Body.Span)!;
                    e = e with { OccurredAtUtc = EnsureUtc(e.OccurredAtUtc) };
                    CategoryReadModel? x = await db.Categories.FindAsync([e.CategoryId]);
                    if (x is null)
                    {
                        db.Categories.Add(
                            new CategoryReadModel
                            {
                                Id = e.CategoryId,
                                Code = e.Code,
                                Name = e.Name,
                                DefaultStationCode = e.DefaultStationCode,
                                DefaultStationName = e.DefaultStationName,
                                IsActive = e.IsActive,
                                Version = e.Version,
                                UpdatedAtUtc = e.OccurredAtUtc,
                            }
                        );
                    }
                    else if (x.Version < e.Version)
                    {
                        x.Code = e.Code;
                        x.Name = e.Name;
                        x.DefaultStationCode = e.DefaultStationCode;
                        x.DefaultStationName = e.DefaultStationName;
                        x.IsActive = e.IsActive;
                        x.Version = e.Version;
                        x.UpdatedAtUtc = e.OccurredAtUtc;
                    }
                    if (x is null || x.Version <= e.Version)
                    {
                        List<MenuItemReadModel> menuItems = await db
                            .MenuItems.Where(item => item.CategoryId == e.CategoryId)
                            .ToListAsync();
                        foreach (MenuItemReadModel menuItem in menuItems)
                        {
                            menuItem.CategoryCode = e.Code;
                            menuItem.CategoryName = e.Name;
                            menuItem.UpdatedAtUtc = e.OccurredAtUtc;
                        }
                    }
                }
                else if (type == typeof(ProductChanged).FullName)
                {
                    ProductChanged e = JsonSerializer.Deserialize<ProductChanged>(ea.Body.Span)!;
                    e = e with { OccurredAtUtc = EnsureUtc(e.OccurredAtUtc) };
                    ProductReadModel? x = await db.Products.FindAsync([e.ProductId]);
                    if (x is null)
                    {
                        db.Products.Add(
                            new ProductReadModel
                            {
                                Id = e.ProductId,
                                Sku = e.Sku,
                                Name = e.Name,
                                CategoryId = e.CategoryId,
                                BasePrice = e.BasePrice,
                                IsActive = e.IsActive,
                                Version = e.Version,
                                UpdatedAtUtc = e.OccurredAtUtc,
                            }
                        );
                    }
                    else if (x.Version < e.Version)
                    {
                        x.Sku = e.Sku;
                        x.Name = e.Name;
                        x.CategoryId = e.CategoryId;
                        x.BasePrice = e.BasePrice;
                        x.IsActive = e.IsActive;
                        x.Version = e.Version;
                        x.UpdatedAtUtc = e.OccurredAtUtc;
                    }
                    if (x is null || x.Version <= e.Version)
                    {
                        CategoryReadModel? category = await db.Categories.FindAsync([e.CategoryId]);
                        List<MenuItemReadModel> menuItems = await db
                            .MenuItems.Where(item => item.ProductId == e.ProductId)
                            .ToListAsync();
                        foreach (MenuItemReadModel menuItem in menuItems)
                        {
                            menuItem.Sku = e.Sku;
                            menuItem.ProductName = e.Name;
                            menuItem.CategoryId = e.CategoryId;
                            if (category is not null)
                            {
                                menuItem.CategoryCode = category.Code;
                                menuItem.CategoryName = category.Name;
                            }
                            menuItem.UpdatedAtUtc = e.OccurredAtUtc;
                        }
                    }
                }
                else if (type == typeof(CatalogItemChanged).FullName)
                {
                    CatalogItemChanged e = JsonSerializer.Deserialize<CatalogItemChanged>(
                        ea.Body.Span
                    )!;
                    e = e with { OccurredAtUtc = EnsureUtc(e.OccurredAtUtc) };
                    MenuItemReadModel? x = await db.MenuItems.SingleOrDefaultAsync(x =>
                        x.RestaurantId == e.RestaurantId && x.ProductId == e.ProductId
                    );
                    if (x is null)
                    {
                        db.MenuItems.Add(
                            new MenuItemReadModel
                            {
                                Id = Guid.NewGuid(),
                                RestaurantId = e.RestaurantId,
                                ProductId = e.ProductId,
                                Sku = e.Sku,
                                ProductName = e.ProductName,
                                CategoryId = e.CategoryId,
                                CategoryCode = e.CategoryCode,
                                CategoryName = e.CategoryName,
                                Price = e.Price,
                                IsAvailable = e.IsAvailable,
                                PreparationStationCode = e.PreparationStationCode,
                                PreparationStationName = e.PreparationStationName,
                                Version = e.Version,
                                UpdatedAtUtc = e.OccurredAtUtc,
                            }
                        );
                    }
                    else if (x.Version < e.Version)
                    {
                        x.Sku = e.Sku;
                        x.ProductName = e.ProductName;
                        x.CategoryId = e.CategoryId;
                        x.CategoryCode = e.CategoryCode;
                        x.CategoryName = e.CategoryName;
                        x.Price = e.Price;
                        x.IsAvailable = e.IsAvailable;
                        x.PreparationStationCode = e.PreparationStationCode;
                        x.PreparationStationName = e.PreparationStationName;
                        x.Version = e.Version;
                        x.UpdatedAtUtc = e.OccurredAtUtc;
                    }
                }
                else if (type == typeof(KitchenStationChanged).FullName)
                {
                    KitchenStationChanged e = JsonSerializer.Deserialize<KitchenStationChanged>(
                        ea.Body.Span
                    )!;
                    e = e with { OccurredAtUtc = EnsureUtc(e.OccurredAtUtc) };
                    KitchenStationReadModel? x = await db.KitchenStations.FindAsync([e.StationId]);
                    if (x is null)
                    {
                        db.KitchenStations.Add(
                            new KitchenStationReadModel
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
                    if (x is null || x.Version <= e.Version)
                    {
                        List<MenuItemReadModel> menuItems = await db
                            .MenuItems.Where(item =>
                                item.RestaurantId == e.RestaurantId
                                && item.PreparationStationCode == e.Code
                            )
                            .ToListAsync();
                        foreach (MenuItemReadModel menuItem in menuItems)
                        {
                            menuItem.PreparationStationName = e.Name;
                            menuItem.UpdatedAtUtc = e.OccurredAtUtc;
                        }
                    }
                }
                else
                {
                    throw new NotSupportedException(type);
                }

                db.InboxMessages.Add(
                    new InboxMessage
                    {
                        Id = messageId,
                        Type = type,
                        ProcessedAtUtc = time.GetUtcNow().UtcDateTime,
                    }
                );
                await db.SaveChangesAsync();
            }
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            log.LogError(e, "Invalid catalog message.");
            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (Exception e)
        {
            log.LogError(e, "Catalog message will be retried.");
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
