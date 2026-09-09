namespace Restaurantes.Orders.Domain;

public enum KitchenTicketStatus
{
    Pending = 1,
    InPreparation = 2,
    Ready = 3,
    Cancelled = 4,
    Dispatched = 5,
}
