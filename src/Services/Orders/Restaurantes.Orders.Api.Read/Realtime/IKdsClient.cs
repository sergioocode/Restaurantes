using Restaurantes.Orders.Contracts;

namespace Restaurantes.Orders.Api.Read.Realtime;

public interface IKdsClient
{
    Task OrderUpdated(KdsOrderUpdated notification);
}
