namespace Restaurantes.Identity.Domain;

public sealed class UserRestaurantAssignment
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public Guid RestaurantId { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime ValidFromUtc { get; set; }
    public DateTime? ValidUntilUtc { get; set; }

    public bool IsCurrentlyActive(DateTime utcNow) =>
        IsActive && ValidFromUtc <= utcNow && (ValidUntilUtc is null || ValidUntilUtc > utcNow);
}
