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

    public async Task<CashRegisterResult> History(
        Guid restaurantId,
        DateOnly? businessDate,
        CancellationToken ct
    )
    {
        return Ok(
            (await store.GetHistoryAsync(restaurantId, businessDate, ct)).Select(session =>
                ToResponse(session, includeAllMovements: businessDate.HasValue)
            )
        );
    }

    public async Task<CashRegisterResult> CreateMovement(
        Guid restaurantId,
        Guid sessionId,
        CreateCashMovementRequest request,
        CashRegisterUser user,
        CancellationToken ct
    )
    {
        CashMovementType? type = request.Type?.Trim().ToLowerInvariant() switch
        {
            "income" => CashMovementType.Income,
            "expense" => CashMovementType.Expense,
            _ => null,
        };
        if (type is null)
        {
            return Validation("type", "Selecciona ingreso o egreso.");
        }

        string? method = request.Method?.Trim() switch
        {
            "Cash" => "Cash",
            "Card" => "Card",
            "Transfer" => "Transfer",
            _ => null,
        };
        if (method is null)
        {
            return Validation("method", "Selecciona efectivo, tarjeta o transferencia.");
        }

        string category = request.Category?.Trim() ?? string.Empty;
        string description = request.Description?.Trim() ?? string.Empty;
        if (category.Length is 0 or > 80)
        {
            return Validation(
                "category",
                "La categoría es obligatoria y admite hasta 80 caracteres."
            );
        }
        if (description.Length is 0 or > 160)
        {
            return Validation(
                "description",
                "El detalle es obligatorio y admite hasta 160 caracteres."
            );
        }
        if (request.Amount is <= 0 or > 100000)
        {
            return Validation("amount", "El importe debe ser mayor que 0 y no superar 100.000 €.");
        }

        CashRegisterSession? session = await store.GetSessionAsync(restaurantId, sessionId, ct);
        if (session is null)
        {
            return new(CashRegisterOutcome.NotFound);
        }
        if (session.Status != CashRegisterStatus.Open)
        {
            return Conflict("Solo se pueden registrar movimientos en una caja abierta.");
        }

        if (
            type == CashMovementType.Expense
            && method == "Cash"
            && ExpectedByMethod(session).GetValueOrDefault(method) < request.Amount
        )
        {
            return Validation("amount", "El egreso supera el efectivo disponible en la caja.");
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        store.AddMovement(
            new CashMovement
            {
                Id = Guid.NewGuid(),
                CashRegisterSessionId = session.Id,
                Session = session,
                Type = type.Value,
                Method = method,
                Amount = request.Amount,
                Category = category,
                Reference = description,
                RecordedByUserId = user.Id,
                RecordedByName = user.Name,
                OccurredAtUtc = now,
            }
        );
        session.Version++;
        await store.SaveChangesAsync(ct);
        return Ok(ToResponse(session));
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
                string.IsNullOrWhiteSpace(x.Key)
                || x.Value
                    < (
                        string.Equals(x.Key.Trim(), "Cash", StringComparison.OrdinalIgnoreCase)
                            ? 0
                            : -1000000
                    )
                || x.Value > 1000000
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
            .Movements.GroupBy(x => x.Method, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x =>
                    x.Sum(movement =>
                        movement.Type switch
                        {
                            CashMovementType.Income => movement.Amount,
                            CashMovementType.Expense => -movement.Amount,
                            _ => movement.Amount,
                        }
                    ),
                StringComparer.OrdinalIgnoreCase
            );
        totals["Cash"] = totals.GetValueOrDefault("Cash") + session.OpeningFloat;
        return totals;
    }

    private static CashRegisterResponse ToResponse(
        CashRegisterSession session,
        bool includeAllMovements = false
    )
    {
        decimal totalSales = session
            .Movements.Where(x => x.Type == CashMovementType.Sale)
            .Sum(x => x.Amount);
        decimal cashSales = session
            .Movements.Where(x => x.Method == "Cash" && x.Type == CashMovementType.Sale)
            .Sum(x => x.Amount);
        decimal otherIncomeTotal = session
            .Movements.Where(x => x.Type == CashMovementType.Income)
            .Sum(x => x.Amount);
        decimal otherExpenseTotal = session
            .Movements.Where(x => x.Type == CashMovementType.Expense)
            .Sum(x => x.Amount);
        PaymentMethodTotal[] methods = session
            .Movements.Where(x => x.Type == CashMovementType.Sale)
            .GroupBy(x => x.Method)
            .OrderBy(x => x.Key)
            .Select(g => new PaymentMethodTotal(g.Key, g.Sum(x => x.Amount)))
            .ToArray();
        Dictionary<string, decimal> expected = ExpectedByMethod(session);
        IEnumerable<CashMovement> sales = session.Movements.Where(x =>
            x.Type == CashMovementType.Sale
        );
        if (!includeAllMovements)
        {
            sales = sales.OrderByDescending(x => x.OccurredAtUtc).Take(100);
        }

        return new(
            session.Id,
            session.RestaurantId,
            session.BusinessDate,
            session.Status.ToString(),
            session.OpeningFloat,
            totalSales,
            cashSales,
            otherIncomeTotal,
            otherExpenseTotal,
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
                .Movements.Where(x => x.Type is CashMovementType.Income or CashMovementType.Expense)
                .Concat(sales)
                .OrderByDescending(x => x.OccurredAtUtc)
                .Select(x => new CashMovementResponse(
                    x.Id,
                    x.Type.ToString(),
                    x.Method,
                    x.Amount,
                    x.Reference,
                    x.OccurredAtUtc,
                    x.Category,
                    x.RecordedByName ?? string.Empty,
                    x.OrderId,
                    x.PaymentId,
                    x.SaleId
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
