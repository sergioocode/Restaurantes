using Restaurantes.CashRegister.Domain;

namespace Restaurantes.CashRegister.Application;

public sealed class CashRegisterService(
    ICashRegisterStore store,
    IIncompleteOrdersChecker incompleteOrders,
    TimeProvider time,
    TimeZoneInfo timeZone
)
{
    public async Task<CashRegisterResult> IsOpen(Guid restaurantId, CancellationToken ct)
    {
        CashRegisterSession? session = await store.GetOpenSessionAsync(restaurantId, ct);
        DateOnly businessDate = session?.BusinessDate ?? BusinessDate(time.GetUtcNow().UtcDateTime);
        return Ok(
            new CashRegisterAvailabilityResponse(
                restaurantId,
                session is not null,
                businessDate,
                session?.Id
            )
        );
    }

    public async Task<CashRegisterResult> Current(Guid restaurantId, CancellationToken ct)
    {
        CashRegisterSession? session = await store.GetCurrentAsync(restaurantId, ct);
        return session is null ? new(CashRegisterOutcome.NoContent) : Ok(ToResponse(session));
    }

    public async Task<CashRegisterResult> History(Guid restaurantId, CancellationToken ct)
    {
        return Ok((await store.GetHistoryAsync(restaurantId, 100, ct)).Select(ToResponse));
    }

    public async Task<CashRegisterResult> Open(
        Guid restaurantId,
        OpenCashRegisterRequest request,
        CashRegisterUser user,
        CancellationToken ct
    )
    {
        if (request.OpeningFloat is < 0 or > 100000)
        {
            return Validation("openingFloat", "El fondo inicial debe estar entre 0 y 100.000 €.");
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        DateOnly today = BusinessDate(now);
        if (await store.HasOpenSessionAsync(restaurantId, ct))
        {
            return Conflict("Ya existe un turno de caja abierto para este local.");
        }

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
        string authorization,
        CancellationToken ct
    )
    {
        if (request.ReconciledByMethod is null || request.ReconciledByMethod.Count == 0)
        {
            return Validation("reconciledByMethod", "Debes conciliar todos los medios de cobro.");
        }

        if (
            request.ReconciledByMethod.Any(x =>
                string.IsNullOrWhiteSpace(x.Key) || x.Value is < 0 or > 1000000
            )
        )
        {
            return Validation("reconciledByMethod", "Hay un importe de conciliación no válido.");
        }

        CashRegisterSession? session = await store.GetSessionAsync(restaurantId, sessionId, ct);
        if (session is null)
        {
            return new(CashRegisterOutcome.NotFound);
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        if (session.Status != CashRegisterStatus.Open)
        {
            return Conflict("Este turno de caja ya no está abierto.");
        }

        Dictionary<string, decimal> expected = ExpectedByMethod(session);
        Dictionary<string, decimal> reconciled = request.ReconciledByMethod.ToDictionary(
            x => x.Key.Trim(),
            x => x.Value,
            StringComparer.OrdinalIgnoreCase
        );
        string[] missing = expected.Keys.Where(method => !reconciled.ContainsKey(method)).ToArray();
        if (missing.Length > 0)
        {
            return Validation(
                "reconciledByMethod",
                $"Falta conciliar: {string.Join(", ", missing)}."
            );
        }

        int incompleteOrderCount;
        try
        {
            incompleteOrderCount = await incompleteOrders.CountAsync(
                restaurantId,
                authorization,
                ct
            );
        }
        catch (IncompleteOrdersUnavailableException)
        {
            return new(
                CashRegisterOutcome.ServiceUnavailable,
                new
                {
                    detail = "No se pudo verificar si quedan pedidos incompletos. Inténtalo de nuevo.",
                }
            );
        }

        if (incompleteOrderCount > 0)
        {
            return new(
                CashRegisterOutcome.Conflict,
                new
                {
                    detail = $"No se puede cerrar la caja: quedan {incompleteOrderCount} pedidos incompletos. Complétalos o resuélvelos desde el TPV.",
                    incompleteOrderCount,
                }
            );
        }

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

    private DateOnly BusinessDate(DateTime utc)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone));
    }

    internal static Dictionary<string, decimal> ExpectedByMethod(CashRegisterSession session)
    {
        Dictionary<string, decimal> totals = session
            .Movements.Where(x => x.Type == CashMovementType.Sale)
            .GroupBy(x => x.Method, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.Sum(movement => movement.Amount),
                StringComparer.OrdinalIgnoreCase
            );
        totals["Cash"] = totals.GetValueOrDefault("Cash") + session.OpeningFloat;
        return totals;
    }

    private static CashRegisterResponse ToResponse(CashRegisterSession session)
    {
        decimal totalSales = session
            .Movements.Where(x => x.Type == CashMovementType.Sale)
            .Sum(x => x.Amount);
        decimal cashSales = session
            .Movements.Where(x => x.Method == "Cash" && x.Type == CashMovementType.Sale)
            .Sum(x => x.Amount);
        PaymentMethodTotal[] methods = session
            .Movements.Where(x => x.Type == CashMovementType.Sale)
            .GroupBy(x => x.Method)
            .OrderBy(x => x.Key)
            .Select(g => new PaymentMethodTotal(g.Key, g.Sum(x => x.Amount)))
            .ToArray();
        Dictionary<string, decimal> expected = ExpectedByMethod(session);
        return new(
            session.Id,
            session.RestaurantId,
            session.BusinessDate,
            session.Status.ToString(),
            session.OpeningFloat,
            totalSales,
            cashSales,
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
                .Movements.Where(x => x.Type == CashMovementType.Sale)
                .OrderByDescending(x => x.OccurredAtUtc)
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

    private static CashRegisterResult Ok(object value)
    {
        return new(CashRegisterOutcome.Ok, value);
    }

    private static CashRegisterResult Conflict(string detail)
    {
        return new(CashRegisterOutcome.Conflict, new { detail });
    }

    private static CashRegisterResult Validation(string key, string message)
    {
        return new(
            CashRegisterOutcome.Validation,
            Errors: new Dictionary<string, string[]> { [key] = [message] }
        );
    }
}
