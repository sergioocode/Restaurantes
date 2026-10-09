using Restaurantes.Clients.Pos.Pwa.Models;
using Xunit;

namespace Restaurantes.Clients.Pwa.Tests;

public sealed class PosPaymentPreviewTests
{
    [Theory]
    [InlineData(10, 4, 0.40, 4, 4.40, 0, 6)]
    [InlineData(10, 12, 1, 10, 13, 2, 0)]
    [InlineData(10, 10, 1, 10, 11, 0, 0)]
    public void Calculates_partial_exact_and_overpaid_checkouts(
        decimal outstanding,
        decimal paymentAmount,
        decimal tip,
        decimal applied,
        decimal received,
        decimal change,
        decimal remaining
    )
    {
        PosPaymentPreview preview = PosPaymentPreview.Calculate(outstanding, paymentAmount, tip);

        Assert.Equal(applied, preview.AppliedToAccount);
        Assert.Equal(received, preview.Received);
        Assert.Equal(change, preview.Change);
        Assert.Equal(remaining, preview.Remaining);
        Assert.Equal(preview.AppliedToAccount + tip + preview.Change, preview.Received);
    }
}
