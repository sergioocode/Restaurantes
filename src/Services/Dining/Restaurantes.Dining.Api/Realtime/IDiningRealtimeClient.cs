using Restaurantes.Dining.Application;

namespace Restaurantes.Dining.Api.Realtime;

public interface IDiningRealtimeClient
{
    Task TableChanged(DiningTableChanged notification);
}
