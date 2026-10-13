using Restaurantes.Orders.Contracts.Events;

namespace Restaurantes.Orders.Api.Realtime;

public interface IKdsClient
{
    Task OrderUpdated(KdsOrderUpdated notification);

    Task DiningTableUpdated(Guid restaurantId);
}
