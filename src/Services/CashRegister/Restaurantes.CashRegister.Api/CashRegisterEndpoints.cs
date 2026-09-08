using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Restaurantes.Security;

namespace Restaurantes.CashRegister.Api;

public sealed record OpenCashRegisterRequest(decimal OpeningFloat);

public sealed record CloseCashRegisterRequest(Dictionary<string, decimal> ReconciledByMethod);

public sealed record CashMovementResponse(
    Guid Id,
    string Type,
    string Method,
    decimal Amount,
    string Reference,
    DateTime OccurredAtUtc
);

public sealed record PaymentMethodTotal(string Method, decimal Sales, decimal Refunds, decimal Net);

public sealed record PaymentMethodExpected(string Method, decimal Expected);

public sealed record PaymentMethodReconciliation(
    string Method,
    decimal Expected,
    decimal Reconciled,
    decimal Difference
);

public sealed record CashRegisterResponse(
    Guid Id,
    Guid RestaurantId,
    DateOnly BusinessDate,
    string Status,
    decimal OpeningFloat,
    decimal TotalSales,
    decimal TotalRefunds,
    decimal CashSales,
    decimal CashRefunds,
    decimal ExpectedCash,
    decimal ExpectedTotal,
    decimal? ReconciledTotal,
    decimal? CountedCash,
    decimal? Difference,
    DateTime OpenedAtUtc,
    string OpenedByName,
    DateTime? ClosedAtUtc,
    string? ClosedByName,
    IReadOnlyCollection<PaymentMethodTotal> PaymentMethods,
    IReadOnlyCollection<PaymentMethodExpected> ExpectedByMethod,
    IReadOnlyCollection<PaymentMethodReconciliation> Reconciliations,
    IReadOnlyCollection<CashMovementResponse> Movements
);

public sealed record CashRegisterAvailabilityResponse(
    Guid RestaurantId,
    bool IsOpen,
    DateOnly BusinessDate,
    Guid? SessionId
);

public static class CashRegisterEndpoints
{
    private const string MadridTimeZone = "Europe/Madrid";

    public static void MapCashRegisterEndpoints(this WebApplication app)
    {
        RouteGroupBuilder publicGroup = app.MapGroup(
            "/api/cash-register/restaurants/{restaurantId:guid}"
        );
        publicGroup.MapGet("/is-open", IsOpen).AllowAnonymous();

        RouteGroupBuilder group = publicGroup.RequireAuthorization();
        group.MapGet("/current", Current);
        group.MapGet("/history", History);
        group.MapPost("/open", Open);
        group.MapPost("/sessions/{sessionId:guid}/close", Close);
    }

    private static async Task<IResult> IsOpen(
        Guid restaurantId,
        CashRegisterDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        DateOnly today = BusinessDate(time.GetUtcNow().UtcDateTime);
        await ExpirePreviousDays(db, restaurantId, today, time.GetUtcNow().UtcDateTime, ct);
        Guid? sessionId = await db
            .Sessions.AsNoTracking()
            .Where(x =>
                x.RestaurantId == restaurantId
                && x.Status == CashRegisterStatus.Open
                && x.BusinessDate == today
            )
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(ct);
        return Results.Ok(
            new CashRegisterAvailabilityResponse(restaurantId, sessionId.HasValue, today, sessionId)
        );
    }

    private static async Task<IResult> Current(
        Guid restaurantId,
        ClaimsPrincipal user,
        CashRegisterDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!CanManage(user, restaurantId))
        {
            return Results.Forbid();
        }

