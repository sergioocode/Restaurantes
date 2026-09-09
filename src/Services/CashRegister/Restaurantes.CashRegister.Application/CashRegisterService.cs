using Restaurantes.CashRegister.Domain;

namespace Restaurantes.CashRegister.Application;

public sealed class CashRegisterService(ICashRegisterStore store, TimeProvider time)
{
    private const string MadridTimeZone = "Europe/Madrid";

    public async Task<CashRegisterResult> IsOpen(Guid restaurantId, CancellationToken ct)
    {
        DateTime now = time.GetUtcNow().UtcDateTime;
        DateOnly today = BusinessDate(now);
        await ExpirePreviousDays(restaurantId, today, now, ct);
        Guid? sessionId = await store.GetOpenSessionIdAsync(restaurantId, today, ct);
        return Ok(
            new CashRegisterAvailabilityResponse(restaurantId, sessionId.HasValue, today, sessionId)
        );
    }

    public async Task<CashRegisterResult> Current(Guid restaurantId, CancellationToken ct)
    {
        DateTime now = time.GetUtcNow().UtcDateTime;
        DateOnly today = BusinessDate(now);
        await ExpirePreviousDays(restaurantId, today, now, ct);
        CashRegisterSession? session = await store.GetCurrentAsync(restaurantId, today, ct);
        return session is null ? new(CashRegisterOutcome.NoContent) : Ok(ToResponse(session));
    }

    public async Task<CashRegisterResult> History(Guid restaurantId, CancellationToken ct) =>
        Ok((await store.GetHistoryAsync(restaurantId, 100, ct)).Select(ToResponse));

