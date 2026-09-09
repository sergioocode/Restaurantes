namespace Restaurantes.Clients.Pos.Pwa.Models;

public sealed record RestaurantAccess(Guid RestaurantId, string Role, List<string> Permissions);
