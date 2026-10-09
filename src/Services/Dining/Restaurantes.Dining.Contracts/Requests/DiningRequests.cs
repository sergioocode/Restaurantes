using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Dining.Contracts.Requests;

public sealed class CreateTableRequest
{
    public Guid ZoneId { get; init; }

    [Required, StringLength(40, MinimumLength = 1)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(80, MinimumLength = 1)]
    public string Label { get; init; } = string.Empty;
}

public sealed class UpdateTableRequest
{
    public Guid ZoneId { get; init; }

    [Required, StringLength(80, MinimumLength = 1)]
    public string Label { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;
}

public sealed class OpenSessionRequest
{
    [Required, RegularExpression("^(CustomerQr|Comandero|Pos)$")]
    public string Source { get; init; } = "Comandero";
}

public sealed class CheckoutSessionRequest
{
    public Guid IdempotencyKey { get; init; }

    [Range(0, 1_000_000)]
    public decimal TipAmount { get; init; }

    [Range(0, 1_000_000)]
    public decimal? AmountReceived { get; init; }

    public List<CheckoutOrderAllocation> Allocations { get; init; } = [];

    [Required, RegularExpression("^(Card|Cash|Transfer)$")]
    public string Method { get; init; } = "Card";

    [StringLength(120)]
    public string ExternalReference { get; init; } = string.Empty;
}

public sealed record CheckoutOrderAllocation(Guid OrderId, decimal Amount);

public sealed class SaveZoneRequest
{
    public bool RequestGuestCount { get; init; }

    [Required, StringLength(80, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }
}

public sealed record SetGuestCountRequest(int GuestCount);
