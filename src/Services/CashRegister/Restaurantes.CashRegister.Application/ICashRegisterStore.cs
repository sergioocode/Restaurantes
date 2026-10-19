using Restaurantes.CashRegister.Domain;

namespace Restaurantes.CashRegister.Application;

public interface ICashRegisterStore
{
    Task<CashRegisterSession?> GetOpenSessionAsync(Guid restaurantId, CancellationToken ct);
    Task<CashRegisterSession?> GetCurrentAsync(Guid restaurantId, CancellationToken ct);
    Task<List<CashRegisterSession>> GetHistoryAsync(
        Guid restaurantId,
        DateOnly? businessDate,
        CancellationToken ct
    );
    Task<bool> HasOpenSessionAsync(Guid restaurantId, CancellationToken ct);
    Task<CashRegisterSession?> GetSessionAsync(
        Guid restaurantId,
        Guid sessionId,
        CancellationToken ct
    );
    Task<bool> HasProcessedMessageAsync(Guid messageId, CancellationToken ct);
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
