using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Payments.Contracts.Events;
using Restaurantes.Payments.Infrastructure.Persistence;
using Restaurantes.Payments.Infrastructure.Persistence.Write;

namespace Restaurantes.Payments.Publisher;

public sealed class PaymentOutboxPublisherWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
    ILogger<PaymentOutboxPublisherWorker> log
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await using (AsyncServiceScope s = scopes.CreateAsyncScope())
        {
            await s
                .ServiceProvider.GetRequiredService<PaymentWriteDbContext>()
                .Database.MigrateAsync(ct);
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                ConnectionFactory f = RabbitMqConnectionFactory.Create(options.Value);
                await using IConnection cn = await f.CreateConnectionAsync("payments-outbox", ct);
                await using IChannel ch = await cn.CreateChannelAsync(
                    new CreateChannelOptions(true, true),
                    ct
                );
                await PaymentsTopology.DeclareAsync(ch, ct);
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
                log.LogError(e, "Payments publisher disconnected; retrying.");
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task<bool> Publish(IChannel ch, CancellationToken ct)
    {
        await using AsyncServiceScope s = scopes.CreateAsyncScope();
        PaymentWriteDbContext db = s.ServiceProvider.GetRequiredService<PaymentWriteDbContext>();
        List<OutboxMessage> messages = await db
            .OutboxMessages.Where(x => x.ProcessedAtUtc == null)
            .OrderBy(x => x.OccurredAtUtc)
            .Take(50)
            .ToListAsync(ct);
        foreach (OutboxMessage m in messages)
        {
            string key =
                m.Type == typeof(PaymentCaptured).FullName
                    ? PaymentsTopology.PaymentCapturedRoutingKey
                : m.Type == typeof(PaymentRefunded).FullName
                    ? PaymentsTopology.PaymentRefundedRoutingKey
                : throw new NotSupportedException(m.Type);
            BasicProperties p = new()
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = m.Id.ToString(),
                Type = m.Type,
            };
            await ch.BasicPublishAsync(
                PaymentsTopology.ExchangeName,
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
