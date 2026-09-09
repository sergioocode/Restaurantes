namespace Restaurantes.Orders.Domain;

public enum OrderStatus
{
    Draft = 1,
    Submitted = 2,
    Cancelled = 3,
    InPreparation = 4,
    Ready = 5,
    Delivered = 6,
}
