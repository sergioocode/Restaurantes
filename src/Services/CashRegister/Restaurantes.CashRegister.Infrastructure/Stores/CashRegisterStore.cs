using Microsoft.EntityFrameworkCore;
using Npgsql;
using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Domain;
using Restaurantes.CashRegister.Infrastructure.Persistence;

namespace Restaurantes.CashRegister.Infrastructure.Stores;

public sealed class CashRegisterStore(CashRegisterDbContext db) : ICashRegisterStore
{
    public Task<Guid?> GetOpenSessionIdAsync(
        Guid restaurantId,
        DateOnly businessDate,
        CancellationToken ct
    ) =>
        db
            .Sessions.AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId
                && x.Status == CashRegisterStatus.Open
                && x.BusinessDate == businessDate
            )
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(ct);

    public Task<CashRegisterSession?> GetCurrentAsync(
        Guid restaurantId,
        DateOnly businessDate,
        CancellationToken ct
    ) =>
        db
            .Sessions.AsNoTracking()
            .Include(x => x.Movements)
            .Include(x => x.Reconciliations)
            .SingleOrDefaultAsync(
                x =>
                    x.RestaurantId == restaurantId
                    && x.Status == CashRegisterStatus.Open
                    && x.BusinessDate == businessDate,
                ct
            );

    public Task<List<CashRegisterSession>> GetHistoryAsync(
        Guid restaurantId,
        int take,
        CancellationToken ct
    ) =>
        db
            .Sessions.AsNoTracking()
            .Include(x => x.Movements)
            .Include(x => x.Reconciliations)
            .Where(x => x.RestaurantId == restaurantId)
            .OrderByDescending(x => x.OpenedAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public Task<bool> HasOpenSessionAsync(Guid restaurantId, CancellationToken ct) =>
        db.Sessions.AnyAsync(
            x => x.RestaurantId == restaurantId && x.Status == CashRegisterStatus.Open,
            ct
        );

    public Task<CashRegisterSession?> GetSessionAsync(
        Guid restaurantId,
        Guid sessionId,
        CancellationToken ct
    ) =>
        db
            .Sessions.Include(x => x.Movements)
            .Include(x => x.Reconciliations)
            .SingleOrDefaultAsync(x => x.Id == sessionId && x.RestaurantId == restaurantId, ct);

    public Task<List<CashRegisterSession>> GetStaleSessionsAsync(
        Guid restaurantId,
        DateOnly businessDate,
        CancellationToken ct
    ) =>
        db
            .Sessions.Where(x =>
                x.RestaurantId == restaurantId
                && x.Status == CashRegisterStatus.Open
                && x.BusinessDate != businessDate
            )
            .ToListAsync(ct);

    public Task<bool> HasProcessedMessageAsync(Guid messageId, CancellationToken ct) =>
        db.InboxMessages.AnyAsync(x => x.Id == messageId, ct);

    public Task<Guid?> GetSaleSessionIdAsync(Guid paymentId, CancellationToken ct) =>
        db
            .Movements.AsNoTracking()
            .Where(x => x.PaymentId == paymentId && x.Type == CashMovementType.Sale)
            .Select(x => (Guid?)x.CashRegisterSessionId)
            .SingleOrDefaultAsync(ct);

    public Task<CashRegisterSession?> FindProjectionSessionAsync(
        PaymentProjection payment,
        Guid? sessionId,
        CancellationToken ct
    )
    {
        IQueryable<CashRegisterSession> query = db
            .Sessions.Include(x => x.Movements)
            .Include(x => x.Reconciliations);
        return sessionId.HasValue
            ? query.SingleOrDefaultAsync(
                x => x.Id == sessionId && x.RestaurantId == payment.RestaurantId,
                ct
            )
            : query
                .Where(x =>
                    x.RestaurantId == payment.RestaurantId
                    && x.OpenedAtUtc <= payment.OccurredAtUtc
                    && (x.ClosedAtUtc == null || x.ClosedAtUtc >= payment.OccurredAtUtc)
                )
                .OrderByDescending(x => x.OpenedAtUtc)
                .FirstOrDefaultAsync(ct);
    }

    public void AddSession(CashRegisterSession session) => db.Sessions.Add(session);

    public void AddReconciliation(CashRegisterReconciliation reconciliation) =>
        db.Reconciliations.Add(reconciliation);

    public void AddMovement(CashMovement movement) => db.Movements.Add(movement);

    public void MarkMessageProcessed(Guid messageId, DateTime processedAtUtc) =>
        db.InboxMessages.Add(
            new CashRegisterInboxMessage { Id = messageId, ProcessedAtUtc = processedAtUtc }
        );

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new CashRegisterDuplicateException(exception);
        }
    }
}
