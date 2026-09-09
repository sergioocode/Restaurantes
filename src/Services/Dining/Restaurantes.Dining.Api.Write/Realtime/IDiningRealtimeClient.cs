using Restaurantes.Dining.Application;

namespace Restaurantes.Dining.Api.Write.Realtime;

public interface IDiningRealtimeClient
{
    Task TableChanged(DiningTableChanged notification);
}
