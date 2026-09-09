namespace Restaurantes.Clients.Backoffice.Pwa.Models;

public sealed record RestaurantAccess(Guid RestaurantId, string Role, List<string> Permissions);
