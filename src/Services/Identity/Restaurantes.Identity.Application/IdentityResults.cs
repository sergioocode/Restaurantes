namespace Restaurantes.Identity.Application;

public sealed record IssuedAccessToken(string Value, DateTime ExpiresAtUtc);

public enum IdentityOutcome
{
    Ok,
    Created,
    NoContent,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
}

public sealed record IdentityResult(
    IdentityOutcome Outcome,
    object? Value = null,
    string? Location = null
);
