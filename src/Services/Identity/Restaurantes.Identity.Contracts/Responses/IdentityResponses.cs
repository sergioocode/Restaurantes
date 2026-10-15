namespace Restaurantes.Identity.Contracts.Responses;

public sealed record ProviderSettingsResponse(
    string ActiveProvider,
    bool MicrosoftConfigured,
    bool GoogleConfigured
);

public sealed record AccountResponse(
    Guid Id,
    string Email,
    string Provider,
    string DisplayName,
    string Role,
    bool AllRestaurants,
    Guid? RestaurantId,
    bool IsActive,
    bool IsLinked
);

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
    IReadOnlyCollection<string> AllRestaurantsRoles,
    IReadOnlyCollection<string> AllRestaurantsPermissions,
    IReadOnlyCollection<LoginRestaurantResponse> Restaurants
);
