using Microsoft.EntityFrameworkCore;

namespace Restaurantes.Dining.Api.Write;

public static partial class DiningEndpoints
{
    private static async Task<IResult> PreviewQr(
        string qrCode,
        HttpContext httpContext,
        DiningDbContext db,
        CancellationToken ct
    )
    {
        string code = qrCode.Trim().ToUpperInvariant();
        RestaurantTable? table = await db
            .Tables.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.QrCode == code && x.DeletedAtUtc == null && x.IsActive,
                ct
            );
        if (table is null)
        {
            return Results.NotFound(new { detail = "El QR no está disponible." });
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
                statusCode: 403,
                detail: "Conéctate a la red autorizada del restaurante."
            );
        }

        DiningSession? session = await db
            .Sessions.AsNoTracking()
            .Include(x => x.Orders)
            .SingleOrDefaultAsync(x => x.TableId == table.Id && x.Status == "Open", ct);
        // Only the holder of this session's token may recover its private account.
        return
            session?.Source == "CustomerQr"
            && TokenEquals(
                session.CustomerAccessToken,
                httpContext.Request.Headers["X-Customer-Session-Token"].FirstOrDefault()
            )
            ? Results.Ok(QrSessionResponse(table, session, policy))
            : Results.Ok(
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
}
