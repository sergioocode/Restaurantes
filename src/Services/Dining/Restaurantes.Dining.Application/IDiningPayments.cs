namespace Restaurantes.Dining.Application;

public sealed record DiningPaymentRequest(
    Guid IdempotencyKey,
    int TransactionOrderCount,
    string Method,
    string ExternalReference
);

public sealed record DiningPaymentResult(bool Succeeded, int StatusCode, string Body);

public interface IDiningPayments
{
    Task<DiningPaymentResult> CaptureAsync(
        Guid orderId,
        DiningPaymentRequest request,
        string? authorization,
        CancellationToken ct
    );
}
