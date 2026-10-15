namespace Restaurantes.Identity.Contracts.Requests;

public sealed record CreateAccountRequest(
    string Email,
    string DisplayName,
    string Role,
    bool AllRestaurants,
    Guid? RestaurantId
);

public sealed record UpdateAccountRequest(
    string DisplayName,
    string Role,
    bool AllRestaurants,
    Guid? RestaurantId,
    bool IsActive
);
