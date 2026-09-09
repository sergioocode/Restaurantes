using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Restaurantes.Dining.Application;
using Restaurantes.Dining.Domain;
using Restaurantes.Dining.Infrastructure.Persistence;

namespace Restaurantes.Dining.Infrastructure.Stores;

public sealed class DiningStore(DiningDbContext db) : IDiningStore
{
    public Task<DiningSession?> LockSessionAsync(Guid sessionId, CancellationToken ct)
    {
        return db
            .Sessions.FromSqlInterpolated(
                $"SELECT * FROM dining_sessions WHERE \"Id\" = {sessionId} FOR UPDATE"
            )
            .SingleOrDefaultAsync(ct);
    }

    public Task<RestaurantTable?> ReadQrTableAsync(string code, CancellationToken ct)
    {
        return db
            .Tables.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.QrCode == code && x.DeletedAtUtc == null && x.IsActive,
                ct
            );
    }

    public Task<DiningRestaurantPolicy?> ReadPolicyAsync(Guid restaurantId, CancellationToken ct)
    {
        return db
            .RestaurantPolicies.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RestaurantId == restaurantId, ct);
    }

    public Task<DiningSession?> ReadActiveSessionWithOrdersAsync(Guid tableId, CancellationToken ct)
    {
        return db
            .Sessions.AsNoTracking()
            .Include(x => x.Orders)
            .SingleOrDefaultAsync(x => x.TableId == tableId && x.Status == "Open", ct);
    }

    public Task<RestaurantTable?> LockTableAsync(Guid tableId, CancellationToken ct)
    {
        return db
            .Tables.FromSqlInterpolated(
                $"SELECT * FROM restaurant_tables WHERE \"Id\" = {tableId} FOR UPDATE"
            )
            .SingleOrDefaultAsync(ct);
    }

    public Task<bool> HasOpenSessionAsync(Guid tableId, CancellationToken ct)
    {
        return db.Sessions.AnyAsync(x => x.TableId == tableId && x.Status == "Open", ct);
    }

    public Task<DiningSession> ReadRequiredActiveSessionAsync(Guid tableId, CancellationToken ct)
    {
        return db
            .Sessions.AsNoTracking()
            .SingleAsync(x => x.TableId == tableId && x.Status == "Open", ct);
    }

    public Task<DiningRestaurantPolicy?> FindPolicyAsync(Guid restaurantId, CancellationToken ct)
    {
        return db.RestaurantPolicies.SingleOrDefaultAsync(x => x.RestaurantId == restaurantId, ct);
    }

    public Task<Dictionary<Guid, DiningSession>> ReadActiveSessionsAsync(
        Guid restaurantId,
        CancellationToken ct
    )
    {
        return db
            .Sessions.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.Status == "Open")
            .ToDictionaryAsync(x => x.TableId, ct);
    }

    public Task<List<RestaurantTable>> ListTablesAsync(Guid restaurantId, CancellationToken ct)
    {
        return db
            .Tables.AsNoTracking()
            .Include(x => x.Zone)
            .Where(x => x.RestaurantId == restaurantId && x.DeletedAtUtc == null)
            .OrderBy(x => x.Code)
            .ToListAsync(ct);
    }

    public Task<RestaurantTable?> LockQrTableAsync(string normalizedQrCode, CancellationToken ct)
    {
        return db
            .Tables.FromSqlInterpolated(
                $"SELECT * FROM restaurant_tables WHERE \"QrCode\" = {normalizedQrCode} FOR UPDATE"
            )
            .SingleOrDefaultAsync(ct);
    }

    public Task<DiningSession?> FindActiveSessionWithOrdersAsync(Guid tableId, CancellationToken ct)
    {
        return db
            .Sessions.Include(x => x.Orders)
            .SingleOrDefaultAsync(x => x.TableId == tableId && x.Status == "Open", ct);
    }

    public Task<DiningSession?> ReadSessionWithOrdersAsync(Guid sessionId, CancellationToken ct)
    {
        return db
            .Sessions.AsNoTracking()
            .Include(x => x.Orders)
            .SingleOrDefaultAsync(x => x.Id == sessionId, ct);
    }

    public Task<DiningSession?> ReadValidatedSessionAsync(
        Guid sessionId,
        Guid restaurantId,
        Guid tableId,
        CancellationToken ct
    )
    {
        return db
            .Sessions.AsNoTracking()
            .SingleOrDefaultAsync(
                x =>
                    x.Id == sessionId
                    && x.RestaurantId == restaurantId
                    && x.TableId == tableId
                    && x.Status == "Open",
                ct
            );
    }

    public Task<bool> AllowsEarlyCheckoutAsync(Guid restaurantId, CancellationToken ct)
    {
        return db
            .RestaurantPolicies.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId)
            .Select(x => x.AllowCheckoutBeforeKitchenCompletion)
            .SingleOrDefaultAsync(ct);
    }

    public Task<DiningSession?> FindSessionWithOrdersAsync(Guid sessionId, CancellationToken ct)
    {
        return db.Sessions.Include(x => x.Orders).SingleOrDefaultAsync(x => x.Id == sessionId, ct);
    }

    public Task<List<DiningZone>> ListZonesAsync(Guid restaurantId, CancellationToken ct)
    {
        return db
            .Zones.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.DeletedAtUtc == null)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);
    }

    public Task<DiningZone?> FindZoneAsync(Guid restaurantId, Guid zoneId, CancellationToken ct)
    {
        return db.Zones.SingleOrDefaultAsync(
            x => x.Id == zoneId && x.RestaurantId == restaurantId && x.DeletedAtUtc == null,
            ct
        );
    }

    public Task<DiningZone?> LockZoneAsync(Guid restaurantId, Guid zoneId, CancellationToken ct)
    {
        return db
            .Zones.FromSqlInterpolated(
                $"SELECT * FROM dining_zones WHERE \"Id\" = {zoneId} AND \"RestaurantId\" = {restaurantId} FOR UPDATE"
            )
            .SingleOrDefaultAsync(ct);
    }

    public Task<bool> HasTablesInZoneAsync(Guid restaurantId, Guid zoneId, CancellationToken ct)
    {
        return db.Tables.AnyAsync(
            x => x.ZoneId == zoneId && x.RestaurantId == restaurantId && x.DeletedAtUtc == null,
            ct
        );
    }

    public void Add<T>(T entity)
        where T : class => db.Add(entity);

    public void Detach(DiningSession session) => db.Entry(session).State = EntityState.Detached;

    public Task LoadOrdersAsync(DiningSession session, CancellationToken ct) =>
        db.Entry(session).Collection(x => x.Orders).LoadAsync(ct);

    public Task<bool> HasProcessedMessageAsync(Guid messageId, CancellationToken ct = default) =>
        db.InboxMessages.AnyAsync(x => x.Id == messageId, ct);

    public void MarkMessageProcessed(Guid messageId, DateTime processedAtUtc) =>
        db.InboxMessages.Add(
            new DiningInboxMessage { Id = messageId, ProcessedAtUtc = processedAtUtc }
        );

    public Task<DiningSessionOrder?> FindOrderAsync(Guid orderId, CancellationToken ct = default) =>
        db.SessionOrders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);

    public Task<DiningPendingPayment?> FindPendingPaymentAsync(
        Guid orderId,
        CancellationToken ct = default
    ) => db.PendingPayments.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);

    public void RemovePendingPayment(DiningPendingPayment payment) =>
        db.PendingPayments.Remove(payment);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new DiningStoreException(DiningStoreFailure.Concurrency, e);
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new DiningStoreException(DiningStoreFailure.Duplicate, e);
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: "23503" })
        {
            throw new DiningStoreException(DiningStoreFailure.ForeignKey, e);
        }
    }

    public async Task<IDiningTransaction> BeginTransactionAsync(CancellationToken ct) =>
        new DiningTransaction(await db.Database.BeginTransactionAsync(ct));

    private sealed class DiningTransaction(IDbContextTransaction transaction) : IDiningTransaction
    {
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);

        public Task RollbackAsync(CancellationToken ct) => transaction.RollbackAsync(ct);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
