using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Contracts;
using Restaurantes.Payments.Contracts;

namespace Restaurantes.Dining.Api.Write;

public sealed class DiningIntegrationWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
    ILogger<DiningIntegrationWorker> log
) : BackgroundService
{
    private IConnection? connection;
    private IChannel? channel;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                ConnectionFactory factory = RabbitMqConnectionFactory.Create(options.Value);
                connection = await factory.CreateConnectionAsync("dining-table-sessions", ct);
                channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await DiningTopology.DeclareAsync(channel, ct);
                AsyncEventingBasicConsumer consumer = new(channel);
                consumer.ReceivedAsync += Handle;
                await channel.BasicConsumeAsync(DiningTopology.QueueName, false, consumer, ct);
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                log.LogError(e, "Dining integration disconnected; retrying.");
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
            if (!Guid.TryParse(ea.BasicProperties.MessageId, out Guid messageId))
            {
                throw new JsonException("MessageId is invalid.");
            }

            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            DiningDbContext db = scope.ServiceProvider.GetRequiredService<DiningDbContext>();
            if (await db.InboxMessages.AnyAsync(x => x.Id == messageId))
            {
                await channel.BasicAckAsync(ea.DeliveryTag, false);
                return;
            }

            string type = ea.BasicProperties.Type ?? string.Empty;
            if (type == typeof(OrderCreated).FullName)
            {
                await ApplyOrderCreated(db, ea.Body);
            }
            else if (type == typeof(OrderSubmitted).FullName)
            {
                await ApplyOrderSubmitted(db, ea.Body);
            }
            else if (type == typeof(OrderDelivered).FullName)
            {
                await ApplyOrderDelivered(db, ea.Body);
            }
            else if (type == typeof(OrderReady).FullName)
            {
                await ApplyOrderReady(db, ea.Body);
            }
            else if (type == typeof(OrderLineCancelled).FullName)
            {
                await ApplyOrderLineCancelled(db, ea.Body);
            }
            else if (type == typeof(PaymentCaptured).FullName)
            {
                await ApplyPayment(db, ea.Body, "Paid");
            }
            else if (type == typeof(PaymentRefunded).FullName)
            {
                await ApplyPayment(db, ea.Body, "Refunded");
            }
            else
            {
                throw new JsonException($"Unsupported integration event '{type}'.");
            }

            db.InboxMessages.Add(
                new DiningInboxMessage
                {
                    Id = messageId,
                    ProcessedAtUtc = time.GetUtcNow().UtcDateTime,
                }
            );
            await db.SaveChangesAsync();
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            log.LogError(e, "Invalid Dining integration event.");
            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (Exception e)
        {
            log.LogError(e, "Dining integration event will retry.");
            await channel.BasicNackAsync(ea.DeliveryTag, false, true);
        }
    }

    private static async Task ApplyOrderCreated(DiningDbContext db, ReadOnlyMemory<byte> body)
    {
        OrderCreated e =
            JsonSerializer.Deserialize<OrderCreated>(body.Span)
            ?? throw new JsonException("OrderCreated is empty.");
        if (e.ServiceMode == "Takeaway" || e.DiningSessionId is null)
        {
            return;
        }

        DiningSession? session = await db
            .Sessions.Include(x => x.Orders)
            .SingleOrDefaultAsync(x => x.Id == e.DiningSessionId.Value);
        if (session is null)
        {
            throw new JsonException($"Dining session '{e.DiningSessionId}' does not exist.");
        }

        if (
            session.RestaurantId != e.RestaurantId
            || session.TableId != e.TableId
            || session.Status != "Open"
        )
        {
            throw new JsonException("Order does not match an open Dining session.");
        }

        if (await db.SessionOrders.AnyAsync(x => x.OrderId == e.OrderId))
        {
            return;
        }

        DiningPendingPayment? pending = await db.PendingPayments.SingleOrDefaultAsync(x =>
            x.OrderId == e.OrderId
        );
        DiningSessionOrder projectedOrder = new()
        {
            OrderId = e.OrderId,
            DiningSessionId = e.DiningSessionId.Value,
            PaymentStatus = pending?.Status ?? "Unpaid",
            OrderStatus = "Draft",
            Amount = e.Lines.Sum(x => x.UnitPrice * x.Quantity),
            AddedAtUtc = e.OccurredAtUtc,
            UpdatedAtUtc = pending?.UpdatedAtUtc ?? e.OccurredAtUtc,
        };
        db.SessionOrders.Add(projectedOrder);
        if (pending is not null)
        {
            db.PendingPayments.Remove(pending);
        }
    }

    private static async Task ApplyOrderDelivered(DiningDbContext db, ReadOnlyMemory<byte> body)
    {
        OrderDelivered e =
            JsonSerializer.Deserialize<OrderDelivered>(body.Span)
            ?? throw new JsonException("OrderDelivered is empty.");
        if (e.ServiceMode == "Takeaway")
        {
            return;
        }
        DiningSessionOrder? order = await db.SessionOrders.SingleOrDefaultAsync(x =>
            x.OrderId == e.OrderId
        );
        if (order is null)
        {
            throw new JsonException($"Dining order '{e.OrderId}' does not exist.");
        }

        order.OrderStatus = "Delivered";
        order.Amount = e
            .Lines.Where(x => x.Status != "Cancelled")
            .Sum(x => x.UnitPrice * x.Quantity);
        order.UpdatedAtUtc = e.DeliveredAtUtc;
    }

    private static async Task ApplyOrderReady(DiningDbContext db, ReadOnlyMemory<byte> body)
    {
        OrderReady e =
            JsonSerializer.Deserialize<OrderReady>(body.Span)
            ?? throw new JsonException("OrderReady is empty.");
        if (e.ServiceMode == "Takeaway")
        {
            return;
        }
        DiningSessionOrder? order = await db.SessionOrders.SingleOrDefaultAsync(x =>
            x.OrderId == e.OrderId
        );
        if (order is null)
        {
            throw new JsonException($"Dining order '{e.OrderId}' does not exist.");
        }
        order.OrderStatus = "Ready";
        order.Amount = e
            .Lines.Where(x => x.Status != "Cancelled")
            .Sum(x => x.UnitPrice * x.Quantity);
        order.UpdatedAtUtc = e.ReadyAtUtc;
    }

    private static async Task ApplyOrderSubmitted(DiningDbContext db, ReadOnlyMemory<byte> body)
    {
        OrderSubmitted e =
            JsonSerializer.Deserialize<OrderSubmitted>(body.Span)
            ?? throw new JsonException("OrderSubmitted is empty.");
        if (e.ServiceMode == "Takeaway")
        {
            return;
        }

        DiningSessionOrder order =
            await db.SessionOrders.SingleOrDefaultAsync(x => x.OrderId == e.OrderId)
            ?? throw new JsonException($"Dining order ''{e.OrderId}'' does not exist.");
        order.OrderStatus = "Submitted";
        order.Amount = e
            .Lines.Where(x => x.Status != "Cancelled")
            .Sum(x => x.UnitPrice * x.Quantity);
        order.UpdatedAtUtc = e.SubmittedAtUtc;
    }

    private static async Task ApplyOrderLineCancelled(DiningDbContext db, ReadOnlyMemory<byte> body)
    {
        OrderLineCancelled e =
            JsonSerializer.Deserialize<OrderLineCancelled>(body.Span)
            ?? throw new JsonException("OrderLineCancelled is empty.");
        if (e.ServiceMode == "Takeaway" || e.DiningSessionId is null)
        {
            return;
        }
        DiningSessionOrder order =
            await db.SessionOrders.SingleOrDefaultAsync(x => x.OrderId == e.OrderId)
            ?? throw new JsonException($"Dining order ''{e.OrderId}'' does not exist.");
        order.OrderStatus = e.Status;
        order.Amount = e.Total;
        order.UpdatedAtUtc = e.CancelledAtUtc;
    }

    private static async Task ApplyPayment(
        DiningDbContext db,
        ReadOnlyMemory<byte> body,
        string status
    )
    {
        Guid orderId;
        DateTime occurredAtUtc;
        if (status == "Paid")
        {
            PaymentCaptured e =
                JsonSerializer.Deserialize<PaymentCaptured>(body.Span)
                ?? throw new JsonException("PaymentCaptured is empty.");
            if (e.ServiceMode == "Takeaway")
            {
                return;
            }
            orderId = e.OrderId;
            occurredAtUtc = e.CapturedAtUtc;
        }
        else
        {
            PaymentRefunded e =
                JsonSerializer.Deserialize<PaymentRefunded>(body.Span)
                ?? throw new JsonException("PaymentRefunded is empty.");
            if (e.ServiceMode == "Takeaway")
            {
                return;
            }
            orderId = e.OrderId;
            occurredAtUtc = e.RefundedAtUtc;
        }

        DiningSessionOrder? order = await db.SessionOrders.SingleOrDefaultAsync(x =>
            x.OrderId == orderId
        );
        if (order is not null)
        {
            order.PaymentStatus = status;
            order.UpdatedAtUtc = occurredAtUtc;
            return;
        }
        DiningPendingPayment? pending = await db.PendingPayments.SingleOrDefaultAsync(x =>
            x.OrderId == orderId
        );
        if (pending is null)
        {
            db.PendingPayments.Add(
                new DiningPendingPayment
                {
                    OrderId = orderId,
                    Status = status,
                    UpdatedAtUtc = occurredAtUtc,
                }
            );
        }
        else
        {
            pending.Status = status;
            pending.UpdatedAtUtc = occurredAtUtc;
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
