using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Dining.Application;

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

public sealed class SaveZoneRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }
}

public sealed record SetGuestCountRequest(int GuestCount);
