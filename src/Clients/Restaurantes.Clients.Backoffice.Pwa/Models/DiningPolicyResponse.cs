namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed class DiningPolicyResponse
{
    public Guid RestaurantId { get; set; }
    public bool QrRequiresImmediatePayment { get; set; }
    public bool RequireTrustedNetworkForQr { get; set; }
    public bool TakeawayRequiresPrepayment { get; set; }
    public bool AllowCheckoutBeforeKitchenCompletion { get; set; }
    public string[] QrAllowedNetworks { get; set; } = [];
    public int Version { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
