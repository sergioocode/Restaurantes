using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Contracts.Events;
using Restaurantes.Payments.Domain;
using Restaurantes.Payments.Infrastructure.Persistence;
using Restaurantes.Payments.Infrastructure.Persistence.Write;

namespace Restaurantes.Payments.Consumer;

public sealed class PayableOrderProjectionWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
    ILogger<PayableOrderProjectionWorker> log
) : BackgroundService
{
    private IConnection? connection;
    private IChannel? channel;

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
                connection = await f.CreateConnectionAsync("payments-payable-orders", ct);
                channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await PaymentsTopology.DeclareAsync(channel, ct);
                AsyncEventingBasicConsumer consumer = new(channel);
                consumer.ReceivedAsync += Handle;
                await channel.BasicConsumeAsync(
                    PaymentsTopology.PayableOrdersQueueName,
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
                log.LogError(e, "Payments order projection disconnected; retrying.");
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
            if (!Guid.TryParse(ea.BasicProperties.MessageId, out Guid id))
            {
                throw new JsonException("Invalid order event");
            }
            await using AsyncServiceScope s = scopes.CreateAsyncScope();
            PaymentWriteDbContext db =
                s.ServiceProvider.GetRequiredService<PaymentWriteDbContext>();
            if (!await db.InboxMessages.AnyAsync(x => x.Id == id))
            {
                if (ea.BasicProperties.Type == typeof(OrderCreated).FullName)
                {
                    OrderCreated e =
                        JsonSerializer.Deserialize<OrderCreated>(ea.Body.Span)
                        ?? throw new JsonException("Empty order");
                    if (!await db.PayableOrders.AnyAsync(x => x.OrderId == e.OrderId))
                    {
                        db.PayableOrders.Add(
                            PayableOrder.Create(
                                e.OrderId,
                                e.RestaurantId,
                                e.TableId,
                                e.DiningSessionId,
                                e.ServiceMode,
                                e.Source,
                                e.Lines.Sum(x => x.UnitPrice * x.Quantity),
                                e.OccurredAtUtc
                            )
                        );
                    }
                }
                else if (ea.BasicProperties.Type == typeof(OrderLineCancelled).FullName)
                {
                    OrderLineCancelled e =
                        JsonSerializer.Deserialize<OrderLineCancelled>(ea.Body.Span)
                        ?? throw new JsonException("Empty cancellation");
                    PayableOrder payable =
                        await db.PayableOrders.SingleOrDefaultAsync(x => x.OrderId == e.OrderId)
                        ?? throw new JsonException($"Payable order ''{e.OrderId}'' is missing.");
                    payable.AdjustAmount(e.Total);
                }
                else
                {
                    throw new NotSupportedException(ea.BasicProperties.Type);
                }

                db.InboxMessages.Add(
                    new InboxMessage { Id = id, ProcessedAtUtc = time.GetUtcNow().UtcDateTime }
                );
                await db.SaveChangesAsync();
            }
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            log.LogError(e, "Invalid payable order event");
            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (Exception e)
        {
            log.LogError(e, "Payable order event will retry");
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
