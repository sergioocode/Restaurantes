namespace Restaurantes.Dining.Application;

public sealed record DiningPaymentRequest(
    Guid IdempotencyKey,
    int TransactionOrderCount,
    decimal Amount,
    decimal TipAmount,
    string Method,
    string ExternalReference,
    decimal? TenderedAmount = null,
    Guid? SaleGroupId = null
);

public sealed record DiningPaymentResult(
    bool Succeeded,
    int StatusCode,
    string Body,
    decimal AmountPaid = 0,
    decimal TipAmountPaid = 0
);

public interface IDiningPayments
{
    Task<DiningPaymentResult> CaptureAsync(
        Guid orderId,
        DiningPaymentRequest request,
        string? authorization,
        CancellationToken ct
    );
}
