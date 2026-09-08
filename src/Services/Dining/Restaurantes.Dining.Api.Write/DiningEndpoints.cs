using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Restaurantes.Security;

namespace Restaurantes.Dining.Api.Write;

public sealed class CreateTableRequest
{
    public bool RequestGuestCount { get; init; }
    public Guid ZoneId { get; init; }

    [Required, StringLength(40, MinimumLength = 1)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(80, MinimumLength = 1)]
    public string Label { get; init; } = string.Empty;
}

public sealed class UpdateTableRequest
{
    public bool RequestGuestCount { get; init; }
    public Guid ZoneId { get; init; }

    [Required, StringLength(80, MinimumLength = 1)]
    public string Label { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;
}

public sealed class OpenSessionRequest
{
    [Required, RegularExpression("^(CustomerQr|WaiterMobile|Pos)$")]
    public string Source { get; init; } = "WaiterMobile";
}

public sealed class CheckoutSessionRequest
{
    public Guid IdempotencyKey { get; init; }

    [Required, RegularExpression("^(Card|Cash)$")]
    public string Method { get; init; } = "Card";

    [StringLength(120)]
    public string ExternalReference { get; init; } = string.Empty;
}

public sealed class CancelSessionRequest
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string Reason { get; init; } = string.Empty;
}

public sealed class UpdateDiningPolicyRequest
{
    public bool QrRequiresImmediatePayment { get; init; } = true;
    public bool RequireTrustedNetworkForQr { get; init; }
    public bool TakeawayRequiresPrepayment { get; init; } = true;
    public bool AllowCheckoutBeforeKitchenCompletion { get; init; }
    public string[] QrAllowedNetworks { get; init; } = [];
}

public static partial class DiningEndpoints
{
    public static IEndpointRouteBuilder MapDiningEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/dining");
        group.RequireAuthorization();
        MapZoneEndpoints(group);

