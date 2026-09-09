using Restaurantes.CashRegister.Domain;

namespace Restaurantes.CashRegister.Application;

public sealed class CashPaymentProjectionService(ICashRegisterStore store, TimeProvider time)
{
    public async Task Project(PaymentProjection payment, CancellationToken ct)
    {
        if (await store.HasProcessedMessageAsync(payment.MessageId, ct))
            return;
        Guid? sessionId = ParseSessionId(payment.Reference);
        if (payment.IsRefund)
            sessionId = await store.GetSaleSessionIdAsync(payment.PaymentId, ct);
        CashRegisterSession? session = await store.FindProjectionSessionAsync(
            payment,
            sessionId,
            ct
        );
        if (session is null)
            throw new InvalidOperationException(
                $"No cash-register shift covers payment {payment.PaymentId} for restaurant {payment.RestaurantId}."
            );

        CashMovementType type = payment.IsRefund ? CashMovementType.Refund : CashMovementType.Sale;
        store.AddMovement(
            new CashMovement
            {
                Id = payment.MessageId,
                CashRegisterSessionId = session.Id,
                PaymentId = payment.PaymentId,
                OrderId = payment.OrderId,
                Type = type,
                Method = payment.Method,
                Amount = payment.Amount,
                Reference = payment.Reference,
                OccurredAtUtc = payment.OccurredAtUtc,
            }
        );
        if (session.Status == CashRegisterStatus.Closed)
            ReconcileLateMovement(session, payment, type);
        store.MarkMessageProcessed(payment.MessageId, time.GetUtcNow().UtcDateTime);
        await store.SaveChangesAsync(ct);
    }

    private static void ReconcileLateMovement(
        CashRegisterSession session,
        PaymentProjection payment,
        CashMovementType type
    )
    {
        decimal signed = type == CashMovementType.Sale ? payment.Amount : -payment.Amount;
        CashRegisterReconciliation? reconciliation = session.Reconciliations.FirstOrDefault(x =>
            string.Equals(x.Method, payment.Method, StringComparison.OrdinalIgnoreCase)
        );
        if (reconciliation is null)
        {
            reconciliation = new CashRegisterReconciliation
            {
                Id = Guid.NewGuid(),
                CashRegisterSessionId = session.Id,
                Method = payment.Method,
            };
            session.Reconciliations.Add(reconciliation);
        }
        reconciliation.ExpectedAmount += signed;
        reconciliation.Difference = reconciliation.ReconciledAmount - reconciliation.ExpectedAmount;
        session.ExpectedTotalAtClose = (session.ExpectedTotalAtClose ?? 0) + signed;
        session.DifferenceAtClose =
            (session.ReconciledTotalAtClose ?? 0) - session.ExpectedTotalAtClose;
        if (payment.Method == "Cash")
            session.ExpectedCashAtClose =
                (session.ExpectedCashAtClose ?? session.OpeningFloat) + signed;
        session.Version++;
    }

    private static Guid? ParseSessionId(string reference)
    {
        const string prefix = "CASHREGISTER:";
        if (!reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        int end = reference.IndexOf(';', prefix.Length);
        string value = end < 0 ? reference[prefix.Length..] : reference[prefix.Length..end];
        return Guid.TryParse(value, out Guid id) ? id : null;
    }
}
