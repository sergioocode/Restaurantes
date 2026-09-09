namespace Restaurantes.Clients.Commander.Pwa.Models;

public sealed record RestaurantAccess(Guid RestaurantId, string Role, List<string> Permissions);
