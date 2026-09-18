namespace Restaurantes.Identity.Application;

public sealed record CreateAccountRequest(
    string Email,
    string Provider,
    string DisplayName,
    string Role,
    Guid? RestaurantId
);

public sealed record UpdateAccountRequest(
    string DisplayName,
    string Role,
    Guid? RestaurantId,
    bool IsActive
);

public sealed record ProviderSettingsResponse(
    string ActiveProvider,
    bool MicrosoftConfigured,
    bool GoogleConfigured
);

public sealed record ChangeProviderRequest(string ActiveProvider);

public sealed record AccountResponse(
    Guid Id,
    string Email,
    string Provider,
    string DisplayName,
    string Role,
    Guid? RestaurantId,
    bool IsActive,
    bool IsLinked
);

public sealed record IssuedAccessToken(string Value, DateTime ExpiresAtUtc);

public sealed record LoginUserResponse(Guid Id, string? Username, string DisplayName);

public sealed record LoginRestaurantResponse(
    Guid RestaurantId,
    string Role,
    DateTime ValidFromUtc,
    DateTime? ValidUntilUtc,
    IReadOnlyCollection<string> Permissions
);

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc,
    LoginUserResponse User,
    IReadOnlyCollection<string> GlobalRoles,
    IReadOnlyCollection<LoginRestaurantResponse> Restaurants
);

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
