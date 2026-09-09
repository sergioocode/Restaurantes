namespace Restaurantes.Orders.Application;

public sealed partial class OrderCommandService(
    IOrderWriteStore store,
    IOrderCatalogStore catalog,
    IOrderPaymentStore payments,
    IDiningSessionStore dining,
    TimeProvider time
) { }
