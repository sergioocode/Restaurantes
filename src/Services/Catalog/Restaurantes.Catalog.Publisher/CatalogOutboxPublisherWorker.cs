using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Restaurantes.Catalog.Contracts;
using Restaurantes.Catalog.Infrastructure.Persistence;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Messaging.RabbitMq;

namespace Restaurantes.Catalog.Publisher;

public sealed class CatalogOutboxPublisherWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
    ILogger<CatalogOutboxPublisherWorker> log
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await using (AsyncServiceScope s = scopes.CreateAsyncScope())
        {
            await s
                .ServiceProvider.GetRequiredService<CatalogWriteDbContext>()
                .Database.MigrateAsync(ct);
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                ConnectionFactory f = RabbitMqConnectionFactory.Create(options.Value);
                await using IConnection cn = await f.CreateConnectionAsync("catalog-outbox", ct);
                await using IChannel ch = await cn.CreateChannelAsync(
                    new CreateChannelOptions(true, true),
                    ct
                );
                await CatalogTopology.DeclareAsync(ch, ct);
                while (cn.IsOpen && ch.IsOpen && !ct.IsCancellationRequested)
                {
                    bool any = await Publish(ch, ct);
                    await Task.Delay(any ? 100 : 1000, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                log.LogError(e, "Catalog publisher disconnected; retrying.");
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task<bool> Publish(IChannel ch, CancellationToken ct)
    {
        await using AsyncServiceScope s = scopes.CreateAsyncScope();
        CatalogWriteDbContext db = s.ServiceProvider.GetRequiredService<CatalogWriteDbContext>();
        List<OutboxMessage> messages = await db
            .OutboxMessages.Where(x => x.ProcessedAtUtc == null)
            .OrderBy(x => x.OccurredAtUtc)
            .Take(50)
            .ToListAsync(ct);
        foreach (OutboxMessage m in messages)
        {
            string key = m.Type switch
            {
                var x when x == typeof(CategoryChanged).FullName =>
                    CatalogTopology.CategoryChangedRoutingKey,
                var x when x == typeof(ProductChanged).FullName =>
                    CatalogTopology.ProductChangedRoutingKey,
                var x when x == typeof(CatalogItemChanged).FullName =>
                    CatalogTopology.CatalogItemChangedRoutingKey,
                var x when x == typeof(KitchenStationChanged).FullName =>
                    CatalogTopology.KitchenStationChangedRoutingKey,
                _ => throw new NotSupportedException(m.Type),
            };
            BasicProperties p = new()
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = m.Id.ToString(),
                Type = m.Type,
            };
            await ch.BasicPublishAsync(
                CatalogTopology.ExchangeName,
                key,
                true,
                p,
                Encoding.UTF8.GetBytes(m.Payload),
                ct
            );
            m.ProcessedAtUtc = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
        return messages.Count > 0;
    }
}
