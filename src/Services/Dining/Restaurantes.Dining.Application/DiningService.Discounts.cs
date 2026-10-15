using System.Security.Claims;
using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Application;

public sealed partial class DiningService
{
    public async Task<DiningResult> SetSessionDiscount(
        Guid sessionId,
        SetSessionDiscountRequest request,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        await using IDiningTransaction transaction = await db.BeginTransactionAsync(ct);
        DiningSession? session = await db.LockSessionAsync(sessionId, ct);
        if (session is null)
        {
            return DiningResults.NotFound();
        }
        if (
            !access.CanAccessRestaurant(
                principal,
                session.RestaurantId,
                DiningPermission.DiscountsApply
            )
        )
        {
            return DiningResults.Forbid();
        }
        await db.LoadOrdersAsync(session, ct);
        if (
            session.Status != "Open"
            || session.CheckoutIdempotencyKey is not null
            || session.Orders.Any(order => order.AmountPaid > 0)
        )
        {
            return DiningResults.Conflict(
                new { detail = "El descuento debe aplicarse antes del primer cobro." }
            );
        }

        if (request.Percentage is < 0 or > 99.99m)
        {
            return DiningResults.BadRequest(
                new { detail = "El porcentaje debe estar entre 0 y 99,99." }
            );
        }

        if (session.DiscountPercentage == request.Percentage)
        {
            return DiningResults.Ok(BillResponse(session));
        }

        session.ApplyDiscount(request.Percentage);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DiningStoreException exception)
            when (exception.Failure == DiningStoreFailure.Concurrency)
        {
            return DiningResults.Conflict(
                new
                {
                    detail = "La cuenta cambió mientras se aplicaba el descuento. Actualiza e inténtalo de nuevo.",
                }
            );
        }
        await transaction.CommitAsync(ct);
        return DiningResults.Ok(BillResponse(session));
    }
}
