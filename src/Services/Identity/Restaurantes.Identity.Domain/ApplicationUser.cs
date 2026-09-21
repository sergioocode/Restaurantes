namespace Restaurantes.Identity.Domain;

public sealed class ApplicationUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? ProviderSubject { get; set; }
    public string? TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool AllRestaurants { get; set; }
    public Guid? RestaurantId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}