        group.MapPost("/restaurants/{restaurantId:guid}/tables", CreateTable);
        group.MapGet("/restaurants/{restaurantId:guid}/tables", ListTables);
        group.MapPut("/tables/{tableId:guid}", UpdateTable);
        group.MapDelete("/tables/{tableId:guid}", DeleteTable);
        group.MapGet("/restaurants/{restaurantId:guid}/policy", GetPolicy);
        group.MapPut("/restaurants/{restaurantId:guid}/policy", UpdatePolicy);
        group.MapPost("/tables/{tableId:guid}/sessions", OpenSession);
        group.MapPost("/tables/{tableId:guid}/qr/rotate", RotateQr);
        group.MapGet("/qr/{qrCode}", PreviewQr).AllowAnonymous();
        group.MapPost("/qr/{qrCode}/sessions", OpenQrSession).AllowAnonymous();
        group.MapGet("/tables/{tableId:guid}/active-session", ActiveSession);
        group.MapGet("/sessions/{sessionId:guid}", GetSession);
        group.MapPut("/sessions/{sessionId:guid}/guests", SetGuestCount).AllowAnonymous();
        group.MapGet("/sessions/{sessionId:guid}/bill", GetBill);
        group.MapPost("/sessions/{sessionId:guid}/checkout", Checkout);
        group.MapGet("/sessions/{sessionId:guid}/validate", ValidateSession).AllowAnonymous();
        group.MapPost("/sessions/{sessionId:guid}/close", CloseSession);
        group.MapPost("/sessions/{sessionId:guid}/cancel", CancelSession);
        return app;
    }

    private static async Task<IResult> CreateTable(
        Guid restaurantId,
        CreateTableRequest request,
        ClaimsPrincipal principal,
        DiningDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (restaurantId == Guid.Empty)
        {
            return Results.BadRequest(new { detail = "RestaurantId is required." });
        }

        if (!principal.CanAccessRestaurant(restaurantId, RestaurantPermissions.TablesManage))
        {
            return Results.Forbid();
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        DiningZone? zone = await LockZone(db, restaurantId, request.ZoneId, ct);
        if (zone is null || zone.DeletedAtUtc is not null)
        {
            return Results.BadRequest(
                new { detail = "Select a zone belonging to this restaurant." }
            );
        }

        RestaurantTable table = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            ZoneId = zone.Id,
            Zone = zone,
            RequestGuestCount = request.RequestGuestCount,
            Code = request.Code.Trim().ToUpperInvariant(),
            Label = request.Label.Trim(),
            QrCode = NewQrCode(),
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        db.Tables.Add(table);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Created($"/api/dining/tables/{table.Id}", TableResponse(table, null));
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: "23505" })
        {
            return Results.Conflict(
                new { detail = $"Table code '{table.Code}' already exists in this restaurant." }
            );
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: "23503" })
        {
            return Results.Conflict(
                new { detail = "The selected zone was deleted. Refresh and retry." }
            );
        }
    }

    private static async Task<IResult> GetPolicy(
        Guid restaurantId,
        ClaimsPrincipal principal,
        DiningDbContext db,
        CancellationToken ct
    )
    {
        if (!principal.CanAccessRestaurant(restaurantId, RestaurantPermissions.TablesRead))
        {
            return Results.Forbid();
        }

        DiningRestaurantPolicy? policy = await db
            .RestaurantPolicies.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RestaurantId == restaurantId, ct);
        return Results.Ok(PolicyResponse(restaurantId, policy));
    }

    private static async Task<IResult> UpdateTable(
        Guid tableId,
        UpdateTableRequest request,
        ClaimsPrincipal principal,
        DiningDbContext db,
        CancellationToken ct
    )
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        RestaurantTable? table = await db
            .Tables.FromSqlInterpolated(
                $"SELECT * FROM restaurant_tables WHERE \"Id\" = {tableId} FOR UPDATE"
            )
            .SingleOrDefaultAsync(ct);
        if (table is null || table.DeletedAtUtc is not null)
        {
            return Results.NotFound();
        }

        if (!principal.CanAccessRestaurant(table.RestaurantId, RestaurantPermissions.TablesManage))
        {
            return Results.Forbid();
        }
        bool occupied = await db.Sessions.AnyAsync(
            x => x.TableId == tableId && x.Status == "Open",
            ct
        );
        if (occupied && !request.IsActive)
        {
            return Results.Conflict(new { detail = "An occupied location cannot be disabled." });
        }
        DiningZone? zone = await LockZone(db, table.RestaurantId, request.ZoneId, ct);
        if (zone is null || zone.DeletedAtUtc is not null)
        {
            return Results.BadRequest(
                new { detail = "Select a zone belonging to this restaurant." }
            );
        }

        table.ZoneId = zone.Id;
        table.Zone = zone;
        table.RequestGuestCount = request.RequestGuestCount;
        table.Label = request.Label.Trim();
        table.IsActive = request.IsActive;
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: "23503" })
        {
            return Results.Conflict(
                new { detail = "The selected zone was deleted. Refresh and retry." }
            );
        }
        DiningSession? session = occupied
            ? await db
                .Sessions.AsNoTracking()
                .SingleAsync(x => x.TableId == tableId && x.Status == "Open", ct)
            : null;
        return Results.Ok(TableResponse(table, session));
    }

    private static async Task<IResult> UpdatePolicy(
        Guid restaurantId,
        UpdateDiningPolicyRequest request,
        ClaimsPrincipal principal,
        DiningDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!principal.CanAccessRestaurant(restaurantId, RestaurantPermissions.TablesManage))
        {
            return Results.Forbid();
        }

        if (
            !QrNetworkAccess.TryNormalize(
                request.QrAllowedNetworks,
                out string allowedNetworks,
                out string? networkError
            )
        )
        {
            return Results.BadRequest(new { detail = networkError });
        }
        if (request.RequireTrustedNetworkForQr && allowedNetworks.Length == 0)
        {
            return Results.BadRequest(
                new
                {
                    detail = "At least one allowed IP address or CIDR network is required when QR network validation is enabled.",
                }
            );
        }

        DiningRestaurantPolicy? policy = await db.RestaurantPolicies.FindAsync([restaurantId], ct);
        if (policy is null)
        {
            policy = new DiningRestaurantPolicy { RestaurantId = restaurantId, Version = 1 };
            db.RestaurantPolicies.Add(policy);
        }
        else
        {
            policy.Version++;
        }

        policy.QrRequiresImmediatePayment = request.QrRequiresImmediatePayment;
        policy.RequireTrustedNetworkForQr = request.RequireTrustedNetworkForQr;
        policy.TakeawayRequiresPrepayment = request.TakeawayRequiresPrepayment;
        policy.AllowCheckoutBeforeKitchenCompletion = request.AllowCheckoutBeforeKitchenCompletion;
        policy.QrAllowedNetworks = allowedNetworks;
        policy.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Results.Ok(PolicyResponse(restaurantId, policy));
    }

    private static async Task<IResult> ListTables(
        Guid restaurantId,
        ClaimsPrincipal principal,
        DiningDbContext db,
        CancellationToken ct
    )
    {
        if (!principal.CanAccessRestaurant(restaurantId, RestaurantPermissions.TablesRead))
        {
            return Results.Forbid();
        }

        Dictionary<Guid, DiningSession> active = await db
            .Sessions.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.Status == "Open")
            .ToDictionaryAsync(x => x.TableId, ct);
        List<RestaurantTable> tables = await db
            .Tables.AsNoTracking()
            .Include(x => x.Zone)
            .Where(x => x.RestaurantId == restaurantId && x.DeletedAtUtc == null)
            .OrderBy(x => x.Code)
            .ToListAsync(ct);
        return Results.Ok(tables.Select(x => TableResponse(x, active.GetValueOrDefault(x.Id))));
    }

    private static async Task<IResult> OpenSession(
        Guid tableId,
        OpenSessionRequest request,
        ClaimsPrincipal principal,
        DiningDbContext db,
        CashRegisterAvailabilityClient cashRegister,
        IHubContext<DiningHub, IDiningRealtimeClient> realtime,
        TimeProvider time,
        CancellationToken ct
    )
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        RestaurantTable? table = await db
            .Tables.FromSqlInterpolated(
                $"SELECT * FROM restaurant_tables WHERE \"Id\" = {tableId} FOR UPDATE"
            )
            .SingleOrDefaultAsync(ct);
        if (table is null || table.DeletedAtUtc is not null)
        {
            return Results.NotFound();
        }

        if (!principal.CanAccessRestaurant(table.RestaurantId, RestaurantPermissions.OrdersCreate))
        {
            return Results.Forbid();
        }

        if (!table.IsActive)
        {
            return Results.Conflict(new { detail = "The table is not active." });
        }

        try
        {
            await cashRegister.EnsureOpenAsync(table.RestaurantId, ct);
        }
        catch (CashRegisterClosedException exception)
        {
            return Results.Conflict(new { detail = exception.Message });
        }

        DiningSession session = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = table.RestaurantId,
            TableId = table.Id,
            RequestGuestCount = table.RequestGuestCount,
            Source = request.Source,
            OpenedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        db.Sessions.Add(session);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await PublishTableChanged(realtime, session, "Occupied", time);
            return Results.Created($"/api/dining/sessions/{session.Id}", SessionResponse(session));
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: "23505" })
        {
            return Results.Conflict(
                new { detail = "This table already has an open dining session." }
            );
        }
    }

    private static async Task<IResult> RotateQr(
        Guid tableId,
        ClaimsPrincipal principal,
        DiningDbContext db,
        CancellationToken ct
    )
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        RestaurantTable? table = await db
            .Tables.FromSqlInterpolated(
                $"SELECT * FROM restaurant_tables WHERE \"Id\" = {tableId} FOR UPDATE"
            )
            .SingleOrDefaultAsync(ct);
        if (table is null || table.DeletedAtUtc is not null)
        {
            return Results.NotFound();
        }

        if (!principal.CanAccessRestaurant(table.RestaurantId, RestaurantPermissions.TablesManage))
        {
            return Results.Forbid();
        }

        table.QrCode = NewQrCode();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(QrResponse(table));
    }

    private static async Task<IResult> OpenQrSession(
        string qrCode,
        int? guestCount,
        HttpContext httpContext,
        DiningDbContext db,
        CashRegisterAvailabilityClient cashRegister,
        IHubContext<DiningHub, IDiningRealtimeClient> realtime,
        TimeProvider time,
        CancellationToken ct
    )
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        string normalizedQrCode = qrCode.Trim().ToUpperInvariant();
        RestaurantTable? table = await db
            .Tables.FromSqlInterpolated(
                $"SELECT * FROM restaurant_tables WHERE \"QrCode\" = {normalizedQrCode} FOR UPDATE"
            )
            .SingleOrDefaultAsync(ct);
        if (table is null || table.DeletedAtUtc is not null)
        {
            return Results.NotFound(new { detail = "The table QR code is invalid." });
        }

        if (!table.IsActive)
        {
            return Results.Conflict(new { detail = "The table is not active." });
        }

        try
        {
            await cashRegister.EnsureOpenAsync(table.RestaurantId, ct);
        }
        catch (CashRegisterClosedException exception)
        {
            return Results.Conflict(new { detail = exception.Message });
        }

        DiningRestaurantPolicy? policy = await db
            .RestaurantPolicies.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RestaurantId == table.RestaurantId, ct);
        if (
            policy?.RequireTrustedNetworkForQr == true
            && !QrNetworkAccess.IsAllowed(
                httpContext.Connection.RemoteIpAddress,
                policy.QrAllowedNetworks
            )
        )
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Restaurant presence required",
                detail: "This table QR can only be used from an authorized restaurant network."
            );
        }

        DiningSession? existing = await db
            .Sessions.Include(x => x.Orders)
            .SingleOrDefaultAsync(x => x.TableId == table.Id && x.Status == "Open", ct);
        if (existing is not null)
        {
            if (existing.Source != "CustomerQr")
            {
                return Results.Conflict(
                    new { detail = "The table is currently managed by restaurant staff." }
                );
            }

            string? suppliedToken = httpContext
                .Request.Headers["X-Customer-Session-Token"]
                .FirstOrDefault();
            return TokenEquals(existing.CustomerAccessToken, suppliedToken)
                ? Results.Ok(QrSessionResponse(table, existing, policy))
                : Results.Conflict(
                    new { detail = "This table already has an active customer QR session." }
                );
        }

        if ((table.RequestGuestCount && guestCount is null) || guestCount is < 1 or > 999)
        {
            return Results.BadRequest(
                new { detail = "Indica entre 1 y 999 comensales antes de pedir." }
            );
        }

        DiningSession session = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = table.RestaurantId,
            TableId = table.Id,
            RequestGuestCount = table.RequestGuestCount,
            GuestCount = guestCount,
            Source = "CustomerQr",
            CustomerAccessToken = NewCustomerAccessToken(),
            OpenedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        db.Sessions.Add(session);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await PublishTableChanged(realtime, session, "Occupied", time);
            return Results.Created(
                $"/api/dining/sessions/{session.Id}",
                QrSessionResponse(table, session, policy)
            );
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: "23505" })
        {
            await transaction.RollbackAsync(ct);
            db.Entry(session).State = EntityState.Detached;
            existing = await db
                .Sessions.AsNoTracking()
                .Include(x => x.Orders)
                .SingleOrDefaultAsync(x => x.TableId == table.Id && x.Status == "Open", ct);
            return Results.Conflict(new { detail = "This table already has an open session." });
        }
    }

    private static async Task<IResult> ActiveSession(
        Guid tableId,
        ClaimsPrincipal principal,
        DiningDbContext db,
        CancellationToken ct
    )
    {
        DiningSession? session = await db
            .Sessions.AsNoTracking()
            .Include(x => x.Orders)
            .SingleOrDefaultAsync(x => x.TableId == tableId && x.Status == "Open", ct);
        return session is null ? Results.NotFound()
            : principal.CanAccessRestaurant(session.RestaurantId, RestaurantPermissions.TablesRead)
                ? Results.Ok(SessionResponse(session))
            : Results.Forbid();
    }

    private static async Task<IResult> GetSession(
        Guid sessionId,
        ClaimsPrincipal principal,
        DiningDbContext db,
        CancellationToken ct
    )
    {
        DiningSession? session = await db
            .Sessions.AsNoTracking()
            .Include(x => x.Orders)
            .SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        return session is null ? Results.NotFound()
            : principal.CanAccessRestaurant(session.RestaurantId, RestaurantPermissions.TablesRead)
                ? Results.Ok(SessionResponse(session))
            : Results.Forbid();
    }

    private static async Task<IResult> ValidateSession(
        Guid sessionId,
        Guid restaurantId,
        Guid tableId,
        string? source,
        string? serviceMode,
        HttpContext httpContext,
        DiningDbContext db,
        CancellationToken ct
    )
    {
        DiningSession? session = await db
            .Sessions.AsNoTracking()
            .SingleOrDefaultAsync(
                x =>
                    x.Id == sessionId
                    && x.RestaurantId == restaurantId
                    && x.TableId == tableId
                    && x.Status == "Open",
                ct
            );
        bool valid = session is not null;
        if (valid && session!.RequestGuestCount && session.GuestCount is null)
        {
            return Results.Conflict(
                new { detail = "Indica la cantidad de comensales antes de pedir." }
            );
        }

        if (valid)
        {
            // Bar remains accepted for historical orders; locations no longer have types.
            valid = serviceMode is "DineIn" or "Bar";
        }
        if (valid && string.Equals(source, "CustomerQr", StringComparison.Ordinal))
        {
            string? customerAccessToken = httpContext
                .Request.Headers["X-Customer-Session-Token"]
                .FirstOrDefault();
            valid =
                session!.Source == "CustomerQr"
                && TokenEquals(session.CustomerAccessToken, customerAccessToken);
        }

        if (valid && session!.GuestCount is int count)
        {
            httpContext.Response.Headers["X-Dining-Guest-Count"] = count.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );
        }

        return valid
            ? Results.NoContent()
            : Results.Conflict(
                new
                {
                    detail = "The dining session is not open or does not belong to this restaurant and table.",
                }
            );
    }

    private static async Task<IResult> GetBill(
        Guid sessionId,
        ClaimsPrincipal principal,
        DiningDbContext db,
        CancellationToken ct
    )
    {
        DiningSession? session = await db
            .Sessions.AsNoTracking()
            .Include(x => x.Orders)
            .SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        if (session is null)
        {
            return Results.NotFound();
        }
        if (!principal.CanAccessRestaurant(session.RestaurantId, RestaurantPermissions.TablesRead))
        {
            return Results.Forbid();
        }

        bool allowCheckoutBeforeKitchenCompletion = await db
            .RestaurantPolicies.AsNoTracking()
            .Where(x => x.RestaurantId == session.RestaurantId)
            .Select(x => x.AllowCheckoutBeforeKitchenCompletion)
            .SingleOrDefaultAsync(ct);
        return Results.Ok(BillResponse(session, allowCheckoutBeforeKitchenCompletion));
    }

    private static async Task<IResult> Checkout(
        Guid sessionId,
        CheckoutSessionRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        IHttpClientFactory clients,
        DiningDbContext db,
        IHubContext<DiningHub, IDiningRealtimeClient> realtime,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (request.IdempotencyKey == Guid.Empty)
        {
            return Results.BadRequest(new { detail = "IdempotencyKey is required." });
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        DiningSession? session = await db
            .Sessions.FromSqlInterpolated(
                $"SELECT * FROM dining_sessions WHERE \"Id\" = {sessionId} FOR UPDATE"
            )
            .SingleOrDefaultAsync(ct);
        if (session is null)
        {
            return Results.NotFound();
        }

        await db.Entry(session).Collection(x => x.Orders).LoadAsync(ct);

        if (
            !principal.CanAccessRestaurant(
                session.RestaurantId,
                RestaurantPermissions.PaymentsCapture
            )
        )
        {
            return Results.Forbid();
        }

        if (session.Status == "Closed")
        {
            return session.CheckoutIdempotencyKey == request.IdempotencyKey
                ? Results.Ok(BillResponse(session, false))
                : Results.Conflict(new { detail = "The dining session is already closed." });
        }
        if (session.Orders.Count == 0)
        {
            return Results.Conflict(new { detail = "The dining session has no projected orders." });
        }

        bool allowCheckoutBeforeKitchenCompletion = await db
            .RestaurantPolicies.AsNoTracking()
            .Where(x => x.RestaurantId == session.RestaurantId)
            .Select(x => x.AllowCheckoutBeforeKitchenCompletion)
            .SingleOrDefaultAsync(ct);
        if (!allowCheckoutBeforeKitchenCompletion)
        {
            Guid[] notDelivered = session
                .Orders.Where(x => x.OrderStatus is not ("Ready" or "Delivered" or "Cancelled"))
                .Select(x => x.OrderId)
                .ToArray();
            if (notDelivered.Length > 0)
            {
                return Results.Conflict(
                    new
                    {
                        detail = "All orders must be delivered before checkout.",
                        orderIds = notDelivered,
                    }
                );
            }
        }

        List<DiningSessionOrder> unpaidOrders = session
            .Orders.Where(x => x.PaymentStatus != "Paid" && x.OrderStatus != "Cancelled")
            .ToList();
        HttpClient payments = clients.CreateClient("payments");
        string? authorization = httpContext.Request.Headers.Authorization.FirstOrDefault();
        foreach (DiningSessionOrder order in unpaidOrders)
        {
            using HttpRequestMessage payment = new(
                HttpMethod.Post,
                $"/api/payments/orders/{order.OrderId}/capture"
            );
            if (
                AuthenticationHeaderValue.TryParse(
                    authorization,
                    out AuthenticationHeaderValue? header
                )
            )
            {
                payment.Headers.Authorization = header;
            }

            payment.Content = JsonContent.Create(
                new
                {
                    request.IdempotencyKey,
                    transactionOrderCount = unpaidOrders.Count,
                    request.Method,
                    externalReference = string.IsNullOrWhiteSpace(request.ExternalReference)
                        ? $"DINING-{session.Id:N}"
                        : request.ExternalReference.Trim(),
                }
            );
            using HttpResponseMessage response = await payments.SendAsync(payment, ct);
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(ct);
                return Results.Conflict(
                    new
                    {
                        detail = $"Payment for order '{order.OrderId}' was rejected with HTTP {(int)response.StatusCode}.",
                        paymentResponse = body,
                    }
                );
            }
            order.PaymentStatus = "Paid";
            order.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        session.CheckoutIdempotencyKey = request.IdempotencyKey;
        session.PaymentMethod = request.Method;
        session.PaidAtUtc = now;
        session.Status = "Closed";
        session.ClosedAtUtc = now;
        session.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await PublishTableChanged(realtime, session, "Available", time);
        return Results.Ok(BillResponse(session, allowCheckoutBeforeKitchenCompletion));
    }

    private static async Task<IResult> CloseSession(
        Guid sessionId,
        ClaimsPrincipal principal,
        DiningDbContext db,
        IHubContext<DiningHub, IDiningRealtimeClient> realtime,
        TimeProvider time,
        CancellationToken ct
    )
    {
        DiningSession? session = await db
            .Sessions.Include(x => x.Orders)
            .SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        if (session is null)
        {
            return Results.NotFound();
        }

        if (
            !principal.CanAccessRestaurant(
                session.RestaurantId,
                RestaurantPermissions.TablesRelease
            )
        )
        {
            return Results.Forbid();
        }

        if (session.Status != "Open")
        {
            return Results.Conflict(new { detail = "The dining session is already closed." });
        }

        if (session.Orders.Count == 0)
        {
            return Results.Conflict(new { detail = "The dining session has no orders." });
        }

        Guid[] unpaid = session
            .Orders.Where(x => x.PaymentStatus != "Paid" && x.OrderStatus != "Cancelled")
            .Select(x => x.OrderId)
            .ToArray();
        if (unpaid.Length > 0)
        {
            return Results.Conflict(
                new
                {
                    detail = "All active session orders must be paid before releasing the table.",
                    unpaidOrderIds = unpaid,
                }
            );
        }

        session.Status = "Closed";
        session.ClosedAtUtc = time.GetUtcNow().UtcDateTime;
        session.Version++;
        await db.SaveChangesAsync(ct);
        await PublishTableChanged(realtime, session, "Available", time);
        return Results.Ok(SessionResponse(session));
    }

    private static async Task<IResult> CancelSession(
        Guid sessionId,
        CancelSessionRequest request,
        ClaimsPrincipal principal,
        DiningDbContext db,
        IHubContext<DiningHub, IDiningRealtimeClient> realtime,
        TimeProvider time,
        CancellationToken ct
    )
    {
        DiningSession? session = await db
            .Sessions.Include(x => x.Orders)
            .SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        if (session is null)
        {
            return Results.NotFound();
        }

        if (
            !principal.CanAccessRestaurant(
                session.RestaurantId,
                RestaurantPermissions.TablesRelease
            )
        )
        {
            return Results.Forbid();
        }

        if (session.Status != "Open")
        {
            return Results.Conflict(
                new { detail = "Only an open dining session can be cancelled." }
            );
        }

        if (session.Orders.Count > 0)
        {
            return Results.Conflict(
                new
                {
                    detail = "A dining session with orders cannot be cancelled. Complete its payment and close it normally.",
                }
            );
        }

        string reason = request.Reason.Trim();
        if (reason.Length is < 3 or > 200)
        {
            return Results.BadRequest(
                new { detail = "A cancellation reason between 3 and 200 characters is required." }
            );
        }

        if (
            !Guid.TryParse(
                principal.FindFirstValue(ClaimTypes.NameIdentifier),
                out Guid cancelledByUserId
            )
        )
        {
            return Results.Forbid();
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        session.Status = "Cancelled";
        session.CancellationReason = reason;
        session.CancelledByUserId = cancelledByUserId;
        session.CancelledAtUtc = now;
        session.ClosedAtUtc = now;
        session.Version++;
        await db.SaveChangesAsync(ct);
        await PublishTableChanged(realtime, session, "Available", time);
        return Results.Ok(SessionResponse(session));
    }

    private static Task PublishTableChanged(
        IHubContext<DiningHub, IDiningRealtimeClient> realtime,
        DiningSession session,
        string status,
        TimeProvider time
    )
    {
        return realtime
            .Clients.Group(DiningHub.RestaurantGroup(session.RestaurantId))
            .TableChanged(
                new DiningTableChanged(
                    session.RestaurantId,
                    session.TableId,
                    status,
                    status == "Occupied" ? session.Id : null,
                    session.Source,
                    time.GetUtcNow().UtcDateTime
                )
            );
    }

    private static object TableResponse(RestaurantTable table, DiningSession? session)
    {
        return new
        {
            table.Id,
            table.RestaurantId,
            table.Code,
            table.Label,
            table.ZoneId,
            table.RequestGuestCount,
            zoneName = table.Zone.Name,
            zoneSortOrder = table.Zone.SortOrder,
            table.QrCode,
            qrPath = $"/api/dining/qr/{table.QrCode}/sessions",
            customerQrPath = $"/qr/{table.QrCode}",
            table.IsActive,
            status = session is null ? "Available" : "Occupied",
            activeSessionId = session?.Id,
        };
    }

    private static object QrResponse(RestaurantTable table)
    {
        return new
        {
            table.Id,
            table.RestaurantId,
            table.Code,
            table.Label,
            table.ZoneId,
            table.RequestGuestCount,
            table.QrCode,
            qrPath = $"/api/dining/qr/{table.QrCode}/sessions",
            customerQrPath = $"/qr/{table.QrCode}",
        };
    }

    private static object QrSessionResponse(
        RestaurantTable table,
        DiningSession session,
        DiningRestaurantPolicy? policy
    )
    {
        return new
        {
            table = new
            {
                table.Id,
                table.RestaurantId,
                table.Code,
                table.Label,
                table.ZoneId,
                table.RequestGuestCount,
            },
            session = SessionResponse(session),
            customerAccessToken = session.CustomerAccessToken,
            qrRequiresImmediatePayment = policy?.QrRequiresImmediatePayment ?? true,
        };
    }

    private static object PolicyResponse(Guid restaurantId, DiningRestaurantPolicy? policy)
    {
        return new
        {
            restaurantId,
            qrRequiresImmediatePayment = policy?.QrRequiresImmediatePayment ?? true,
            requireTrustedNetworkForQr = policy?.RequireTrustedNetworkForQr ?? false,
            takeawayRequiresPrepayment = policy?.TakeawayRequiresPrepayment ?? true,
            allowCheckoutBeforeKitchenCompletion = policy?.AllowCheckoutBeforeKitchenCompletion
                ?? false,
            qrAllowedNetworks = QrNetworkAccess.ToResponse(
                policy?.QrAllowedNetworks ?? string.Empty
            ),
            version = policy?.Version ?? 0,
            updatedAtUtc = policy?.UpdatedAtUtc,
        };
    }

    private static string NewQrCode()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }

    private static string NewCustomerAccessToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    private static bool TokenEquals(string expected, string? supplied)
    {
        if (expected.Length == 0 || string.IsNullOrWhiteSpace(supplied))
        {
            return false;
        }

        byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        byte[] suppliedBytes = System.Text.Encoding.UTF8.GetBytes(supplied.Trim());
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private static object SessionResponse(DiningSession session)
    {
        return new
        {
            session.Id,
            session.RestaurantId,
            session.TableId,
            session.RequestGuestCount,
            session.GuestCount,
            session.Source,
            session.Status,
            session.Version,
            session.OpenedAtUtc,
            session.ClosedAtUtc,
            session.PaidAtUtc,
            session.PaymentMethod,
            session.CancelledAtUtc,
            session.CancelledByUserId,
            session.CancellationReason,
            orders = session
                .Orders.OrderBy(x => x.AddedAtUtc)
                .Select(x => new
                {
                    x.OrderId,
                    x.OrderStatus,
                    x.PaymentStatus,
                    x.Amount,
                }),
        };
    }

    private static object BillResponse(
        DiningSession session,
        bool allowCheckoutBeforeKitchenCompletion
    )
    {
        return new
        {
            session.Id,
            session.RestaurantId,
            session.TableId,
            session.RequestGuestCount,
            session.GuestCount,
            session.Status,
            session.PaymentMethod,
            session.PaidAtUtc,
            total = session.Orders.Sum(x => x.Amount),
            outstanding = session.Orders.Where(x => x.PaymentStatus != "Paid").Sum(x => x.Amount),
            canCheckout = session.Status == "Open"
                && session.Orders.Count > 0
                && (
                    allowCheckoutBeforeKitchenCompletion
                    || session.Orders.All(x =>
                        x.OrderStatus is "Ready" or "Delivered" or "Cancelled"
                    )
                ),
            orders = session
                .Orders.OrderBy(x => x.AddedAtUtc)
                .Select(x => new
                {
                    x.OrderId,
                    x.OrderStatus,
                    x.PaymentStatus,
                    x.Amount,
                    x.AddedAtUtc,
                }),
        };
    }
}
