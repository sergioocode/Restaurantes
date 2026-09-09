using Restaurantes.CashRegister.Domain;

namespace Restaurantes.CashRegister.Application;

public interface ICashRegisterStore
{
    Task<Guid?> GetOpenSessionIdAsync(
        Guid restaurantId,
        DateOnly businessDate,
        CancellationToken ct
    );
    Task<CashRegisterSession?> GetCurrentAsync(
        Guid restaurantId,
        DateOnly businessDate,
        CancellationToken ct
    );
    Task<List<CashRegisterSession>> GetHistoryAsync(
        Guid restaurantId,
        int take,
        CancellationToken ct
    );
    Task<bool> HasOpenSessionAsync(Guid restaurantId, CancellationToken ct);
    Task<CashRegisterSession?> GetSessionAsync(
        Guid restaurantId,
        Guid sessionId,
        CancellationToken ct
    );
    Task<List<CashRegisterSession>> GetStaleSessionsAsync(
        Guid restaurantId,
        DateOnly businessDate,
        CancellationToken ct
    );
    Task<bool> HasProcessedMessageAsync(Guid messageId, CancellationToken ct);
    Task<Guid?> GetSaleSessionIdAsync(Guid paymentId, CancellationToken ct);
    Task<CashRegisterSession?> FindProjectionSessionAsync(
        PaymentProjection payment,
        Guid? sessionId,
        CancellationToken ct
    );
    void AddSession(CashRegisterSession session);
    void AddReconciliation(CashRegisterReconciliation reconciliation);
    void AddMovement(CashMovement movement);
    void MarkMessageProcessed(Guid messageId, DateTime processedAtUtc);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed class CashRegisterDuplicateException(Exception innerException)
    : Exception(innerException.Message, innerException);
