using System.Security.Claims;
using Restaurantes.Dining.Domain;

namespace Restaurantes.Dining.Application;

public sealed partial class DiningService
{
    public async Task<DiningResult> GetPolicy(
        Guid restaurantId,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        if (!access.CanAccessRestaurant(principal, restaurantId, DiningPermission.TablesRead))
        {
            return DiningResults.Forbid();
        }

        DiningRestaurantPolicy? policy = await db.ReadPolicyAsync(restaurantId, ct);
        return DiningResults.Ok(PolicyResponse(restaurantId, policy));
    }

    public async Task<DiningResult> UpdatePolicy(
        Guid restaurantId,
        UpdateDiningPolicyRequest request,
        ClaimsPrincipal principal,
        CancellationToken ct
    )
    {
        if (!access.CanAccessRestaurant(principal, restaurantId, DiningPermission.TablesManage))
        {
            return DiningResults.Forbid();
        }

        if (
            !QrNetworkAccess.TryNormalize(
                request.QrAllowedNetworks,
                out string allowedNetworks,
                out string? networkError
            )
        )
        {
            return DiningResults.BadRequest(new { detail = networkError });
        }
        if (request.RequireTrustedNetworkForQr && allowedNetworks.Length == 0)
        {
            return DiningResults.BadRequest(
                new
                {
                    detail = "At least one allowed IP address or CIDR network is required when QR network validation is enabled.",
                }
            );
        }

        DiningRestaurantPolicy? policy = await db.FindPolicyAsync(restaurantId, ct);
        if (policy is null)
        {
            policy = new DiningRestaurantPolicy { RestaurantId = restaurantId, Version = 1 };
            db.Add(policy);
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
        return DiningResults.Ok(PolicyResponse(restaurantId, policy));
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
}
