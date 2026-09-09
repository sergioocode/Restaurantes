using Restaurantes.Orders.Contracts.Events;

namespace Restaurantes.Orders.Api.Read.Realtime;

public interface IKdsClient
{
    Task OrderUpdated(KdsOrderUpdated notification);
}
