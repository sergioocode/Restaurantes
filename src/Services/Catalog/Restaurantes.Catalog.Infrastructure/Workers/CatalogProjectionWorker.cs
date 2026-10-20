using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Catalog.Contracts.Events;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.ServiceDefaults;

namespace Restaurantes.Catalog.Infrastructure.Workers;

public sealed class CatalogProjectionWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
    ILogger<CatalogProjectionWorker> log
) : AsyncDisposingBackgroundService
{
    private IConnection? connection;
    private IChannel? channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CancellationToken ct = stoppingToken;
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
                channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(true, true),
                    ct
                );
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
            if (!Guid.TryParse(ea.BasicProperties.MessageId, out Guid messageId))
            {
                throw new JsonException("Invalid MessageId");
            }

            await using AsyncServiceScope s = scopes.CreateAsyncScope();
            CatalogReadDbContext db = s.ServiceProvider.GetRequiredService<CatalogReadDbContext>();
            if (!await db.InboxMessages.AnyAsync(x => x.Id == messageId))
            {
                string type = ea.BasicProperties.Type ?? "";
                await ApplyEventAsync(db, type, ea.Body);
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
            RabbitMqRetryResult retry = await RabbitMqRetry.ScheduleOrDeadLetterAsync(
                channel,
                ea,
                CatalogTopology.ReadModelQueueName,
                e,
                ea.CancellationToken
            );
            log.LogError(
                e,
                "Catalog message failed. Retry scheduled: {RetryScheduled}; attempt {RetryAttempt}/{MaximumAttempts}; delay {RetryDelay}.",
                retry.Scheduled,
                retry.Attempt,
                RabbitMqRetry.MaximumAttempts,
                retry.Delay
            );
        }
    }

    internal static Task ApplyEventAsync(
        CatalogReadDbContext db,
        string type,
        ReadOnlyMemory<byte> body
    )
    {
        if (type == typeof(CategoryChanged).FullName)
            return ApplyCategoryChangedAsync(db, Deserialize<CategoryChanged>(body));
        if (type == typeof(ProductChanged).FullName)
            return ApplyProductChangedAsync(db, Deserialize<ProductChanged>(body));
        if (type == typeof(CatalogItemChanged).FullName)
            return ApplyCatalogItemChangedAsync(db, Deserialize<CatalogItemChanged>(body));
        if (type == typeof(KitchenStationChanged).FullName)
            return ApplyKitchenStationChangedAsync(db, Deserialize<KitchenStationChanged>(body));
        throw new NotSupportedException(type);
    }

    private static T Deserialize<T>(ReadOnlyMemory<byte> body)
    {
        return JsonSerializer.Deserialize<T>(body.Span)!;
    }

    private static async Task ApplyCategoryChangedAsync(
        CatalogReadDbContext db,
        CategoryChanged message
    )
    {
        CategoryChanged e = message with { OccurredAtUtc = EnsureUtc(message.OccurredAtUtc) };
        CategoryReadModel? category = await db.Categories.FindAsync([e.CategoryId]);
        if (category is null)
            db.Categories.Add(CreateCategoryReadModel(e));
        else if (category.Version < e.Version)
            UpdateCategory(category, e);

        if (category is null || category.Version <= e.Version)
            await UpdateCategoryMenuItemsAsync(db, e);
    }

    private static CategoryReadModel CreateCategoryReadModel(CategoryChanged e)
    {
        return new CategoryReadModel
        {
            Id = e.CategoryId,
            Code = e.Code,
            Name = e.Name,
            DefaultStationCode = e.DefaultStationCode,
            DefaultStationName = e.DefaultStationName,
            RequiresKds = e.RequiresKds,
            IsActive = e.IsActive,
            ShowInPos = e.ShowInPos,
            ShowInCommander = e.ShowInCommander,
            ShowInCustomerQr = e.ShowInCustomerQr,
            Version = e.Version,
            UpdatedAtUtc = e.OccurredAtUtc,
        };
    }

    private static void UpdateCategory(CategoryReadModel category, CategoryChanged e)
    {
        category.Code = e.Code;
        category.Name = e.Name;
        category.DefaultStationCode = e.DefaultStationCode;
        category.DefaultStationName = e.DefaultStationName;
        category.RequiresKds = e.RequiresKds;
        category.IsActive = e.IsActive;
        category.ShowInPos = e.ShowInPos;
        category.ShowInCommander = e.ShowInCommander;
        category.ShowInCustomerQr = e.ShowInCustomerQr;
        category.Version = e.Version;
        category.UpdatedAtUtc = e.OccurredAtUtc;
    }

    private static async Task UpdateCategoryMenuItemsAsync(
        CatalogReadDbContext db,
        CategoryChanged e
    )
    {
        List<MenuItemReadModel> items = await db
            .MenuItems.Where(item => item.CategoryId == e.CategoryId)
            .ToListAsync();
        foreach (MenuItemReadModel item in items)
        {
            item.CategoryCode = e.Code;
            item.CategoryName = e.Name;
            item.RequiresKds = e.RequiresKds;
            item.UpdatedAtUtc = e.OccurredAtUtc;
        }
    }

    private static async Task ApplyProductChangedAsync(
        CatalogReadDbContext db,
        ProductChanged message
    )
    {
        ProductChanged e = message with { OccurredAtUtc = EnsureUtc(message.OccurredAtUtc) };
        ProductReadModel? product = await db.Products.FindAsync([e.ProductId]);
        if (product is null)
            db.Products.Add(CreateProductReadModel(e));
        else if (product.Version < e.Version)
            UpdateProduct(product, e);

        if (product is null || product.Version <= e.Version)
            await UpdateProductMenuItemsAsync(db, e);
    }

    private static ProductReadModel CreateProductReadModel(ProductChanged e)
    {
        return new ProductReadModel
        {
            Id = e.ProductId,
            Sku = e.Sku,
            Name = e.Name,
            CategoryId = e.CategoryId,
            IsActive = e.IsActive,
            Version = e.Version,
            UpdatedAtUtc = e.OccurredAtUtc,
        };
    }

    private static void UpdateProduct(ProductReadModel product, ProductChanged e)
    {
        product.Sku = e.Sku;
        product.Name = e.Name;
        product.CategoryId = e.CategoryId;
        product.IsActive = e.IsActive;
        product.Version = e.Version;
        product.UpdatedAtUtc = e.OccurredAtUtc;
    }

    private static async Task UpdateProductMenuItemsAsync(CatalogReadDbContext db, ProductChanged e)
    {
        CategoryReadModel? category = await db.Categories.FindAsync([e.CategoryId]);
        List<MenuItemReadModel> items = await db
            .MenuItems.Where(item => item.ProductId == e.ProductId)
            .ToListAsync();
        foreach (MenuItemReadModel item in items)
        {
            item.Sku = e.Sku;
            item.ProductName = e.Name;
            item.CategoryId = e.CategoryId;
            if (category is not null)
            {
                item.CategoryCode = category.Code;
                item.CategoryName = category.Name;
            }
            item.UpdatedAtUtc = e.OccurredAtUtc;
        }
    }

    private static async Task ApplyCatalogItemChangedAsync(
        CatalogReadDbContext db,
        CatalogItemChanged message
    )
    {
        CatalogItemChanged e = message with { OccurredAtUtc = EnsureUtc(message.OccurredAtUtc) };
        MenuItemReadModel? item = await db.MenuItems.SingleOrDefaultAsync(x =>
            x.RestaurantId == e.RestaurantId && x.ProductId == e.ProductId
        );
        if (item is null)
            db.MenuItems.Add(CreateMenuItemReadModel(e));
        else if (item.Version < e.Version)
            UpdateMenuItem(item, e);
    }

    private static MenuItemReadModel CreateMenuItemReadModel(CatalogItemChanged e)
    {
        return new MenuItemReadModel
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
            RequiresKds = e.RequiresKds,
            Version = e.Version,
            UpdatedAtUtc = e.OccurredAtUtc,
        };
    }

    private static void UpdateMenuItem(MenuItemReadModel item, CatalogItemChanged e)
    {
        item.Sku = e.Sku;
        item.ProductName = e.ProductName;
        item.CategoryId = e.CategoryId;
        item.CategoryCode = e.CategoryCode;
        item.CategoryName = e.CategoryName;
        item.Price = e.Price;
        item.IsAvailable = e.IsAvailable;
        item.PreparationStationCode = e.PreparationStationCode;
        item.PreparationStationName = e.PreparationStationName;
        item.RequiresKds = e.RequiresKds;
        item.Version = e.Version;
        item.UpdatedAtUtc = e.OccurredAtUtc;
    }

    private static async Task ApplyKitchenStationChangedAsync(
        CatalogReadDbContext db,
        KitchenStationChanged message
    )
    {
        KitchenStationChanged e = message with { OccurredAtUtc = EnsureUtc(message.OccurredAtUtc) };
        KitchenStationReadModel? station = await db.KitchenStations.FindAsync([e.StationId]);
        ApplyKitchenStationState(db, e, station);
        if (!e.IsDeleted && (station is null || station.Version <= e.Version))
            await UpdateKitchenStationMenuItemsAsync(db, e);
    }

    private static void ApplyKitchenStationState(
        CatalogReadDbContext db,
        KitchenStationChanged e,
        KitchenStationReadModel? station
    )
    {
        if (e.IsDeleted)
        {
            if (station is not null && station.Version < e.Version)
                db.KitchenStations.Remove(station);
            return;
        }
        if (station is null)
            db.KitchenStations.Add(CreateKitchenStationReadModel(e));
        else if (station.Version < e.Version)
            UpdateKitchenStation(station, e);
    }

    private static KitchenStationReadModel CreateKitchenStationReadModel(KitchenStationChanged e)
    {
        return new KitchenStationReadModel
        {
            Id = e.StationId,
            RestaurantId = e.RestaurantId,
            Code = e.Code,
            Name = e.Name,
            IsPrimary = e.IsPrimary,
            RequiresPrimaryDispatch = e.RequiresPrimaryDispatch,
            PrintTicket = e.PrintTicket,
            Priority = e.Priority,
            IsActive = e.IsActive,
            Version = e.Version,
            UpdatedAtUtc = e.OccurredAtUtc,
        };
    }

    private static void UpdateKitchenStation(
        KitchenStationReadModel station,
        KitchenStationChanged e
    )
    {
        station.Name = e.Name;
        station.IsPrimary = e.IsPrimary;
        station.RequiresPrimaryDispatch = e.RequiresPrimaryDispatch;
        station.PrintTicket = e.PrintTicket;
        station.Priority = e.Priority;
        station.IsActive = e.IsActive;
        station.Version = e.Version;
        station.UpdatedAtUtc = e.OccurredAtUtc;
    }

    private static async Task UpdateKitchenStationMenuItemsAsync(
        CatalogReadDbContext db,
        KitchenStationChanged e
    )
    {
        List<MenuItemReadModel> items = await db
            .MenuItems.Where(item =>
                item.RestaurantId == e.RestaurantId && item.PreparationStationCode == e.Code
            )
            .ToListAsync();
        foreach (MenuItemReadModel item in items)
        {
            item.PreparationStationName = e.Name;
            item.UpdatedAtUtc = e.OccurredAtUtc;
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
