using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Payments.Contracts;

namespace Restaurantes.CashRegister.Api;

public sealed class CashPaymentProjectionWorker(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
    ILogger<CashPaymentProjectionWorker> log
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
                connection = await RabbitMqConnectionFactory
                    .Create(options.Value)
                    .CreateConnectionAsync("cash-register", ct);
                channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await PaymentsTopology.DeclareAsync(channel, ct);
                AsyncEventingBasicConsumer consumer = new(channel);
                consumer.ReceivedAsync += Handle;
                await channel.BasicConsumeAsync(
                    PaymentsTopology.CashRegisterQueueName,
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
            catch (Exception ex)
            {
                log.LogError(ex, "CashRegister consumer disconnected; retrying.");
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
                throw new JsonException("Invalid payment event id.");
            }

            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            CashRegisterDbContext db =
                scope.ServiceProvider.GetRequiredService<CashRegisterDbContext>();
            if (await db.InboxMessages.AnyAsync(x => x.Id == messageId))
            {
                await channel.BasicAckAsync(ea.DeliveryTag, false);
                return;
            }
            PaymentData payment = ReadPayment(ea);
            Guid? referencedSessionId = ParseSessionId(payment.Reference);
            if (payment.Type == CashMovementType.Refund)
            {
                referencedSessionId = await db
                    .Movements.AsNoTracking()
                    .Where(x => x.PaymentId == payment.PaymentId && x.Type == CashMovementType.Sale)
                    .Select(x => (Guid?)x.CashRegisterSessionId)
                    .SingleOrDefaultAsync();
            }
            CashRegisterSession? session = referencedSessionId.HasValue
                ? await db
                    .Sessions.Include(x => x.Movements)
                    .Include(x => x.Reconciliations)
                    .SingleOrDefaultAsync(x =>
                        x.Id == referencedSessionId && x.RestaurantId == payment.RestaurantId
                    )
                : await db
                    .Sessions.Include(x => x.Movements)
                    .Include(x => x.Reconciliations)
                    .Where(x =>
                        x.RestaurantId == payment.RestaurantId
                        && x.OpenedAtUtc <= payment.OccurredAtUtc
                        && (x.ClosedAtUtc == null || x.ClosedAtUtc >= payment.OccurredAtUtc)
                    )
                    .OrderByDescending(x => x.OpenedAtUtc)
                    .FirstOrDefaultAsync();
            if (session is null)
            {
                throw new InvalidOperationException(
                    $"No cash-register shift covers payment {payment.PaymentId} for restaurant {payment.RestaurantId}."
                );
            }

            db.Movements.Add(
                new CashMovement
                {
                    Id = messageId,
                    CashRegisterSessionId = session.Id,
                    PaymentId = payment.PaymentId,
                    OrderId = payment.OrderId,
                    Type = payment.Type,
                    Method = payment.Method,
                    Amount = payment.Amount,
                    Reference = payment.Reference,
                    OccurredAtUtc = payment.OccurredAtUtc,
                }
            );
            if (session.Status == CashRegisterStatus.Closed)
            {
                decimal signed =
                    payment.Type == CashMovementType.Sale ? payment.Amount : -payment.Amount;
                CashRegisterReconciliation? reconciliation = session.Reconciliations.FirstOrDefault(
                    x => string.Equals(x.Method, payment.Method, StringComparison.OrdinalIgnoreCase)
                );
                if (reconciliation is null)
                {
                    reconciliation = new CashRegisterReconciliation
                    {
                        Id = Guid.NewGuid(),
                        CashRegisterSessionId = session.Id,
                        Method = payment.Method,
                        ReconciledAmount = 0,
                    };
                    db.Reconciliations.Add(reconciliation);
                }
                reconciliation.ExpectedAmount += signed;
                reconciliation.Difference =
                    reconciliation.ReconciledAmount - reconciliation.ExpectedAmount;
                session.ExpectedTotalAtClose = (session.ExpectedTotalAtClose ?? 0) + signed;
                session.DifferenceAtClose =
                    (session.ReconciledTotalAtClose ?? 0) - session.ExpectedTotalAtClose;
                if (payment.Method == "Cash")
                {
                    session.ExpectedCashAtClose =
                        (session.ExpectedCashAtClose ?? session.OpeningFloat) + signed;
                }

                session.Version++;
            }
            db.InboxMessages.Add(
                new CashRegisterInboxMessage
                {
                    Id = messageId,
                    ProcessedAtUtc = time.GetUtcNow().UtcDateTime,
                }
            );
            await db.SaveChangesAsync();
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            log.LogError(ex, "Invalid cash-register event.");
            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Cash-register event will retry.");
            await channel.BasicNackAsync(ea.DeliveryTag, false, true);
        }
    }

    private static PaymentData ReadPayment(BasicDeliverEventArgs ea)
    {
        if (ea.BasicProperties.Type == typeof(PaymentCaptured).FullName)
        {
            PaymentCaptured e =
                JsonSerializer.Deserialize<PaymentCaptured>(ea.Body.Span)
                ?? throw new JsonException("Empty payment.");
            return new(
                e.PaymentId,
                e.OrderId,
                e.RestaurantId,
                e.Amount,
                e.Method,
                e.ExternalReference,
                e.CapturedAtUtc,
                CashMovementType.Sale
            );
        }
        if (ea.BasicProperties.Type == typeof(PaymentRefunded).FullName)
        {
            PaymentRefunded e =
                JsonSerializer.Deserialize<PaymentRefunded>(ea.Body.Span)
                ?? throw new JsonException("Empty refund.");
            return new(
                e.PaymentId,
                e.OrderId,
                e.RestaurantId,
                e.Amount,
                e.Method,
                e.Reason,
                e.RefundedAtUtc,
                CashMovementType.Refund
            );
        }
        throw new NotSupportedException(ea.BasicProperties.Type);
    }

    private static Guid? ParseSessionId(string reference)
    {
        const string prefix = "CASHREGISTER:";
        if (!reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int end = reference.IndexOf(';', prefix.Length);
        string value = end < 0 ? reference[prefix.Length..] : reference[prefix.Length..end];
        return Guid.TryParse(value, out Guid id) ? id : null;
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

    private sealed record PaymentData(
        Guid PaymentId,
        Guid OrderId,
        Guid RestaurantId,
        decimal Amount,
        string Method,
        string Reference,
        DateTime OccurredAtUtc,
        CashMovementType Type
    );
}
