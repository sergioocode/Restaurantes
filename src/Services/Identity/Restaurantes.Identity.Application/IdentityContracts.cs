namespace Restaurantes.Identity.Application;

public sealed record LoginRequest(string Username, string Password);

public sealed record CreateStaffUserRequest(
    string Username,
    string DisplayName,
    string Password,
    Guid RestaurantId,
    string Role
);

public sealed record AssignRestaurantRequest(
    Guid RestaurantId,
    string Role,
    DateTime? ValidFromUtc = null,
    DateTime? ValidUntilUtc = null
);

public sealed record UpdateRestaurantAssignmentRequest(
    string Role,
    bool IsActive,
    DateTime? ValidFromUtc = null,
    DateTime? ValidUntilUtc = null
);

public sealed record IdentityActor(bool IsAdmin, IReadOnlySet<Guid> ManageableRestaurantIds)
{
    public bool CanManage(Guid restaurantId) =>
        IsAdmin && ManageableRestaurantIds.Contains(restaurantId);
}

public sealed record IssuedAccessToken(string Value, DateTime ExpiresAtUtc);

public sealed record LoginUserResponse(Guid Id, string? Username, string DisplayName);

public sealed record LoginRestaurantResponse(
    Guid RestaurantId,
    string Role,
    DateTime ValidFromUtc,
    DateTime? ValidUntilUtc,
    IReadOnlyCollection<string> Permissions
);

public sealed record RestaurantAssignmentResponse(
    Guid RestaurantId,
    string Role,
    bool IsActive,
    DateTime ValidFromUtc,
    DateTime? ValidUntilUtc
);

public sealed record UpdatedRestaurantAssignmentResponse(
    Guid Id,
    Guid RestaurantId,
    string Role,
    bool IsActive,
    DateTime ValidFromUtc,
    DateTime? ValidUntilUtc
);

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc,
    LoginUserResponse User,
    IReadOnlyCollection<string> GlobalRoles,
    IReadOnlyCollection<LoginRestaurantResponse> Restaurants
);

public sealed record IdentityUserResponse(
    Guid Id,
    string? Username,
    string DisplayName,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyCollection<RestaurantAssignmentResponse> Restaurants
);

public sealed record CreatedIdentityUserResponse(
    Guid Id,
    string? Username,
    string DisplayName,
    Guid RestaurantId,
    string Role
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
