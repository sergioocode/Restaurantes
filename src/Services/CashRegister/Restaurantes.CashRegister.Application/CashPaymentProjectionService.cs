using Restaurantes.CashRegister.Domain;

namespace Restaurantes.CashRegister.Application;

public sealed class CashPaymentProjectionService(ICashRegisterStore store, TimeProvider time)
{
    public async Task Project(PaymentProjection payment, CancellationToken ct)
    {
        if (await store.HasProcessedMessageAsync(payment.MessageId, ct))
        {
            return;
        }

        Guid? sessionId = payment.CashRegisterSessionId ?? ParseSessionId(payment.Reference);

        CashRegisterSession? session = await store.FindProjectionSessionAsync(
            payment,
            sessionId,
            ct
        );
        if (session is null)
        {
            throw new InvalidOperationException(
                $"No cash-register shift covers payment {payment.PaymentId} for restaurant {payment.RestaurantId}."
            );
        }

        store.AddMovement(
            new CashMovement
            {
                Id = payment.MessageId,
                CashRegisterSessionId = session.Id,
                PaymentId = payment.PaymentId,
                SaleId = payment.SaleId,
                OrderId = payment.OrderId,
                Type = CashMovementType.Sale,
                Method = payment.Method,
                Amount = payment.Amount,
                Reference = payment.Reference,
                OccurredAtUtc = payment.OccurredAtUtc,
            }
        );
        if (payment.Method == "Card" && payment.ChangeAmount > 0)
        {
            store.AddMovement(
                new CashMovement
                {
                    Id = Guid.NewGuid(),
                    CashRegisterSessionId = session.Id,
                    PaymentId = payment.PaymentId,
                    SaleId = payment.SaleId,
                    OrderId = payment.OrderId,
                    Type = CashMovementType.CardTender,
                    Method = "Card",
                    Amount = payment.ChangeAmount,
                    Reference = payment.Reference,
                    OccurredAtUtc = payment.OccurredAtUtc,
                }
            );
            store.AddMovement(
                new CashMovement
                {
                    Id = Guid.NewGuid(),
                    CashRegisterSessionId = session.Id,
                    PaymentId = payment.PaymentId,
                    SaleId = payment.SaleId,
                    OrderId = payment.OrderId,
                    Type = CashMovementType.CashChange,
                    Method = "Cash",
                    Amount = -payment.ChangeAmount,
                    Reference = payment.Reference,
                    OccurredAtUtc = payment.OccurredAtUtc,
                }
            );
        }
        if (session.Status == CashRegisterStatus.Closed)
        {
            ReconcileLateMovement(session, payment.Method, payment.Amount);
            if (payment.Method == "Card" && payment.ChangeAmount > 0)
            {
                ReconcileLateMovement(session, "Card", payment.ChangeAmount);
                ReconcileLateMovement(session, "Cash", -payment.ChangeAmount);
            }
        }

        store.MarkMessageProcessed(payment.MessageId, time.GetUtcNow().UtcDateTime);
        await store.SaveChangesAsync(ct);
    }

    private static void ReconcileLateMovement(
        CashRegisterSession session,
        string method,
        decimal amount
    )
    {
        CashRegisterReconciliation? reconciliation = session.Reconciliations.FirstOrDefault(x =>
            string.Equals(x.Method, method, StringComparison.OrdinalIgnoreCase)
        );
        if (reconciliation is null)
        {
            reconciliation = new CashRegisterReconciliation
            {
                Id = Guid.NewGuid(),
                CashRegisterSessionId = session.Id,
                Method = method,
            };
            session.Reconciliations.Add(reconciliation);
        }
        reconciliation.ExpectedAmount += amount;
        reconciliation.Difference = reconciliation.ReconciledAmount - reconciliation.ExpectedAmount;
        session.ExpectedTotalAtClose = (session.ExpectedTotalAtClose ?? 0) + amount;
        session.DifferenceAtClose =
            (session.ReconciledTotalAtClose ?? 0) - session.ExpectedTotalAtClose;
        if (method == "Cash")
        {
            session.ExpectedCashAtClose =
                (session.ExpectedCashAtClose ?? session.OpeningFloat) + amount;
        }

        session.Version++;
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
}
