namespace Restaurantes.Orders.Contracts;

public enum OrderLifecycleStatus
{
    Unknown,
    Draft,
    Submitted,
    InPreparation,
    Ready,
    Delivered,
    Cancelled,
}
