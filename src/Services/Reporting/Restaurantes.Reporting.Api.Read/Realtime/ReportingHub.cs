using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Restaurantes.Security;

namespace Restaurantes.Reporting.Api.Read.Realtime;

[Authorize]
public sealed class ReportingHub : Hub
{
    public async Task JoinRestaurants(Guid[] restaurantIds)
    {
        ClaimsPrincipal? user = Context.User;
        if (user is null)
        {
            throw new HubException("La sesión no está autenticada.");
        }

        foreach (Guid restaurantId in restaurantIds.Distinct())
        {
            if (!user.CanAccessRestaurant(restaurantId, RestaurantPermissions.DashboardRead))
            {
                throw new HubException("No tienes acceso al local solicitado.");
            }
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(restaurantId));
        }
    }

    public static string GroupName(Guid restaurantId)
    {
        return $"restaurant:{restaurantId:N}";
    }
}
