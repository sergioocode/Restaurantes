using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Orders.Contracts.Requests;

public sealed class CreateOrderRequest
{
    public Guid RestaurantId { get; init; }
    public Guid? TableId { get; init; }
    public Guid? DiningSessionId { get; init; }

    [StringLength(80)]
    public string TableLabel { get; init; } = "";

    [StringLength(80)]
    public string CustomerName { get; init; } = "";

    [Required, RegularExpression("^(DineIn|Bar|Takeaway)$")]
    public string ServiceMode { get; init; } = "DineIn";

    [Required, RegularExpression("^(CustomerQr|WaiterMobile|Pos)$")]
    public string Source { get; init; } = "Pos";

    [Required, RegularExpression("^(Immediate|OnAccount)$")]
    public string PaymentTiming { get; init; } = "OnAccount";

    [Required, MinLength(1)]
    public List<CreateOrderLineRequest> Lines { get; init; } = [];
}
