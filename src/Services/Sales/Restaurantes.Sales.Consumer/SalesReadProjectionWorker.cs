using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Sales.Contracts;
using Restaurantes.Sales.Infrastructure.Persistence;
using Restaurantes.Sales.Infrastructure.Persistence.Read;

namespace Restaurantes.Sales.Consumer;

public sealed class SalesReadProjectionWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
    ILogger<SalesReadProjectionWorker> log
) : BackgroundService
{
    private IConnection? connection;
    private IChannel? channel;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await using (AsyncServiceScope s = scopes.CreateAsyncScope())
        {
            await s
                .ServiceProvider.GetRequiredService<SaleReadDbContext>()
                .Database.MigrateAsync(ct);
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                connection = await RabbitMqConnectionFactory
                    .Create(options.Value)
                    .CreateConnectionAsync("sales-read-model", ct);
                channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await SalesTopology.DeclareAsync(channel, ct);
                AsyncEventingBasicConsumer consumer = new(channel);
                consumer.ReceivedAsync += Handle;
                await channel.BasicConsumeAsync(
                    SalesTopology.ReadModelQueueName,
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
                log.LogError(e, "Sales read projector disconnected; retrying.");
                await DisposeBroker();
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
            if (
                !Guid.TryParse(ea.BasicProperties.MessageId, out Guid messageId)
                || ea.BasicProperties.Type != typeof(SaleCompleted).FullName
            )
            {
                throw new JsonException("Invalid SaleCompleted message");
            }

            SaleCompleted e =
                JsonSerializer.Deserialize<SaleCompleted>(ea.Body.Span)
                ?? throw new JsonException("Empty sale");
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            SaleReadDbContext db = scope.ServiceProvider.GetRequiredService<SaleReadDbContext>();
            if (!await db.InboxMessages.AnyAsync(x => x.Id == messageId))
            {
                SaleReadModel? view = await db.Sales.FindAsync([e.SaleId]);
                if (view is null)
                {
                    view = new SaleReadModel { Id = e.SaleId };
                    db.Sales.Add(view);
                }
                view.RestaurantId = e.RestaurantId;
                view.Source = e.Source;
                view.PaymentMethod = e.PaymentMethod;
                view.Total = e.Total;
                view.Status = "Completed";
                view.OrderCount = e.OrderIds.Count;
                view.CompletedAtUtc = e.CompletedAtUtc;
                view.OrderIdsJson = JsonSerializer.Serialize(e.OrderIds);
                view.LinesJson = JsonSerializer.Serialize(e.Lines);
                db.InboxMessages.Add(
                    new InboxMessage
                    {
                        Id = messageId,
                        ProcessedAtUtc = time.GetUtcNow().UtcDateTime,
                    }
                );
                await db.SaveChangesAsync();
            }
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (JsonException e)
        {
            log.LogError(e, "Invalid sales read event");
            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (Exception e)
        {
            log.LogError(e, "Sales read event will retry");
            await channel.BasicNackAsync(ea.DeliveryTag, false, true);
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        await DisposeBroker();
    }

    private async Task DisposeBroker()
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
