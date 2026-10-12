namespace Restaurantes.RestaurantOperations.Contracts.Requests;

public sealed class SetRestaurantActiveRequest
{
    public bool IsActive { get; init; }
}
