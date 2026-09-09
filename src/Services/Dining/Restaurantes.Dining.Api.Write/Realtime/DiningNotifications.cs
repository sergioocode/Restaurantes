using Microsoft.AspNetCore.SignalR;
using Restaurantes.Dining.Application;

namespace Restaurantes.Dining.Api.Write.Realtime;

public sealed class DiningNotifications(IHubContext<DiningHub, IDiningRealtimeClient> hub)
    : IDiningNotifications
{
    public Task TableChanged(DiningTableChanged notification) =>
        hub
            .Clients.Group(DiningHub.RestaurantGroup(notification.RestaurantId))
            .TableChanged(notification);
}
