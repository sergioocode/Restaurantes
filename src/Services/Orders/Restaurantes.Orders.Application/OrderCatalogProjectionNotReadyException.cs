namespace Restaurantes.Orders.Application;

public sealed class OrderCatalogProjectionNotReadyException : Exception
{
    public OrderCatalogProjectionNotReadyException()
        : base("The current menu configuration has not reached the order service yet.") { }
}
