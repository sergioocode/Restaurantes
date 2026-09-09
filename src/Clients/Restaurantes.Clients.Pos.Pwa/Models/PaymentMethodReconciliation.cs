namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record PaymentMethodReconciliation(
    string Method,
    decimal Expected,
    decimal Reconciled,
    decimal Difference
);
