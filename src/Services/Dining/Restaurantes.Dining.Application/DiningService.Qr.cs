using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Application;

public sealed partial class DiningService
{
    private static readonly TimeSpan QrReservationDuration = TimeSpan.FromMinutes(5);

    public async Task<DiningResult> PreviewQr(
        string qrCode,
        DiningRequest httpContext,
        CancellationToken ct
    )
    {
        string code = qrCode.Trim().ToUpperInvariant();
        RestaurantTable? table = await db.ReadQrTableAsync(code, ct);
        if (table is null)
        {
            return DiningResults.NotFound(new { detail = "El QR no está disponible." });
        }

        bool restaurantOpen = true;
        try
        {
            await cashRegister.EnsureOpenAsync(table.RestaurantId, ct);
        }
        catch (DiningCashRegisterClosedException)
        {
            restaurantOpen = false;
        }

        DiningSession? session = await db.ReadActiveSessionWithOrdersAsync(table.Id, ct);
        // Only the holder of this session's token may recover its private account.
        if (
            session?.Source == "CustomerQr"
            && TokenEquals(session.CustomerAccessToken, httpContext.CustomerSessionToken)
        )
        {
            if (IsQrReservationExpired(session, time.GetUtcNow().UtcDateTime))
            {
                return DiningResults.Gone(
                    new { detail = "La reserva del QR caducó. Vuelve a escanear el código." }
                );
            }

            return DiningResults.Ok(QrSessionResponse(table, session, restaurantOpen));
        }

        if (!string.IsNullOrWhiteSpace(httpContext.CustomerSessionToken))
        {
            DiningSession? previousSession = await db.FindQrSessionByTokenAsync(
                table.Id,
                httpContext.CustomerSessionToken.Trim(),
                ct
            );
            if (previousSession is not null && previousSession.Status != "Open")
            {
                return DiningResults.Gone(
                    new { detail = "La sesión QR terminó. Vuelve a escanear el código." }
                );
            }
        }

        return DiningResults.Ok(
            new
            {
                table = new
                {
                    table.Id,
                    table.RestaurantId,
                    table.Code,
                    table.Label,
                },
                session = (object?)null,
                customerAccessToken = string.Empty,
                isRestaurantOpen = restaurantOpen,
            }
        );
    }

    public async Task<DiningResult> OpenQrSession(
        string qrCode,
        DiningRequest httpContext,
        CancellationToken ct
    )
    {
        await using IDiningTransaction transaction = await db.BeginTransactionAsync(ct);
        string normalizedQrCode = qrCode.Trim().ToUpperInvariant();
        RestaurantTable? table = await db.LockQrTableAsync(normalizedQrCode, ct);
        if (table is null || table.DeletedAtUtc is not null)
        {
            return DiningResults.NotFound(new { detail = "The table QR code is invalid." });
        }

        if (!table.IsActive)
        {
            return DiningResults.Conflict(new { detail = "The table is not active." });
        }

        try
        {
            await cashRegister.EnsureOpenAsync(table.RestaurantId, ct);
        }
        catch (DiningCashRegisterClosedException exception)
        {
            return DiningResults.Conflict(new { detail = exception.Message });
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        DiningSession? existing = await db.FindActiveSessionWithOrdersAsync(table.Id, ct);
        if (existing is not null)
        {
            if (existing.Source != "CustomerQr")
            {
                return DiningResults.Conflict(
                    new { detail = "The table is currently managed by restaurant staff." }
                );
            }

            string? suppliedToken = httpContext.CustomerSessionToken;
            if (IsQrReservationExpired(existing, now))
            {
                existing.ExpireQrReservation(now);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                if (!TokenEquals(existing.CustomerAccessToken, suppliedToken))
                {
                    return DiningResults.Conflict(
                        new
                        {
                            detail = "La mesa ya está reservada por otro cliente o está ocupada.",
                        }
                    );
                }

                existing.RenewQrReservation(now.Add(QrReservationDuration));
                await db.SaveChangesAsync(ct);
                return DiningResults.Ok(QrSessionResponse(table, existing));
            }
        }

        DiningSession session = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = table.RestaurantId,
            TableId = table.Id,
            RequestGuestCount = false,
            Source = "CustomerQr",
            CustomerAccessToken = NewCustomerAccessToken(),
            QrReservationExpiresAtUtc = now.Add(QrReservationDuration),
            OpenedAtUtc = now,
        };
        db.Add(session);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return DiningResults.Created(
                $"/api/dining/sessions/{session.Id}",
                QrSessionResponse(table, session),
                table.RestaurantId
            );
        }
        catch (DiningStoreException e) when (e.Failure == DiningStoreFailure.Duplicate)
        {
            await transaction.RollbackAsync(ct);
            db.Detach(session);
            existing = await db.ReadActiveSessionWithOrdersAsync(table.Id, ct);
            return DiningResults.Conflict(
                new { detail = "This table already has an open session." }
            );
        }
    }

    public async Task<DiningResult> TouchQrSession(
        Guid sessionId,
        DiningRequest httpContext,
        CancellationToken ct
    )
    {
        await using IDiningTransaction transaction = await db.BeginTransactionAsync(ct);
        DiningSession? session = await db.LockSessionAsync(sessionId, ct);
        if (
            session is null
            || session.Source != "CustomerQr"
            || !TokenEquals(session.CustomerAccessToken, httpContext.CustomerSessionToken)
        )
        {
            return DiningResults.Gone(
                new { detail = "La sesión QR terminó. Vuelve a escanear el código." }
            );
        }

        if (session.Status != "Open")
        {
            return DiningResults.Gone(
                new { detail = "La mesa fue liberada. Vuelve a escanear el código." }
            );
        }

        await db.LoadOrdersAsync(session, ct);
        DateTime now = time.GetUtcNow().UtcDateTime;
        if (IsQrReservationExpired(session, now))
        {
            session.ExpireQrReservation(now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return DiningResults.Gone(
                new { detail = "La reserva del QR caducó. Vuelve a escanear el código." },
                session.RestaurantId
            );
        }

        if (session.HasConfirmedQrOrder)
        {
            session.ConfirmQrOrder();
        }
        else
        {
            session.RenewQrReservation(now.Add(QrReservationDuration));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return DiningResults.Ok(new { session.Id, session.QrReservationExpiresAtUtc });
    }

    public async Task<List<Guid>> ExpireQrReservations(CancellationToken ct)
    {
        DateTime now = time.GetUtcNow().UtcDateTime;
        List<Guid> expiredRestaurants = [];
        List<Guid> sessionIds = await db.FindExpiredQrReservationIdsAsync(now, ct);
        foreach (Guid sessionId in sessionIds)
        {
            await using IDiningTransaction transaction = await db.BeginTransactionAsync(ct);
            DiningSession? session = await db.LockSessionAsync(sessionId, ct);
            if (session is null || session.Status != "Open" || session.Source != "CustomerQr")
            {
                continue;
            }

            await db.LoadOrdersAsync(session, ct);
            if (IsQrReservationExpired(session, now))
            {
                session.ExpireQrReservation(now);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                expiredRestaurants.Add(session.RestaurantId);
            }
        }

        return expiredRestaurants.Distinct().ToList();
    }

    private static bool IsQrReservationExpired(DiningSession session, DateTime nowUtc) =>
        session.QrReservationExpiresAtUtc is DateTime expiresAtUtc
        && expiresAtUtc <= nowUtc
        && !session.HasConfirmedQrOrder;
}