        DateOnly today = BusinessDate(time.GetUtcNow().UtcDateTime);
        await ExpirePreviousDays(db, restaurantId, today, time.GetUtcNow().UtcDateTime, ct);
        CashRegisterSession? session = await db
            .Sessions.AsNoTracking()
            .Include(x => x.Movements)
            .Include(x => x.Reconciliations)
            .SingleOrDefaultAsync(
                x =>
                    x.RestaurantId == restaurantId
                    && x.Status == CashRegisterStatus.Open
                    && x.BusinessDate == today,
                ct
            );
        return session is null ? Results.NoContent() : Results.Ok(ToResponse(session));
    }

    private static async Task<IResult> History(
        Guid restaurantId,
        ClaimsPrincipal user,
        CashRegisterDbContext db,
        CancellationToken ct
    )
    {
        if (!CanManage(user, restaurantId))
        {
            return Results.Forbid();
        }

        List<CashRegisterSession> sessions = await db
            .Sessions.AsNoTracking()
            .Include(x => x.Movements)
            .Include(x => x.Reconciliations)
            .Where(x => x.RestaurantId == restaurantId)
            .OrderByDescending(x => x.OpenedAtUtc)
            .Take(100)
            .ToListAsync(ct);
        return Results.Ok(sessions.Select(ToResponse));
    }

    private static async Task<IResult> Open(
        Guid restaurantId,
        OpenCashRegisterRequest request,
        ClaimsPrincipal user,
        CashRegisterDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!CanManage(user, restaurantId))
        {
            return Results.Forbid();
        }

        if (request.OpeningFloat is < 0 or > 100000)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["openingFloat"] = ["El fondo inicial debe estar entre 0 y 100.000 €."],
                }
            );
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        DateOnly today = BusinessDate(now);
        await ExpirePreviousDays(db, restaurantId, today, now, ct);
        if (
            await db.Sessions.AnyAsync(
                x => x.RestaurantId == restaurantId && x.Status == CashRegisterStatus.Open,
                ct
            )
        )
        {
            return Results.Conflict(
                new { detail = "Ya existe un turno de caja abierto para este local." }
            );
        }

        CashRegisterSession session = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            BusinessDate = today,
            Status = CashRegisterStatus.Open,
            OpeningFloat = request.OpeningFloat,
            OpenedAtUtc = now,
            OpenedByUserId = UserId(user),
            OpenedByName = user.Identity?.Name ?? "Usuario",
        };
        db.Sessions.Add(session);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: "23505" })
        {
            return Results.Conflict(
                new { detail = "Ya existe un turno de caja abierto para este local." }
            );
        }
        return Results.Created(
            $"/api/cash-register/restaurants/{restaurantId}/sessions/{session.Id}",
            ToResponse(session)
        );
    }

    private static async Task<IResult> Close(
        Guid restaurantId,
        Guid sessionId,
        CloseCashRegisterRequest request,
        ClaimsPrincipal user,
        CashRegisterDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!CanManage(user, restaurantId))
        {
            return Results.Forbid();
        }

        if (request.ReconciledByMethod is null || request.ReconciledByMethod.Count == 0)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["reconciledByMethod"] = ["Debes conciliar todos los medios de cobro."],
                }
            );
        }

        if (
            request.ReconciledByMethod.Any(x =>
                string.IsNullOrWhiteSpace(x.Key) || x.Value is < 0 or > 1000000
            )
        )
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["reconciledByMethod"] = ["Hay un importe de conciliación no válido."],
                }
            );
        }

        CashRegisterSession? session = await db
            .Sessions.Include(x => x.Movements)
            .Include(x => x.Reconciliations)
            .SingleOrDefaultAsync(x => x.Id == sessionId && x.RestaurantId == restaurantId, ct);
        if (session is null)
        {
            return Results.NotFound();
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        if (session.Status != CashRegisterStatus.Open)
        {
            return Results.Conflict(new { detail = "Este turno de caja ya no está abierto." });
        }

        if (session.BusinessDate != BusinessDate(now))
        {
            session.Status = CashRegisterStatus.Expired;
            session.ClosedAtUtc = now;
            session.ClosedByName = "Cierre automático por cambio de día";
            session.Version++;
            await db.SaveChangesAsync(ct);
            return Results.Conflict(
                new
                {
                    detail = "El turno pertenecía a un día anterior y ha caducado. Abre una caja nueva.",
                }
            );
        }
        Dictionary<string, decimal> expectedByMethod = ExpectedByMethod(session);
        Dictionary<string, decimal> reconciled = request.ReconciledByMethod.ToDictionary(
            x => x.Key.Trim(),
            x => x.Value,
            StringComparer.OrdinalIgnoreCase
        );
        string[] missing = expectedByMethod
            .Keys.Where(method => !reconciled.ContainsKey(method))
            .ToArray();
        if (missing.Length > 0)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["reconciledByMethod"] = [$"Falta conciliar: {string.Join(", ", missing)}."],
                }
            );
        }

        foreach ((string method, decimal expected) in expectedByMethod)
        {
            decimal actual = reconciled[method];
            db.Reconciliations.Add(
                new CashRegisterReconciliation
                {
                    Id = Guid.NewGuid(),
                    CashRegisterSessionId = session.Id,
                    Method = method,
                    ExpectedAmount = expected,
                    ReconciledAmount = actual,
                    Difference = actual - expected,
                }
            );
        }
        decimal expectedTotal = expectedByMethod.Values.Sum();
        decimal reconciledTotal = expectedByMethod.Keys.Sum(method => reconciled[method]);
        session.Status = CashRegisterStatus.Closed;
        session.CountedCash = reconciled.GetValueOrDefault("Cash");
        session.ExpectedCashAtClose = expectedByMethod.GetValueOrDefault("Cash");
        session.ExpectedTotalAtClose = expectedTotal;
        session.ReconciledTotalAtClose = reconciledTotal;
        session.DifferenceAtClose = reconciledTotal - expectedTotal;
        session.ClosedAtUtc = now;
        session.ClosedByUserId = UserId(user);
        session.ClosedByName = user.Identity?.Name ?? "Usuario";
        session.Version++;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(session));
    }

    internal static async Task ExpirePreviousDays(
        CashRegisterDbContext db,
        Guid restaurantId,
        DateOnly today,
        DateTime now,
        CancellationToken ct
    )
    {
        List<CashRegisterSession> stale = await db
            .Sessions.Where(x =>
                x.RestaurantId == restaurantId
                && x.Status == CashRegisterStatus.Open
                && x.BusinessDate != today
            )
            .ToListAsync(ct);
        if (stale.Count == 0)
        {
            return;
        }

        foreach (CashRegisterSession session in stale)
        {
            session.Status = CashRegisterStatus.Expired;
            session.ClosedAtUtc = now;
            session.ClosedByName = "Cierre automático por cambio de día";
            session.Version++;
        }
        await db.SaveChangesAsync(ct);
    }

    private static DateOnly BusinessDate(DateTime utc)
    {
        return DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, MadridTimeZone)
        );
    }

    private static bool CanManage(ClaimsPrincipal user, Guid restaurantId)
    {
        return user.CanAccessRestaurant(restaurantId, RestaurantPermissions.CashRegisterManage);
    }

    private static Guid UserId(ClaimsPrincipal user)
    {
        return Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id)
            ? id
            : throw new InvalidOperationException("El token no contiene usuario.");
    }

    private static decimal Signed(CashMovement x)
    {
        return x.Type == CashMovementType.Sale ? x.Amount : -x.Amount;
    }

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
        PaymentMethodExpected[] expectedMethods = expected
            .OrderBy(x => x.Key)
            .Select(x => new PaymentMethodExpected(x.Key, x.Value))
            .ToArray();
        PaymentMethodReconciliation[] reconciliations = session
            .Reconciliations.OrderBy(x => x.Method)
            .Select(x => new PaymentMethodReconciliation(
                x.Method,
                x.ExpectedAmount,
                x.ReconciledAmount,
                x.Difference
            ))
            .ToArray();
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
            expectedMethods,
            reconciliations,
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
}
