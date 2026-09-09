using System.Net;
using System.Security.Claims;

namespace Restaurantes.Dining.Application;

public sealed record DiningRequest(
    ClaimsPrincipal User,
    IPAddress? RemoteIpAddress,
    string? CustomerSessionToken,
    string? Authorization
);
