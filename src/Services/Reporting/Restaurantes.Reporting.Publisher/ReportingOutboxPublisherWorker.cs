using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Reporting.Contracts;
using Restaurantes.Reporting.Infrastructure.Persistence;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;

namespace Restaurantes.Reporting.Publisher;

public sealed class ReportingOutboxPublisherWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
    ILogger<ReportingOutboxPublisherWorker> log
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<ReportingReadDbContext>()
                .Database.MigrateAsync(ct);
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                ConnectionFactory factory = RabbitMqConnectionFactory.Create(options.Value);
                await using IConnection connection = await factory.CreateConnectionAsync(
                    "reporting-outbox",
                    ct
                );
                await using IChannel channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(true, true),
                    ct
                );
                await ReportingTopology.DeclareAsync(channel, ct);
                while (connection.IsOpen && channel.IsOpen && !ct.IsCancellationRequested)
                {
                    bool any = await Publish(channel, ct);
                    await Task.Delay(any ? 100 : 1000, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                log.LogError(e, "Reporting publisher disconnected; retrying.");
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task<bool> Publish(IChannel channel, CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ReportingReadDbContext db =
            scope.ServiceProvider.GetRequiredService<ReportingReadDbContext>();
        List<ReportingOutboxMessage> messages = await db
            .OutboxMessages.Where(x => x.ProcessedAtUtc == null)
            .OrderBy(x => x.OccurredAtUtc)
            .Take(50)
            .ToListAsync(ct);
        foreach (ReportingOutboxMessage message in messages)
        {
            if (message.Type != typeof(DashboardProjectionUpdated).FullName)
            {
                throw new NotSupportedException(message.Type);
            }

            BasicProperties properties = new()
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = message.Id.ToString(),
                Type = message.Type,
            };
            await channel.BasicPublishAsync(
                ReportingTopology.ExchangeName,
                ReportingTopology.DashboardProjectionUpdatedRoutingKey,
                true,
                properties,
                Encoding.UTF8.GetBytes(message.Payload),
                ct
            );
            message.ProcessedAtUtc = time.GetUtcNow().UtcDateTime;
            message.Error = null;
            await db.SaveChangesAsync(ct);
        }
        return messages.Count > 0;
    }
}
