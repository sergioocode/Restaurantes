using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Application;

public interface IDiningStore
{
    Task<DiningSession?> LockSessionAsync(Guid sessionId, CancellationToken ct);
    Task<RestaurantTable?> ReadQrTableAsync(string code, CancellationToken ct);
    Task<DiningRestaurantPolicy?> ReadPolicyAsync(Guid restaurantId, CancellationToken ct);
    Task<DiningSession?> ReadActiveSessionWithOrdersAsync(Guid tableId, CancellationToken ct);
    Task<RestaurantTable?> LockTableAsync(Guid tableId, CancellationToken ct);
    Task<bool> HasOpenSessionAsync(Guid tableId, CancellationToken ct);
    Task<DiningSession> ReadRequiredActiveSessionAsync(Guid tableId, CancellationToken ct);
    Task<DiningRestaurantPolicy?> FindPolicyAsync(Guid restaurantId, CancellationToken ct);
    Task<Dictionary<Guid, DiningSession>> ReadActiveSessionsAsync(
        Guid restaurantId,
        CancellationToken ct
    );
    Task<List<RestaurantTable>> ListTablesAsync(Guid restaurantId, CancellationToken ct);
    Task<RestaurantTable?> LockQrTableAsync(string normalizedQrCode, CancellationToken ct);
    Task<DiningSession?> FindActiveSessionWithOrdersAsync(Guid tableId, CancellationToken ct);
    Task<DiningSession?> ReadSessionWithOrdersAsync(Guid sessionId, CancellationToken ct);
    Task<DiningSession?> ReadValidatedSessionAsync(
        Guid sessionId,
        Guid restaurantId,
        Guid tableId,
        CancellationToken ct
    );
    Task<bool> AllowsEarlyCheckoutAsync(Guid restaurantId, CancellationToken ct);
    Task<DiningSession?> FindSessionWithOrdersAsync(Guid sessionId, CancellationToken ct);
    Task<List<DiningZone>> ListZonesAsync(Guid restaurantId, CancellationToken ct);
    Task<DiningZone?> FindZoneAsync(Guid restaurantId, Guid zoneId, CancellationToken ct);
    Task<DiningZone?> LockZoneAsync(Guid restaurantId, Guid zoneId, CancellationToken ct);
    Task<bool> HasTablesInZoneAsync(Guid restaurantId, Guid zoneId, CancellationToken ct);
    void Add<T>(T entity)
        where T : class;
    void Detach(DiningSession session);
    Task LoadOrdersAsync(DiningSession session, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<IDiningTransaction> BeginTransactionAsync(CancellationToken ct);
    Task<bool> HasProcessedMessageAsync(Guid messageId, CancellationToken ct = default);
    void MarkMessageProcessed(Guid messageId, DateTime processedAtUtc);
    Task<DiningSessionOrder?> FindOrderAsync(Guid orderId, CancellationToken ct = default);
    Task<DiningPendingPayment?> FindPendingPaymentAsync(
        Guid orderId,
        CancellationToken ct = default
    );
    void RemovePendingPayment(DiningPendingPayment payment);
}

public enum DiningStoreFailure
{
    Duplicate,
    ForeignKey,
    Concurrency,
}

public sealed class DiningStoreException(DiningStoreFailure failure, Exception innerException)
    : Exception(innerException.Message, innerException)
{
    public DiningStoreFailure Failure { get; } = failure;
}
