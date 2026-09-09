using System.ComponentModel.DataAnnotations;

namespace Restaurantes.RestaurantOperations.Contracts.Requests;

public sealed class UpdateRestaurantRequest
{
    [Required, StringLength(160, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(300, MinimumLength = 3)]
    public string Address { get; init; } = string.Empty;

    public bool IsActive { get; init; }
}
