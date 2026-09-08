using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Sales.Contracts;
using Restaurantes.Sales.Infrastructure.Persistence;
using Restaurantes.Sales.Infrastructure.Persistence.Write;

namespace Restaurantes.Sales.Publisher;

public sealed class SalesOutboxPublisherWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
    ILogger<SalesOutboxPublisherWorker> log
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await using (AsyncServiceScope s = scopes.CreateAsyncScope())
        {
            await s
                .ServiceProvider.GetRequiredService<SaleWriteDbContext>()
                .Database.MigrateAsync(ct);
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using IConnection cn = await RabbitMqConnectionFactory
                    .Create(options.Value)
                    .CreateConnectionAsync("sales-outbox", ct);
                await using IChannel ch = await cn.CreateChannelAsync(
                    new CreateChannelOptions(true, true),
                    ct
                );
                await SalesTopology.DeclareAsync(ch, ct);
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
                log.LogError(e, "Sales publisher disconnected; retrying.");
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task<bool> Publish(IChannel ch, CancellationToken ct)
    {
        await using AsyncServiceScope s = scopes.CreateAsyncScope();
        SaleWriteDbContext db = s.ServiceProvider.GetRequiredService<SaleWriteDbContext>();
        List<OutboxMessage> messages = await db
            .OutboxMessages.Where(x => x.ProcessedAtUtc == null)
            .OrderBy(x => x.OccurredAtUtc)
            .Take(50)
            .ToListAsync(ct);
        foreach (OutboxMessage m in messages)
        {
            if (m.Type != typeof(SaleCompleted).FullName)
            {
                throw new NotSupportedException(m.Type);
            }

            BasicProperties p = new()
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = m.Id.ToString(),
                Type = m.Type,
            };
            await ch.BasicPublishAsync(
                SalesTopology.ExchangeName,
                SalesTopology.SaleCompletedRoutingKey,
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
