using System.ComponentModel.DataAnnotations;

namespace Restaurantes.RestaurantOperations.Contracts;

public sealed class CreateRestaurantRequest
{
    [Required, StringLength(40, MinimumLength = 2)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(160, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(300, MinimumLength = 3)]
    public string Address { get; init; } = string.Empty;
}

public sealed class UpdateRestaurantRequest
{
    [Required, StringLength(160, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(300, MinimumLength = 3)]
    public string Address { get; init; } = string.Empty;

    public bool IsActive { get; init; }
}

public sealed record RestaurantResponse(
    Guid Id,
    string Code,
    string Name,
    string Address,
    bool IsActive,
    int Version,
    DateTime UpdatedAtUtc
);

public sealed record RestaurantChanged(
    Guid RestaurantId,
    string Code,
    string Name,
    string Address,
    bool IsActive,
    int Version,
    DateTime OccurredAtUtc
);
