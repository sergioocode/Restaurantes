using System.Security.Claims;

namespace Restaurantes.Dining.Application;

public sealed record DiningRequest(
    ClaimsPrincipal User,
    string? CustomerSessionToken,
    string? Authorization
);