    public async Task<CashRegisterResult> Open(
        Guid restaurantId,
        OpenCashRegisterRequest request,
        CashRegisterUser user,
        CancellationToken ct
    )
    {
        if (request.OpeningFloat is < 0 or > 100000)
            return Validation("openingFloat", "El fondo inicial debe estar entre 0 y 100.000 €.");

        DateTime now = time.GetUtcNow().UtcDateTime;
        DateOnly today = BusinessDate(now);
        await ExpirePreviousDays(restaurantId, today, now, ct);
        if (await store.HasOpenSessionAsync(restaurantId, ct))
            return Conflict("Ya existe un turno de caja abierto para este local.");

        CashRegisterSession session = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            BusinessDate = today,
            Status = CashRegisterStatus.Open,
            OpeningFloat = request.OpeningFloat,
            OpenedAtUtc = now,
            OpenedByUserId = user.Id,
            OpenedByName = user.Name,
        };
        store.AddSession(session);
        try
        {
            await store.SaveChangesAsync(ct);
        }
        catch (CashRegisterDuplicateException)
        {
            return Conflict("Ya existe un turno de caja abierto para este local.");
        }
        return new(
            CashRegisterOutcome.Created,
            ToResponse(session),
            $"/api/cash-register/restaurants/{restaurantId}/sessions/{session.Id}"
        );
    }

    public async Task<CashRegisterResult> Close(
        Guid restaurantId,
        Guid sessionId,
        CloseCashRegisterRequest request,
        CashRegisterUser user,
        CancellationToken ct
    )
    {
        if (request.ReconciledByMethod is null || request.ReconciledByMethod.Count == 0)
            return Validation("reconciledByMethod", "Debes conciliar todos los medios de cobro.");
        if (
            request.ReconciledByMethod.Any(x =>
                string.IsNullOrWhiteSpace(x.Key) || x.Value is < 0 or > 1000000
            )
        )
            return Validation("reconciledByMethod", "Hay un importe de conciliación no válido.");

        CashRegisterSession? session = await store.GetSessionAsync(restaurantId, sessionId, ct);
        if (session is null)
            return new(CashRegisterOutcome.NotFound);
        DateTime now = time.GetUtcNow().UtcDateTime;
        if (session.Status != CashRegisterStatus.Open)
            return Conflict("Este turno de caja ya no está abierto.");
        if (session.BusinessDate != BusinessDate(now))
        {
            Expire(session, now);
            await store.SaveChangesAsync(ct);
            return Conflict(
                "El turno pertenecía a un día anterior y ha caducado. Abre una caja nueva."
            );
        }

        Dictionary<string, decimal> expected = ExpectedByMethod(session);
        Dictionary<string, decimal> reconciled = request.ReconciledByMethod.ToDictionary(
            x => x.Key.Trim(),
            x => x.Value,
            StringComparer.OrdinalIgnoreCase
        );
        string[] missing = expected.Keys.Where(method => !reconciled.ContainsKey(method)).ToArray();
        if (missing.Length > 0)
            return Validation(
                "reconciledByMethod",
                $"Falta conciliar: {string.Join(", ", missing)}."
            );

        foreach ((string method, decimal amount) in expected)
        {
            decimal actual = reconciled[method];
            store.AddReconciliation(
                new CashRegisterReconciliation
                {
                    Id = Guid.NewGuid(),
                    CashRegisterSessionId = session.Id,
                    Method = method,
                    ExpectedAmount = amount,
                    ReconciledAmount = actual,
                    Difference = actual - amount,
                }
            );
        }
        decimal expectedTotal = expected.Values.Sum();
        decimal reconciledTotal = expected.Keys.Sum(method => reconciled[method]);
        session.Status = CashRegisterStatus.Closed;
        session.CountedCash = reconciled.GetValueOrDefault("Cash");
        session.ExpectedCashAtClose = expected.GetValueOrDefault("Cash");
        session.ExpectedTotalAtClose = expectedTotal;
        session.ReconciledTotalAtClose = reconciledTotal;
        session.DifferenceAtClose = reconciledTotal - expectedTotal;
        session.ClosedAtUtc = now;
        session.ClosedByUserId = user.Id;
        session.ClosedByName = user.Name;
        session.Version++;
        await store.SaveChangesAsync(ct);
        return Ok(ToResponse(session));
    }

    private async Task ExpirePreviousDays(
        Guid restaurantId,
        DateOnly today,
        DateTime now,
        CancellationToken ct
    )
    {
        List<CashRegisterSession> stale = await store.GetStaleSessionsAsync(
            restaurantId,
            today,
            ct
        );
        foreach (CashRegisterSession session in stale)
            Expire(session, now);
        if (stale.Count > 0)
            await store.SaveChangesAsync(ct);
    }

    private static void Expire(CashRegisterSession session, DateTime now)
    {
        session.Status = CashRegisterStatus.Expired;
        session.ClosedAtUtc = now;
        session.ClosedByName = "Cierre automático por cambio de día";
        session.Version++;
    }

    private static DateOnly BusinessDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, MadridTimeZone));

    private static decimal Signed(CashMovement movement) =>
        movement.Type == CashMovementType.Sale ? movement.Amount : -movement.Amount;

    internal static Dictionary<string, decimal> ExpectedByMethod(CashRegisterSession session)
    {
        Dictionary<string, decimal> totals = session
            .Movements.GroupBy(x => x.Method, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(Signed), StringComparer.OrdinalIgnoreCase);
        totals["Cash"] = totals.GetValueOrDefault("Cash") + session.OpeningFloat;
        return totals;
    }

    private static CashRegisterResponse ToResponse(CashRegisterSession session)
    {
        decimal totalSales = session
            .Movements.Where(x => x.Type == CashMovementType.Sale)
            .Sum(x => x.Amount);
        decimal totalRefunds = session
            .Movements.Where(x => x.Type == CashMovementType.Refund)
            .Sum(x => x.Amount);
        decimal cashSales = session
            .Movements.Where(x => x.Method == "Cash" && x.Type == CashMovementType.Sale)
            .Sum(x => x.Amount);
        decimal cashRefunds = session
            .Movements.Where(x => x.Method == "Cash" && x.Type == CashMovementType.Refund)
            .Sum(x => x.Amount);
        PaymentMethodTotal[] methods = session
            .Movements.GroupBy(x => x.Method)
            .OrderBy(x => x.Key)
            .Select(g => new PaymentMethodTotal(
                g.Key,
                g.Where(x => x.Type == CashMovementType.Sale).Sum(x => x.Amount),
                g.Where(x => x.Type == CashMovementType.Refund).Sum(x => x.Amount),
                g.Sum(Signed)
            ))
            .ToArray();
        Dictionary<string, decimal> expected = ExpectedByMethod(session);
        return new(
            session.Id,
            session.RestaurantId,
            session.BusinessDate,
            session.Status.ToString(),
            session.OpeningFloat,
            totalSales,
            totalRefunds,
            cashSales,
            cashRefunds,
            expected.GetValueOrDefault("Cash"),
            expected.Values.Sum(),
            session.ReconciledTotalAtClose,
            session.CountedCash,
            session.DifferenceAtClose,
            session.OpenedAtUtc,
            session.OpenedByName,
            session.ClosedAtUtc,
            session.ClosedByName,
            methods,
            expected
                .OrderBy(x => x.Key)
                .Select(x => new PaymentMethodExpected(x.Key, x.Value))
                .ToArray(),
            session
                .Reconciliations.OrderBy(x => x.Method)
                .Select(x => new PaymentMethodReconciliation(
                    x.Method,
                    x.ExpectedAmount,
                    x.ReconciledAmount,
                    x.Difference
                ))
                .ToArray(),
            session
                .Movements.OrderByDescending(x => x.OccurredAtUtc)
                .Take(100)
                .Select(x => new CashMovementResponse(
                    x.Id,
                    x.Type.ToString(),
                    x.Method,
                    x.Amount,
                    x.Reference,
                    x.OccurredAtUtc
                ))
                .ToArray()
        );
    }

    private static CashRegisterResult Ok(object value) => new(CashRegisterOutcome.Ok, value);

    private static CashRegisterResult Conflict(string detail) =>
        new(CashRegisterOutcome.Conflict, new { detail });

    private static CashRegisterResult Validation(string key, string message) =>
        new(
            CashRegisterOutcome.Validation,
            Errors: new Dictionary<string, string[]> { [key] = [message] }
        );
}
