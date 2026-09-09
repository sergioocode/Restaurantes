using Microsoft.AspNetCore.Identity;

namespace Restaurantes.Identity.Domain;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public List<UserRestaurantAssignment> RestaurantAccesses { get; set; } = [];
}
