namespace Restaurantes.Identity.Domain;

public sealed class AuthenticationSettings
{
    public int Id { get; set; } = 1;
    public string ActiveProvider { get; set; } = "Microsoft";
}

public sealed class LoginTicket
{
    public string CodeHash { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = default!;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}
