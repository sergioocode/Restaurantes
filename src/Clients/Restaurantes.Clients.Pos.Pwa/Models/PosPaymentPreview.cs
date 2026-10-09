namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record PosPaymentPreview(
    decimal AppliedToAccount,
    decimal Received,
    decimal Change,
    decimal Remaining
)
{
    public static PosPaymentPreview Calculate(
        decimal outstanding,
        decimal paymentAmount,
        decimal tip
    )
    {
        decimal applied = Math.Min(Math.Max(0, paymentAmount), Math.Max(0, outstanding));
        return new(
            applied,
            paymentAmount + tip,
            Math.Max(0, paymentAmount - applied),
            Math.Max(0, outstanding - applied)
        );
    }
}
