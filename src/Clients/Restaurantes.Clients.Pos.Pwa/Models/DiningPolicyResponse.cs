namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record DiningPolicyResponse(
    Guid RestaurantId,
    bool QrRequiresImmediatePayment,
    bool TakeawayRequiresPrepayment
);
