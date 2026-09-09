using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Application;

public sealed partial class DiningService
{
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

        DiningRestaurantPolicy? policy = await db.ReadPolicyAsync(table.RestaurantId, ct);
        if (
            policy?.RequireTrustedNetworkForQr == true
            && !QrNetworkAccess.IsAllowed(httpContext.RemoteIpAddress, policy.QrAllowedNetworks)
        )
        {
            return DiningResults.Problem(
                statusCode: 403,
                detail: "Conéctate a la red autorizada del restaurante."
            );
        }

        DiningSession? session = await db.ReadActiveSessionWithOrdersAsync(table.Id, ct);
        // Only the holder of this session's token may recover its private account.
        return
            session?.Source == "CustomerQr"
            && TokenEquals(session.CustomerAccessToken, httpContext.CustomerSessionToken)
            ? DiningResults.Ok(QrSessionResponse(table, session, policy))
            : DiningResults.Ok(
                new
                {
                    table = new
                    {
                        table.Id,
                        table.RestaurantId,
                        table.Code,
                        table.Label,
                        table.RequestGuestCount,
                    },
                    session = (object?)null,
                    customerAccessToken = string.Empty,
                    qrRequiresImmediatePayment = policy?.QrRequiresImmediatePayment ?? true,
                }
            );
    }

    public async Task<DiningResult> OpenQrSession(
        string qrCode,
        int? guestCount,
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

        DiningRestaurantPolicy? policy = await db.ReadPolicyAsync(table.RestaurantId, ct);
        if (
            policy?.RequireTrustedNetworkForQr == true
            && !QrNetworkAccess.IsAllowed(httpContext.RemoteIpAddress, policy.QrAllowedNetworks)
        )
        {
            return DiningResults.Problem(
                statusCode: 403,
                title: "Restaurant presence required",
                detail: "This table QR can only be used from an authorized restaurant network."
            );
        }

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
            return TokenEquals(existing.CustomerAccessToken, suppliedToken)
                ? DiningResults.Ok(QrSessionResponse(table, existing, policy))
                : DiningResults.Conflict(
                    new { detail = "This table already has an active customer QR session." }
                );
        }

        if ((table.RequestGuestCount && guestCount is null) || guestCount is < 1 or > 999)
        {
            return DiningResults.BadRequest(
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
        db.Add(session);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await PublishTableChanged(realtime, session, "Occupied", time);
            return DiningResults.Created(
                $"/api/dining/sessions/{session.Id}",
                QrSessionResponse(table, session, policy)
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
}
