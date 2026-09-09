namespace Restaurantes.Dining.Domain;

public sealed class DiningRestaurantPolicy
{
    public Guid RestaurantId { get; set; }
    public bool QrRequiresImmediatePayment { get; set; } = true;
    public bool RequireTrustedNetworkForQr { get; set; }
    public bool TakeawayRequiresPrepayment { get; set; } = true;
    public bool AllowCheckoutBeforeKitchenCompletion { get; set; }
    public string QrAllowedNetworks { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public int Version { get; set; } = 1;
}
